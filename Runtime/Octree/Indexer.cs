using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;

using Random = System.Random;
using Debug = UnityEngine.Debug;

namespace FastPoints {
    public class Indexer {

        static int treeDepth = 3;
        static int maxNodeSize = 100000;
        static int subsampleSize = 50000;

        int octreeDepth = 0;

        QueuedWriter qw;
        Dispatcher dispatcher;

        uint footprint;
        object footprintLock = new object();

        public uint FootprintMB
        {
            get
            {
                lock (footprintLock)
                {
                    return footprint / (1024 * 1024);
                }
            }
        }

        Random rand;

        public Indexer(QueuedWriter qw, Dispatcher dispatcher)
        {
            this.qw = qw;
            this.dispatcher = dispatcher;
            this.rand = new Random();
        }

        public async Task IndexChunk(PointCloudData data, Node root, string chunkPath) { 
            Debug.Log("I0");

            // Get BBOX better eventually
            string chunkCode = Path.GetFileNameWithoutExtension(chunkPath);
            AABB curr = new AABB(data.MinPoint, data.MaxPoint);

            // Test bbox subdivide
            /* AABB[] test1 = curr.Subdivide(8);
            Queue<AABB> testQueue = new();
            testQueue.Enqueue(curr);
            for (int i = 0; i < 3; i++) {
                for (int b = 0; b < Mathf.Pow(2, i); b++) {
                    AABB bb = testQueue.Dequeue();
                    foreach (AABB bbox in bb.Subdivide(2))
                        testQueue.Enqueue(bbox);
                }
            }
            AABB[] test2 = testQueue.ToArray();
            int tmp;
            foreach (AABB bbox in test1) {
                bool found = false;
                foreach (AABB bbox2 in test2) {
                    if (bbox.Equals(bbox2))
                        found = true;
                }
                if (!found)
                    tmp = 0;
                else
                    tmp = 0;
            } */


            for (int i = 1; i < chunkCode.Length; i++) {
                int idx = chunkCode[i] - '0';
                curr = curr.child(idx);
            }



            lock (footprintLock)
            {
                footprint += (uint)new FileInfo(chunkPath).Length * 2;
            }

            Point[] points = Point.ToPoints(File.ReadAllBytes(chunkPath));

            // root.bbox = new AABB(new Vector3(curr.Min.x-1E-5f, curr.Min.y-1E-5f, curr.Min.z-1E-5f), new Vector3(curr.Max.x+1E-5f, curr.Max.y+1E-5f, curr.Max.z+1E-5f));
            root.bbox = new AABB(new Vector3(curr.Min.x, curr.Min.y, curr.Min.z), new Vector3(curr.Max.x, curr.Max.y, curr.Max.z));
            root.name = "n";
            Debug.Log(chunkPath + ", " + root.bbox.ToString());

            // foreach (Point pt in points) {
            //     if (Vector3.Min(root.bbox.Min, pt.pos) != root.bbox.Min || Vector3.Max(root.bbox.Max, pt.pos) != root.bbox.Max)
            //         Debug.Log("Problem!");
            // }

            await IndexPoints(root, points);
        }

