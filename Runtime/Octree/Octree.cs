using UnityEngine;
using UnityEditor;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    public class Octree : ScriptableObject {
        AABB bbox;
        public NodeEntry[] nodes = null;
        public bool Loaded { get { return nodes != null; } }
        public string dirPath { get; private set; }
        public FileStream fs;

        public Node root;

        public Octree(string dirPath) {
            this.dirPath = dirPath;
        }

        public void LoadTree() {
            byte[] metaBytes = File.ReadAllBytes($"{dirPath}/meta.dat");
            int nodeCount = metaBytes.Length / 36;
            nodes = new NodeEntry[nodeCount];
            for (int i = 0; i < nodeCount; i ++)
                nodes[i] = new NodeEntry(metaBytes, i * 36);
            fs = File.OpenRead($"{dirPath}/octree.dat");

            // Construct empty tree hierarchy
            root = nodes[0].ToNode();

            // At end of expandEntry call, idx is just past last descendent of root
            void expandEntry(Node root, ref int idx) {
                int rootIdx = idx;
                NodeEntry entry = nodes[rootIdx];
                idx++;

                if (entry.descendentCount == 0)
                    return;

                for (int i = 0; i < 8; i++) {
                    Node child = nodes[idx].ToNode();
                    int childIdx = 
                        (child.bbox.Min.x == entry.bbox.Min.x ? 0 : 4) + 
                        (child.bbox.Min.y == entry.bbox.Min.y ? 0 : 2) + 
                        (child.bbox.Min.z == entry.bbox.Min.z ? 0 : 1);

                    root.children[childIdx] = child;
                    expandEntry(child, ref idx);

                    if (idx > rootIdx + entry.descendentCount) // No more children
                        break;
                }
            }

            int idx = 0;
            expandEntry(root, ref idx);
        }

        // Get masks separately from points to avoid needless IO
        public bool[] GetTreeMask(Plane[] frustum, Vector3 camPosition, float fov, float screenHeight) {
            bool[] mask = new bool[nodes.Length];
            // Populate bool array with true if node is visible, false otherwise
            int i = 0;
            while (i < nodes.Length) {
                NodeEntry n = nodes[i];
                double distance = (n.bbox.Center - camPosition).magnitude;
                double slope = Math.Tan(fov / 2 * Mathf.Deg2Rad);
                double projectedSize = (screenHeight / 2.0) * (n.bbox.Size.magnitude / 2) / (slope * distance);

                float minNodeSize = 10;

                if (!Utils.TestPlanesAABB(frustum, n.bbox) || projectedSize < minNodeSize) { // If bbox outside camera or too small
                    mask[i] = false;
                    i += (int)nodes[i].descendentCount;
                } else {    // Set to render and step into children
                    mask[i] = true;
                    i++;
                }
            }

            return mask;
        }

        public Point[] ReadNode(int nidx)
        {
            NodeEntry n = nodes[nidx];
            fs.Seek(n.offset, SeekOrigin.Begin);
            byte[] pBytes = new byte[n.pointCount * 15];
            fs.Read(pBytes);
            return Point.ToPoints(pBytes);
        }

        public Point[] GetLoadedPoints() {
            List<Point> result = new();

            foreach (NodeEntry n in nodes) {
                lock (n) {
                    if (n.points != null)
                        result.AddRange(n.points);
                }
            }

            return result.ToArray();
        }

        public async Task BuildTree(PointCloudData data, int treeDepth, Dispatcher dispatcher, Action cb) {
            Task chunkTask;

            try {
                string name = dirPath.Split(Path.DirectorySeparatorChar)[dirPath.Split(Path.DirectorySeparatorChar).Length-1];
                Debug.Log($"{name}: Building Tree");
                if (!data.BoundsPopulated) 
                    await data.PopulateBounds();

                Debug.Log($"{name}: Got Bounds");

                this.bbox = new AABB(data.MinPoint, data.MaxPoint);

                // chunkTask = Chunker.MakeChunks(data, dirPath, dispatcher);
                // await chunkTask;

                Debug.Log($"{name}: Chunking done");

                try {
                    QueuedWriter qw = new($"{dirPath}/octree.dat");
                    Indexer idxr = new(qw, dispatcher);

                    string[] chunkPaths = Directory.GetFiles($"{dirPath}/chunks");

                    int chunkDepth = 0;
                    for (int c = 0; c < chunkPaths.Length; c++)
                        chunkDepth = Math.Max(chunkDepth, Path.GetFileNameWithoutExtension(chunkPaths[c]).Length-1);

                    Node[] chunkRoots = new Node[chunkPaths.Length];
                    for (int i = 0; i < chunkPaths.Length; i++)
                        chunkRoots[i] = new Node();

                    // DO INDEXING

                    List<Task> indexTasks = new();

                    for (int i = 0; i < chunkPaths.Length; i++)
                    {
                        while (idxr.FootprintMB > 1024)
                            Thread.Sleep(50);

                        await idxr.IndexChunk(data, chunkRoots[i], chunkPaths[i]);
                        // indexTasks.Add(idxr.IndexChunk(data, chunkRoots[i], chunkPaths[i]));
                        Debug.Log($"Landmark chunk {i}/{chunkPaths.Length}");
                        // Debug.Log($"{i} chunks added");
                    }

                    // Debug.Log("Done adding shit");

                    Task.WaitAll(indexTasks.ToArray());

                    Debug.Log($"{name}: Indexing done");

                    // CONSTRUCT GLOBAL TREE

                    // Debug.Log("Constructing global tree");

                    Node root = new();

                    int d = chunkPaths.Select(p => Path.GetFileNameWithoutExtension(p).Length-1).Max();
                    // Debug.Log($"Got depth {d}");

                    AABB[][] bboxPyramid = new AABB[chunkDepth+1][];
                    for (int l = 0; l < chunkDepth+1; l++)
                        bboxPyramid[l] = this.bbox.Subdivide((int)Mathf.Pow(2,l));

                    Queue<Node> queue = new();
                    queue.Enqueue(root);
                    for (int l = 0; l < chunkDepth+1; l++)
                    {
                        int n;
                        for (n = 0; n < (int)Mathf.Pow(8, l); n++)
                        {
                            Node node = queue.Dequeue();
                            node.bbox = bboxPyramid[l][n];
                            node.children = new Node[8];
                            for (int i = 0; i < 8; i++) { 
                                node.children[i] = new();
                                queue.Enqueue(node.children[i]);
                            }
                        }
                        // Debug.Log($"l{l} with {n} nodes");
                    }

                    for (int c = 0; c < chunkPaths.Length; c++)
                    {
                        string chunkID = Path.GetFileNameWithoutExtension(chunkPaths[c])[1..];
                        Node curr = root;
                        for (int i = 0; i < chunkID.Length-1; i++)
                            curr = curr.children[chunkID[i] - '0'];

                        curr.children[chunkID[chunkID.Length-1] - '0'] = chunkRoots[c];

                        // if (Vector3.Max(curr.bbox.Max, chunkRoots[c].bbox.Max) != curr.bbox.Max || Vector3.Min(curr.bbox.Min, chunkRoots[c].bbox.Min) != curr.bbox.Min)
                        //     Debug.LogError("Global issue");
                    }

                    Sampling.Sample(root, (Node node) => {
                        node.offset = (uint)qw.Enqueue(Point.ToBytes(node.points, 0, (int)node.pointCount));
                        node.points = null;
                    });

                    root.offset = (uint)qw.Enqueue(Point.ToBytes(root.points, 0, (int)root.pointCount));
                    // root.points = null;

                    void traverse(Node node, Action<Node> cb) {
                        foreach (Node child in node.children)
                            if (child != null)
                                traverse(child, cb);
                        cb(node);
                    }

                    int[] xRangeCounts = new int[24];
                    foreach (Point pt in root.points)
                        xRangeCounts[12+Mathf.FloorToInt(pt.pos.x)]++;

                    while (qw.QueueSize > 0)
                        Thread.Sleep(500);

                    // FileStream fs2 = File.Open($"{dirPath}/octree.dat", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    // fs2.Seek(root.offset, SeekOrigin.Begin);
                    // byte[] pBytes = new byte[root.pointCount * 15];
                    // fs2.Read(pBytes);
                    // Point[] pts = Point.ToPoints(pBytes);

                    // foreach (Point pt in pts)
                    //     if (!root.points.Contains(pt))
                    //         Debug.Log("Bad!");

                    // foreach (Point pt in root.points)
                    //     if (!pts.Contains(pt))
                    //         Debug.Log("Bad!");

                    // traverse(root, (Node n) => { n.children = null; });

                    List<NodeEntry> nodeList = new();

                    xRangeCounts = xRangeCounts;

                    uint memFootprint = 0;

                    uint AddEntry(Node n)
                    // void AddEntry(Node n)
                    {
                        Debug.Log("AddEntry");
                        NodeEntry entry = new NodeEntry
                        {
                            pointCount = n.pointCount,
                            offset = n.offset,
                            descendentCount = 0,
                            // childFlags = n.children.Select(c => c != null).ToArray(),
                            bbox = n.bbox
                        };

                        memFootprint += 36; // Size of entry in bytes?

                        if (memFootprint > 1024 * 1024 * 1024)  // 1 GB
                            Debug.Log("Here!");

                        nodeList.Add(entry);

                        if (n.IsLeaf)
                            n = n;

                        // List<Task> childTasks = new();

                        for (int i = 0; i < 8; i++)
                            if (n.children[i] != null)
                                entry.descendentCount += AddEntry(n.children[i]) + 1;
                                // childTasks.Add(AddEntry(n.children[i]));

                        // Task.WaitAll(childTasks.ToArray());
                        // foreach (Task<uint> t in childTasks)
                        //     entry.descendentCount += t.Result + 1;

                        return entry.descendentCount;
                    }

                    AddEntry(root);

                    nodes = nodeList.ToArray();

                    FileStream fs = File.Create($"{dirPath}/meta.dat");
                    for (int n = 0; n < nodes.Length; n++) {
                        Debug.Log("Write node");
                        fs.Write(nodes[n].ToBytes());
                    }

                    Debug.Log($"{name}: Stitching done");
                    cb();
                    Debug.Log($"{name}: All done");
                    AssetDatabase.ForceReserializeAssets();
                } catch (Exception e)
                {
                    Debug.Log($"Exception. Message: {e.Message}, Backtrace: {e.StackTrace}, Inner: {e.InnerException}");
                    // Debug.Log(e.InnerExceptions[0].StackTrace);
                }
            } catch (ThreadAbortException e) {

            }
        }
    }
}