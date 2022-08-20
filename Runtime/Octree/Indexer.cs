using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Buffers;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;

using Random = System.Random;
using Debug = UnityEngine.Debug;

namespace FastPoints {
    public class Indexer {
        bool debug = false;

        static int treeDepth = 3;
        static int maxNodeSize = 50000;
        static int subsampleSize = 50000;

        int octreeDepth = 0;

        QueuedWriter qw;
        Dispatcher dispatcher;
        ArrayPool<Point> pointPool;
        ArrayPool<int> intPool;

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
            this.pointPool = ArrayPool<Point>.Shared;
            this.intPool = ArrayPool<int>.Shared;
        }

        public async Task IndexChunk(PointCloudData data, Node root, string chunkPath) { 
            if (debug)
                Debug.Log("I0");

            // Get BBOX better eventually
            string chunkCode = Path.GetFileNameWithoutExtension(chunkPath);
            AABB curr = new AABB(data.MinPoint, data.MaxPoint);

            for (int i = 1; i < chunkCode.Length; i++) {
                int idx = chunkCode[i] - '0';
                curr = curr.child(idx);
            }

            curr = new AABB(
                new Vector3(curr.Min.x-1E-3f, curr.Min.y-1E-3f, curr.Min.z-1E-3f),
                new Vector3(curr.Max.x+1E-3f, curr.Max.y+1E-3f, curr.Max.z+1E-3f));

            uint chunkFootprint = (uint)new FileInfo(chunkPath).Length * 2;

            lock (footprintLock)
            {
                footprint += chunkFootprint;
                Debug.Log($"Starting chunk {Path.GetFileNameWithoutExtension(chunkPath)} with bbox {curr.ToString()}, {chunkFootprint/30} points, footprint now {FootprintMB}");
            }

            int pointCount = (int)new FileInfo(chunkPath).Length / 15;
            Point[] points = pointPool.Rent(pointCount);    // RENT_0
            Point.ToPoints(File.ReadAllBytes(chunkPath), points, true);

            for (int i = 0; i < pointCount; i++)
                if (!curr.InAABB(points[i].pos))
                    Debug.LogError($"ChunkIssue at chunk {Path.GetFileNameWithoutExtension(chunkPath)}");

            // root.bbox = new AABB(new Vector3(curr.Min.x-1E-5f, curr.Min.y-1E-5f, curr.Min.z-1E-5f), new Vector3(curr.Max.x+1E-5f, curr.Max.y+1E-5f, curr.Max.z+1E-5f));
            root.bbox = curr;
            root.name = "n";
            // Debug.Log(chunkPath + ", " + root.bbox.ToString());

            Debug.Log($"Preliminary chunk test");
            CheckBBox(points, root.bbox, pointCount);
            Debug.Log($"Preliminary chunk test done");

            // for (int i = 0; i < pointCount; i++) {
            //     Point pt = points[i];
            //     if (!root.bbox.InAABB(pt.pos))
            //         Debug.LogError($"Chunk at {chunkPath} has point {pt.ToString()} outside bbox!");
            // }

            Stopwatch watch = new();
            watch.Start();

            IndexPoints(root, points, pointCount); 

            watch.Stop();
            pointPool.Return(points);   // RETURN_0

            lock (footprintLock)
            {
                footprint -= chunkFootprint;
                Debug.Log($"Finished chunk {Path.GetFileNameWithoutExtension(chunkPath)} in {watch.ElapsedMilliseconds / 1000} seconds, written at offset {root.offset}, footprint now {FootprintMB}");
                if (FootprintMB == 0)
                    Debug.Log($"All cleaned up");
            }

            // Debug.Log("Exit");
        }

