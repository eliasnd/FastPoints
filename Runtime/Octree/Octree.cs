using UnityEngine;

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
        NodeEntry[] nodes;
        string dirPath;

        public void ReadTree() {
            byte[] metaBytes = File.ReadAllBytes($"{dirPath}/meta.dat");
            for (int i = 0; i < metaBytes.Length / 36; i ++)
                nodes[i] = new NodeEntry(metaBytes, i * 36);
        }

        public async Task BuildTree(PointCloudData data, int treeDepth, string dirPath, Dispatcher dispatcher, Action cb) {
            string name = dirPath.Split(Path.DirectorySeparatorChar)[dirPath.Split(Path.DirectorySeparatorChar).Length-1];

            if (!data.BoundsPopulated) 
                await data.PopulateBounds();

            Debug.Log($"{name}: Got Bounds");

            this.dirPath = dirPath;
            this.bbox = new AABB(data.MinPoint, data.MaxPoint);

            await Chunker.MakeChunks(data, dirPath, dispatcher);

            Debug.Log($"{name}: Chunking done");

            Task indexTask;
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

                    // await idxr.IndexChunk(data, chunkRoots[i], chunkPaths[i]);
                    indexTasks.Add(idxr.IndexChunk(data, chunkRoots[i], chunkPaths[i]));
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
                    node.offset = (uint)qw.Enqueue(Point.ToBytes(node.points));
                });

                void traverse(Node node, Action<Node> cb) {
                    foreach (Node child in node.children)
                        if (child != null)
                            traverse(child, cb);
                    cb(node);
                }

                // traverse(root, (Node n) => { n.children = null; });

                List<NodeEntry> nodeList = new();

                uint AddEntry(Node n)
                {

                    NodeEntry entry = new NodeEntry
                    {
                        pointCount = n.pointCount,
                        offset = n.offset,
                        descendentCount = 0,
                        bbox = n.bbox
                    };

                    nodeList.Add(entry);

                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            entry.descendentCount += AddEntry(n.children[i]);

                    return entry.descendentCount;
                }

                AddEntry(root);

                nodes = nodeList.ToArray();

                FileStream fs = File.Create($"{dirPath}/meta.dat");
                for (int n = 0; n < nodes.Length; n++)
                    fs.Write(nodes[n].ToBytes());

                Debug.Log($"{name}: Stitching done");
                cb();
                Debug.Log($"{name}: All done");
            } catch (Exception e)
            {
                Debug.Log($"Exception. Message: {e.Message}, Backtrace: {e.StackTrace}, Inner: {e.InnerException}");
                // Debug.Log(e.InnerExceptions[0].StackTrace);
            }
        }
    }

    struct BuildTreeParams {
        public Octree tree;
        public PointCloudData data;
        public Dispatcher dispatcher;
        public string dirPath;
        public Action cb;
        public BuildTreeParams(Octree tree, string dirPath, PointCloudData data, Dispatcher dispatcher, Action cb) {
            this.tree = tree;
            this.dirPath = dirPath;
            this.data = data;
            this.dispatcher = dispatcher;
            this.cb = cb;
        }
    }
}