        public async Task IndexPoints(Node root, Point[] points) {
            Stopwatch watch = new Stopwatch();
            TimeSpan currTime = new();
            long i1 = watch.ElapsedMilliseconds - currTime.Milliseconds;
            currTime = watch.Elapsed;
            
            // DEBUG_CODE
            Debug.Log(CheckBBox(points, root.bbox));

            long i2 = watch.ElapsedMilliseconds - currTime.Milliseconds;
            currTime = watch.Elapsed;

            await Task.Run(async () =>
            {
                int gridSize = (int)Mathf.Pow(2, treeDepth-1);
                int gridTotal = gridSize * gridSize * gridSize;
                int threadBudget = Mathf.CeilToInt(points.Length / 4096f);

                int[] mortonIndices = new int[gridSize * gridSize * gridSize];

                for (int x = 0; x < gridSize; x++)
                for (int y = 0; y < gridSize; y++)
                for (int z = 0; z < gridSize; z++)
                    mortonIndices[x * gridSize * gridSize + y * gridSize + z] = Utils.MortonEncode(z, y, x);

                ComputeShader computeShader;

                // float[] minPoint = new float[] { root.bbox.Min.x-1E-5f, root.bbox.Min.y-1E-5f, root.bbox.Min.z-1E-5f };
                // float[] maxPoint = new float[] { root.bbox.Max.x+1E-5f, root.bbox.Max.y+1E-5f, root.bbox.Max.z+1E-5f };
                float[] minPoint = new float[] { root.bbox.Min.x, root.bbox.Min.y, root.bbox.Min.z };
                float[] maxPoint = new float[] { root.bbox.Max.x, root.bbox.Max.y, root.bbox.Max.z };


                // Action outputs
                uint[] nodeCounts = new uint[gridTotal];
                uint[] nodeStarts = new uint[gridTotal];
                Point[] sortedPoints = new Point[points.Length];

                long i3 = watch.ElapsedMilliseconds - currTime.Milliseconds;
                currTime = watch.Elapsed;

                // DEBUG_CODE
                int GetChunk(Point p) {
                    float threshold = 1f / gridSize; // Used to calculate which index for each dimension

                    int x = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.x, root.bbox.Max.x, p.pos.x) / threshold), gridSize-1);
                    int y = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.y, root.bbox.Max.y, p.pos.y) / threshold), gridSize-1);
                    int z = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.z, root.bbox.Max.z, p.pos.z) / threshold), gridSize-1);
                    return mortonIndices[x * gridSize * gridSize + y * gridSize + z];
                }

                List<Point>[] sortedNodesCPU = new List<Point>[gridTotal];
                for (int i = 0; i < gridTotal; i++)
                    sortedNodesCPU[i] = new();

                foreach (Point pt in points)
                    sortedNodesCPU[GetChunk(pt)].Add(pt);

                await dispatcher.EnqueueAsync(() => {
                    // INITIALIZE

                    computeShader = (ComputeShader)Resources.Load("CountAndSort");

                    computeShader.SetInt("_BatchSize", points.Length);
                    computeShader.SetInt("_NodeCount", gridSize);
                    computeShader.SetInt("_ThreadBudget", threadBudget);
                    computeShader.SetFloats("_MinPoint", minPoint);
                    computeShader.SetFloats("_MaxPoint", maxPoint);

                    ComputeBuffer mortonBuffer = new ComputeBuffer(gridTotal, sizeof(int));
                    mortonBuffer.SetData(mortonIndices);
                    computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_MortonIndices", mortonBuffer);
                    computeShader.SetBuffer(computeShader.FindKernel("SortLinear"), "_MortonIndices", mortonBuffer);

                    ComputeBuffer pointBuffer = new ComputeBuffer(points.Length, Point.size);
                    pointBuffer.SetData(points);

                    // COUNT

                    ComputeBuffer countBuffer = new ComputeBuffer(gridTotal, sizeof(uint));

                    int countHandle = computeShader.FindKernel("CountPoints");
                    computeShader.SetBuffer(countHandle, "_Points", pointBuffer);
                    computeShader.SetBuffer(countHandle, "_Counts", countBuffer);

                    computeShader.Dispatch(countHandle, 64, 1, 1);

                    countBuffer.GetData(nodeCounts);

                    nodeStarts[0] = 0;
                    for (int i = 1; i < nodeStarts.Length; i++)
                        nodeStarts[i] = nodeStarts[i-1] + nodeCounts[i-1];

                    // SORT

                    ComputeBuffer sortedBuffer = new ComputeBuffer(points.Length, Point.size);
                    ComputeBuffer startBuffer = new ComputeBuffer(gridTotal, sizeof(uint));
                    startBuffer.SetData(nodeStarts);
                    ComputeBuffer offsetBuffer = new ComputeBuffer(gridTotal, sizeof(uint));
                    uint[] chunkOffsets = new uint[gridTotal];   // Initialize to 0s?
                    offsetBuffer.SetData(chunkOffsets);

                    int sortHandle = computeShader.FindKernel("SortLinear");  // Sort points into array
                    computeShader.SetBuffer(sortHandle, "_Points", pointBuffer);
                    computeShader.SetBuffer(sortHandle, "_SortedPoints", sortedBuffer); // Must output adjacent nodes in order
                    computeShader.SetBuffer(sortHandle, "_ChunkStarts", startBuffer);
                    computeShader.SetBuffer(sortHandle, "_ChunkOffsets", offsetBuffer);

                    computeShader.Dispatch(sortHandle, 64, 1, 1);

                    sortedBuffer.GetData(sortedPoints);

                    // SUBSAMPLE EVENTUALLY

                    // CLEAN UP

                    sortedBuffer.Release();
                    pointBuffer.Release();
                    startBuffer.Release();
                    offsetBuffer.Release();
                    countBuffer.Release();
                    pointBuffer.Release();
                }); 

                long i4 = watch.ElapsedMilliseconds - currTime.Milliseconds;
                currTime = watch.Elapsed;

                // DEBUG_CODE

                for (int i = 0; i < gridTotal; i++)
                    if (nodeCounts[i] != sortedNodesCPU[i].Count)
                        Debug.LogError("Count issue!");

                // MAKE SUM PYRAMID

                uint[][] sumPyramid = new uint[treeDepth][];
                sumPyramid[treeDepth-1] = nodeCounts;

                for (int level = treeDepth-2; level >= 0; level--) {
                    int levelDim = (int)Mathf.Pow(2, level);
                    uint[] currLevel = new uint[levelDim * levelDim * levelDim];

                    uint[] lastLevel = sumPyramid[level+1];

                    for (int x = 0; x < levelDim; x++)
                        for (int y = 0; y < levelDim; y++)
                            for (int z = 0; z < levelDim; z++)
                            {
                                int chunkIdx = Convert.ToInt32(Utils.MortonEncode(z, y, x));
                                // Debug.Log($"Test: Morton code for {x}, {y}, {z} = {chunkIdx}");
                                int childIdx = Convert.ToInt32(Utils.MortonEncode(2*z, 2*y, 2*x));
                                currLevel[chunkIdx] =
                                    lastLevel[childIdx] + lastLevel[childIdx+1] + lastLevel[childIdx+2] + lastLevel[childIdx+3] +
                                    lastLevel[childIdx+4] + lastLevel[childIdx+5] + lastLevel[childIdx+6] + lastLevel[childIdx+7];
                            }

                    sumPyramid[level] = currLevel;
                }

                uint[][] offsetPyramid = new uint[treeDepth][];
                for (int l = 0; l < treeDepth; l++)
                {
                    uint currOffset = 0;
                    offsetPyramid[l] = new uint[sumPyramid[l].Length];
                    for (int i = 0; i < offsetPyramid[l].Length; i++)
                    {
                        offsetPyramid[l][i] = currOffset;
                        currOffset += sumPyramid[l][i];
                    }
                }

                long i5 = watch.ElapsedMilliseconds - currTime.Milliseconds;
                currTime = watch.Elapsed;

                // TEST SORTING

                // DEBUG_CODE for constructing sorted pyramid and testing if node offset linear stuff matches
                uint[] testCounts = new uint[gridTotal];
                AABB[] leafBB = root.bbox.Subdivide(gridSize);

                // for (int i = 0; i < gridTotal; i++)
                //     Debug.Log(CheckBBox(sortedNodesCPU[i].ToArray(), leafBB[i]));

                // float threshold = 1f / gridSize;
                // foreach (Point pt in points) {
                //     int x = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.x, root.bbox.Max.x, pt.pos.x) / threshold), gridSize-1);
                //     int y = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.y, root.bbox.Max.y, pt.pos.y) / threshold), gridSize-1);
                //     int z = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.z, root.bbox.Max.z, pt.pos.z) / threshold), gridSize-1);
                //     // return 0 |= splitBy3(x) | splitBy3(y) << 1 | splitBy3(z) << 2;
                //     int idx = mortonIndices[x * gridSize * gridSize + y * gridSize + z];
                //     if (Vector3.Max(pt.pos, leafBB[idx].Max) != leafBB[idx].Max || Vector3.Min(pt.pos, leafBB[idx].Min) != leafBB[idx].Min)
                //         Debug.LogError("Found point outside");
                //     testCounts[idx]++;
                // }

                uint offset = 0;
                for (int i = 0; i < gridTotal; i++) {
                    uint size = sumPyramid[treeDepth-1][i];
                    for (int j = 0; j < size; j++) {
                        // if (Vector3.Max(sortedPoints[offset].pos, leafBB[i].Max) != leafBB[i].Max || Vector3.Min(sortedPoints[offset].pos, leafBB[i].Min) != leafBB[i].Min)
                        //     Debug.LogError("Found point outside");
                        offset++;
                    }
                }

                AABB[][] bboxPyramid = new AABB[treeDepth][];
                for (int l = 0; l < treeDepth; l++)
                    bboxPyramid[l] = root.bbox.Subdivide((int)Mathf.Pow(2,l));

                List<Point>[][] cpuSortedPyramid = new List<Point>[treeDepth][];
                cpuSortedPyramid[treeDepth-1] = sortedNodesCPU;
                for (int l = treeDepth-2; l >= 0; l--) {
                    cpuSortedPyramid[l] = new List<Point>[(int)Mathf.Pow(8, l)];
                    for (int n = 0; n < cpuSortedPyramid[l].Length; n++) {
                        cpuSortedPyramid[l][n] = new();
                        for (int child = 0; child < 8; child++)
                            cpuSortedPyramid[l][n].AddRange(cpuSortedPyramid[l+1][n*8+child]);
                    }
                }

                // for (int l = 0; l < treeDepth; l++)
                //     for (int n = 0; n < cpuSortedPyramid[l].Length; n++)
                //         Debug.Log(CheckBBox(cpuSortedPyramid[l][n].ToArray(), bboxPyramid[n][l]));

                // AABB[][] bboxPyramidAlt = new AABB[treeDepth][];
                // bboxPyramidAlt[0] = new AABB[] { root.bbox };
                // for (int l = 1; l < treeDepth; l++) {
                //     bboxPyramidAlt[l] = new AABB[(int)Mathf.Pow(8, l)];
                //     for (int n = 0; n < bboxPyramidAlt[l-1].Length; l++)
                //         for (int i = 0; i < 8; i++)
                //             bboxPyramidAlt[l][n*8+i] = bboxPyramidAlt[l-1][n].child(i);
                // }
                    

                // CREATE NODE REFERENCES

                List<NodeReference> nodeRefs = new();

                NodeReference rootRef = new NodeReference();
                rootRef.name = "";
                rootRef.level = 0;
                rootRef.x = 0;
                rootRef.y = 0;
                rootRef.z = 0;
                rootRef.pointCount = sumPyramid[0][0];

                Stack<NodeReference> stack = new();
                stack.Push(rootRef);

                int c = 0;
                while (stack.Count > 0) {
                    c++;
                    NodeReference nr = stack.Pop();
                    int nodeIdx = Utils.MortonEncode(nr.z, nr.y, nr.x);
                    uint numPoints = sumPyramid[nr.level][nodeIdx];
                    if (nr.level == treeDepth-1) {
                        if (numPoints > 0)
                            nodeRefs.Add(nr);
                    } else if (numPoints > maxNodeSize) {
                        int childIdx = Utils.MortonEncode(2*nr.z, 2*nr.y, 2*nr.x);
                        for (int i = 0; i < 8; i++) {
                            uint count = sumPyramid[nr.level+1][childIdx+i];

                            if (count > 0) {
                                NodeReference child = new();
                                child.level = nr.level+1;
                                child.name = nr.name + i;
                                child.offset = offsetPyramid[nr.level + 1][childIdx+i];
                                Debug.Log($"Made node {child.name} with level {child.level}, idx {childIdx+i}");
                                child.pointCount = count;
                                // child.bbox = bboxPyramid[nr.level + 1][childIdx+i];
                                
                                child.x = 2 * nr.x + ((i & 0b100) >> 2);
                                child.y = 2 * nr.y + ((i & 0b010) >> 1);
                                child.z = 2 * nr.z + ((i & 0b001) >> 0);

                                if (Utils.MortonEncode(child. z, child.y, child.x) != childIdx+i)
                                    Debug.LogError($"Mismatch: computed {Utils.MortonEncode(child.z, child.y, child.x)}, should be {childIdx+i}");

                                stack.Push(child);
                            }
                        }
                    } else if (numPoints > 0) {
                        nodeRefs.Add(nr);
                    }
                }

                long i6 = watch.ElapsedMilliseconds - currTime.Milliseconds;
                currTime = watch.Elapsed;

                // CREATE NODES

                Node ExpandReference(NodeReference nr)
                {
                    string startName = root.name;
                    string fullName = startName + nr.name;

                    Node currNode = root;
                    for (int i = startName.Length; i < fullName.Length; i++)
                    {
                        int idx = fullName[i] - '0';

                        if (currNode.children[idx] == null)
                        {
                            Node child = new();
                            // child.bbox = currNode.bbox.Subdivide(2)[idx];
                            // child.bbox = currNode.bbox.child(idx);
                            child.bbox = bboxPyramid[nr.level][Utils.MortonEncode(nr.z, nr.y, nr.x)];
                            child.name = currNode.name + idx;
                            child.children = new Node[8];

                            currNode.children[idx] = child;
                            currNode = child;
                        } else
                        {
                            currNode = currNode.children[idx];
                        }
                    
                    }

                    bboxPyramid = bboxPyramid; // DEBUG_CODE

                    return currNode;
                }

                List<Task> recursiveCalls = new();

                foreach (NodeReference nr in nodeRefs)
                {
                    Node node = ExpandReference(nr);
                    node.pointCount = nr.pointCount;
                    node.points = new Point[node.pointCount];

                    // node.bbox = nr.bbox;

                    // Array.Copy(points, nr.offset, node.points, 0, nr.pointCount);

                    // DEBUG_CODE
                    if (nr == null)
                        Debug.Log("HEre");
                    if (nr.name == null)
                        Debug.Log("Here");

                    node.points = cpuSortedPyramid[nr.level][Utils.MortonEncode(nr.z, nr.y, nr.x)].ToArray();
                    Debug.Log($"Got points for {node.name} from idx {Utils.MortonEncode(nr.z, nr.y, nr.x)}");

                    if (node.points.Length != node.pointCount)
                        Debug.LogError("Biiig!");
                    
                    // foreach (Point pt in node.points)
                    //     if (!cpuSortedPyramid[l][idx].Contains(pt))
                    //         Debug.LogError("Mismatch in CPU and GPU");

                    // Debug.Log("Expand: " + CheckBBox(node.points, node.bbox));

                    // Debug.Log($"Created node {node.name} with bbox ({node.bbox.Min.x}, {node.bbox.Min.y}, {node.bbox.Min.z}), ({node.bbox.Max.x}, {node.bbox.Max.y}, {node.bbox.Max.z})");
                    Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    foreach (Point pt in node.points) {
                        min = Vector3.Min(min, pt.pos);
                        max = Vector3.Max(max, pt.pos);
                        /* if (Vector3.Min(node.bbox.Min, pt.pos) != node.bbox.Min || Vector3.Max(node.bbox.Max, pt.pos) != node.bbox.Max)
                        Debug.LogError($"Found point ({pt.pos.x}, {pt.pos.y}, {pt.pos.z}) outside bbox in node {node.name}"); */
                    }

                    if (node.pointCount > maxNodeSize)
                        recursiveCalls.Add(IndexPoints(node, node.points));
                }

                Task.WaitAll(recursiveCalls.ToArray());

                long i7 = watch.ElapsedMilliseconds - currTime.Milliseconds;
                currTime = watch.Elapsed;

                // POPULATE INNER NODES

                //await SubsampleNode(root, qw, rand);
                Sampling.Sample(root, (Node node) => {
                    node.offset = (uint)qw.Enqueue(Point.ToBytes(node.points));
                });

                void traverse(Node node, Action<Node> cb) {
                    foreach (Node child in node.children)
                        if (child != null)
                            traverse(child, cb);
                    cb(node);
                }

                traverse(root, (Node n) => { n.children = null; });

                long i8 = watch.ElapsedMilliseconds - currTime.Milliseconds;

                Debug.Log($"Call finished. I1 {i1}, I2 {i2}, I3 {i3}, I4 {i4}, I5 {i5}, I6 {i6}, I7 {i7}, I8 {i8}");
            });
        }

        string CheckBBox(Point[] points, AABB bbox) {
            foreach (Point pt in points)
                if (!bbox.InAABB(pt.pos))
                    return $"BBox check on {bbox.ToString()} failed on ({pt.pos.x}, {pt.pos.y}, {pt.pos.z})";

            return $"BBox check on {bbox.ToString()} passed";
        }

        // Subsamples and writes node
        public static async Task SubsampleNode(Node node, QueuedWriter qw, Random rand)
        {
            await Task.Run(() =>
            {
                if (node.IsLeaf || node.subsampled)
                    return;

                // Should do space constraint or something

                /* Point[] newPoints = new Point[subsampleSize];
                for (int i = 0; i < subsampleSize; i++)
                    newPoints[i] = node.points[rand.Next(0, (int)node.pointCount)];

                node.pointCount = (uint)subsampleSize; */

                List<Task> childTasks = new();

                for (int i = 0; i < 8; i++)
                    if (node.children[i] != null)
                        childTasks.Add(SubsampleNode(node.children[i], qw, rand));

                List<Point> newPoints = new List<Point>();

                int budget = subsampleSize;
                int[] childBudgets = new int[8];
                for (int i = 0; i < 7; i++) {
                    if (node.children[i].pointCount < budget / (8-i))
                        childBudgets[i] = (int)node.children[i].pointCount;
                    else
                        childBudgets[i] = budget / (8-i);
                    budget -= childBudgets[i];
                }

                childBudgets[7] = budget;

                for (int i = 0; i < 8; i++) {
                    if (childBudgets[i] == node.children[i].pointCount)
                        newPoints.AddRange(node.children[i].points);
                    else {
                        Point[] childPoints = new Point[childBudgets[i]];
                        for (int p = 0; p < childBudgets[i]; p++)
                            newPoints.Add(node.children[i].points[p * (node.children[i].pointCount / childBudgets[i])]);
                    }
                }

                node.points = newPoints.ToArray();
                node.subsampled = true;

                Task.WaitAll(childTasks.ToArray());

                for (int i = 0; i < 8; i++)
                    if (node.children[i] != null)
                    {
                        Node child = node.children[i];
                        child.offset = (uint)qw.Enqueue(Point.ToBytes(child.points));
                        child.points = null;
                    }
            });
        }

        static async Task WriteNode(Node node, QueuedWriter qw)
        {
            await Task.Run(() =>
            {
                List<Task> childTasks = new();

                for (int i = 0; i < 8; i++)
                    if (node.children[i] != null)
                        childTasks.Add(WriteNode(node.children[i], qw));

                node.offset = (uint)qw.Enqueue(Point.ToBytes(node.points));
                node.points = null;

                Task.WaitAll(childTasks.ToArray());
            });
        }
    }
}