        public void IndexPoints(Node root, Point[] points, int pointCount, string traceback = "") {
            traceback += $"\nIndexing root {root.name}";
            if (debug)
                Debug.Log("I1");
            try {
                CheckBBox(points, root.bbox, pointCount);
                traceback += "\nBBox check passed";
            } catch (Exception e) {
                Debug.LogError($"Currently at root name {root.name}, error {e.Message}");
            }

            // Was in task before
            int gridSize = (int)Mathf.Pow(2, treeDepth-1);
            int gridTotal = gridSize * gridSize * gridSize;

            // int[] mortonIndices = new int[gridSize * gridSize * gridSize];

            // for (int x = 0; x < gridSize; x++)
            // for (int y = 0; y < gridSize; y++)
            // for (int z = 0; z < gridSize; z++)
            //     mortonIndices[x * gridSize * gridSize + y * gridSize + z] = Utils.MortonEncode(z, y, x);

            int GetNode(Point p) {
                float threshold = 1f / gridSize; // Used to calculate which index for each dimension

                int x = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.x, root.bbox.Max.x, p.pos.x) / threshold), gridSize-1);
                int y = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.y, root.bbox.Max.y, p.pos.y) / threshold), gridSize-1);
                int z = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(root.bbox.Min.z, root.bbox.Max.z, p.pos.z) / threshold), gridSize-1);
                return Utils.MortonEncode(z, y, x);
            }

            int[] nodeCounts = intPool.Rent(gridTotal); // RENT_1
            for (int i = 0; i < gridTotal; i++)
                nodeCounts[i] = 0;

            for (int i = 0; i < pointCount; i++) {
                nodeCounts[GetNode(points[i])]++;
            }

            int countSum = 0;
            for (int i = 0; i < gridTotal; i++) {
                countSum += nodeCounts[i];
            }
            if (countSum != pointCount)
                Debug.Log("Count issue!");

            int[] nodeOffsets = intPool.Rent(gridTotal); // RENT_2
            nodeOffsets[0] = 0;
            for (int i = 1; i < gridTotal; i++)
                nodeOffsets[i] = nodeOffsets[i-1] + nodeCounts[i-1];

            if (nodeOffsets[gridTotal-1] + nodeCounts[gridTotal-1] != pointCount)
                Debug.Log("Offset issue!");

            // int[] debugNodeOffsets = (int[])nodeOffsets.Clone();

            Point[] sortedPoints = pointPool.Rent(pointCount);  // RENT_3
            // Point[] sortedPoints = new Point[pointCount];
            for (int i = 0; i < pointCount; i++) {
                int node = GetNode(points[i]);
                // if (node == 59)
                //     node = 59;
                int offset = nodeOffsets[node]++;
                sortedPoints[offset] = points[i];
            }

            int n = 0;

            // points = sortedPoints;
            // pointPool.Return(sortedPoints); // RETURN_3

            if (debug)
                Debug.Log("I2");

            // MAKE SUM PYRAMID

            int[][] sumPyramid = new int[treeDepth][];
            sumPyramid[treeDepth-1] = nodeCounts;

            int[][] offsetPyramid = new int[treeDepth][];
            offsetPyramid[treeDepth-1] = new int[gridTotal];
            offsetPyramid[treeDepth-1][0] = 0;

            for (int i = 1; i < gridTotal; i++)
                offsetPyramid[treeDepth-1][i] = offsetPyramid[treeDepth-1][i-1] + nodeCounts[i-1];

            int levelDim = gridSize / 2;

            for (int level = treeDepth-2; level >= 0; level--) {
                int[] currLevel = new int[levelDim * levelDim * levelDim];

                int[] lastLevel = sumPyramid[level+1];

                for (int x = 0; x < levelDim; x++)
                    for (int y = 0; y < levelDim; y++)
                        for (int z = 0; z < levelDim; z++)
                        {
                            int chunkIdx = Utils.MortonEncode(z, y, x);
                            int childIdx = Utils.MortonEncode(2*z, 2*y, 2*x);
                            currLevel[chunkIdx] =
                                lastLevel[childIdx] + lastLevel[childIdx+1] + lastLevel[childIdx+2] + lastLevel[childIdx+3] +
                                lastLevel[childIdx+4] + lastLevel[childIdx+5] + lastLevel[childIdx+6] + lastLevel[childIdx+7];
                        }

                sumPyramid[level] = currLevel;

                int[] currOffsets = new int[levelDim * levelDim * levelDim];
                currOffsets[0] = 0;
                for (int i = 1; i < levelDim * levelDim * levelDim; i++)
                    currOffsets[i] = currOffsets[i-1] + currLevel[i-1];
                offsetPyramid[level] = currOffsets;

                levelDim /= 2;
            }

            // DEBUG: Create bbox pyramid

            // AABB[][] bboxPyramid = new AABB[treeDepth][];
            // for (int l = 0; l < treeDepth; l++)
            //     bboxPyramid[l] = root.bbox.Subdivide((int)Mathf.Pow(2,l));

            // for (int i = 0; i < gridTotal; i++)
            //     for (int p = 0; p < sumPyramid[treeDepth-1][i].Length; p++)
            //         if (!bboxPyramid[treeDepth-1][i].InAABB(sumPyramid[treeDepth-1][i][p]))
            //             Debug.LogError($"Check 1 bounding box issue at node {node.name}. Traceback: {traceback}");

            if (debug)
                Debug.Log("I3");

            // CREATE NODE REFERENCES

            List<NodeReference> nodeRefs = new();

            NodeReference rootRef = new NodeReference();
            rootRef.name = "";
            rootRef.level = 0;
            rootRef.x = 0;
            rootRef.y = 0;
            rootRef.z = 0;
            rootRef.pointCount = (uint)sumPyramid[0][0];

            Stack<NodeReference> stack = new();
            stack.Push(rootRef);

            int c = 0;
            while (stack.Count > 0) {
                c++;
                NodeReference nr = stack.Pop();

                int nodeIdx = Utils.MortonEncode(nr.z, nr.y, nr.x);
                uint numPoints = (uint)sumPyramid[nr.level][nodeIdx];

                if (nr.level == treeDepth-1) {
                    // If on maximum granularity for this pass
                    if (numPoints > 0)
                        nodeRefs.Add(nr);

                } else if (numPoints > maxNodeSize) {
                    // Create child nodes
                    int childIdx = Utils.MortonEncode(2*nr.z, 2*nr.y, 2*nr.x);
                    for (int i = 0; i < 8; i++) {
                        int count = sumPyramid[nr.level+1][childIdx+i];

                        if (count > 0) {
                            NodeReference child = new();
                            child.level = nr.level+1;
                            child.name = nr.name + i;
                            try {
                                child.offset = (uint)offsetPyramid[nr.level + 1][childIdx+i];
                            } catch (Exception e) {
                                Debug.Log("Here");
                            }
                            // Debug.Log($"Made node {child.name} with level {child.level}, idx {childIdx+i}");
                            child.pointCount = (uint)count;
                            // child.bbox = bboxPyramid[nr.level + 1][childIdx+i];
                            
                            child.x = 2 * nr.x + ((i & 0b100) >> 2);
                            child.y = 2 * nr.y + ((i & 0b010) >> 1);
                            child.z = 2 * nr.z + ((i & 0b001) >> 0);

                            stack.Push(child);
                        }
                    }
                } else if (numPoints > 0) {
                    // Create leaf node
                    nodeRefs.Add(nr);
                }
            }

            if (debug)
                Debug.Log("I6");

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
                        child.bbox = currNode.bbox.child(idx);
                        child.name = currNode.name + idx;
                        child.children = new Node[8];

                        currNode.children[idx] = child;
                        currNode = child;
                    } else
                    {
                        currNode = currNode.children[idx];
                    }
                
                }

                return currNode;
            }

            List<Node> recursiveCalls = new();  // Parallelize with tasks eventually?

            // n = 0;
            // for (int j = 0; j < gridTotal; j++)
            //     for (int k = 0; k < nodeCounts[j]; k++) {
            //         if (GetNode(sortedPoints[n]) != j)
            //             Debug.LogError($"Issue0 in sorted points at {n}, expected {j}, got {GetNode(sortedPoints[n])}");
            //         n++;
            //     }

            // Point[] oldSortedPoints = new Point[sortedPoints.Length];
            // for (int i = 0; i < pointCount; i++)
            //     oldSortedPoints[i] = sortedPoints[i].DeepClone();
            
            // int nrc = 0;

            foreach (NodeReference nr in nodeRefs)
            {
                // for (int i = 0; i < pointCount; i++)
                //     if (!oldSortedPoints[i].Equals(sortedPoints[i]))
                //         throw new Exception("Unequal1");
                // nrc++;
                Node node = ExpandReference(nr);
                node.pointCount = nr.pointCount;

                // for (int i = 0; i < pointCount; i++)
                //     if (!oldSortedPoints[i].Equals(sortedPoints[i]))
                //         throw new Exception("Unequal2");

                node.points = pointPool.Rent((int)node.pointCount); // RENT_6
                int nodeOffset = offsetPyramid[nr.level][Utils.MortonEncode(nr.z, nr.y, nr.x)];
                for (int i = 0; i < node.pointCount; i++) {
                    node.points[i] = sortedPoints[nodeOffset+i];
                }

                // for (int i = 0; i < pointCount; i++)
                //     if (!oldSortedPoints[i].Equals(sortedPoints[i]))
                //         throw new Exception("Unequal3");

                n = 0;
                // for (int j = 0; j < gridTotal; j++)
                //     for (int k = 0; k < nodeCounts[j]; k++) {
                //         if (GetNode(sortedPoints[n]) != j)
                //             Debug.LogError($"Issue1 in sorted points at {n}, expected {j}, got {GetNode(sortedPoints[n])}");
                //         n++;
                //     }

                for (int i = 0; i < node.pointCount; i++)
                    if (!node.bbox.InAABB(node.points[i].pos)) {
                        int idx = Utils.MortonEncode(nr.z, nr.y, nr.x);
                        int target = GetNode(node.points[i]);
                        Debug.LogError($"Bounding box issue at node {node.name}, level {nr.level}, idx {idx} with bbox {node.bbox.ToString()} and point ({node.points[i].pos.x}, {node.points[i].pos.y}, {node.points[i].pos.z}), originally sorted into node {target},\nTraceback: {traceback}");
                        // VERIFY SORTING
                        // if (idx != target)
                        //     Debug.LogError($"Sorting issue at point {nodeOffset+i}, expected {idx}, actually {target}");
                        // n = 0;
                        // for (int j = 0; j < gridTotal; j++)
                        //     for (int k = 0; k < nodeCounts[j]; k++) {
                        //         if (GetNode(sortedPoints[n]) != j)
                        //             Debug.LogError($"Issue2 in sorted points at {n}, expected {j}, got {GetNode(sortedPoints[n])}");
                        //         n++;
                        //     }
                        // Debug.LogError($"Final n value is {n}");
                    }

                if (node.pointCount > maxNodeSize) {
                    // if (node.name == "n33")
                    //     Debug.Log("n33");
                    recursiveCalls.Add(node);
                }

                // if (node.name == "n377")
                //     Debug.Log("Here");
                // for (int i = 0; i < pointCount; i++)
                //     if (!oldSortedPoints[i].Equals(sortedPoints[i]))
                //         throw new Exception("Unequal4");
            }

            pointPool.Return(sortedPoints); // RETURN_3

            foreach (Node node in recursiveCalls) {
                traceback += $"\nNode {node.name} needs expansion";
                IndexPoints(node, node.points, (int)node.pointCount, string.Copy(traceback));
            }


            if (debug)
                Debug.Log("I7");

            Sampling.Sample(root, (Node node) => {
                // if (node.name == "n37")
                //     Debug.Log("Here");
                node.offset = (uint)qw.Enqueue(Point.ToBytes(node.points, 0, (int)node.pointCount));
                pointPool.Return(node.points);  // RETURN_6
                node.points = null;
            }, string.Copy(traceback));  


            intPool.Return(nodeCounts); // RETURN_1
            intPool.Return(nodeOffsets); // RETURN_2

            if (debug)
                Debug.Log($"I8");
        }

        void CheckBBox(Point[] points, AABB bbox, int pointCount=0) {
            if (pointCount == 0)
                pointCount = points.Length;
            for (int i = 0; i < pointCount; i++)
                if (!bbox.InAABB(points[i].pos))
                    throw new Exception($"BBox check on {bbox.ToString()} failed on ({points[i].pos.x}, {points[i].pos.y}, {points[i].pos.z})");

            // return $"BBox check on {bbox.ToString()} passed";
        }
    }
}