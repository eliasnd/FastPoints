using UnityEngine;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    public class Octree {
        int count;
        int treeDepth;  // Number of layers in tree - treeDepth = 1 has only root node
        public int TreeDepth { get { return treeDepth; } }
        public int LeafNodeAxis { get { return (int)Mathf.Pow(2, treeDepth-1); } }
        public int LeafNodeTotal { get { return (int)Mathf.Pow(8, treeDepth-1); } }
        
        AABB bbox;
        string dirPath;
        OctreeNode root { get { return nodes[0]; } }
        List<OctreeNode> nodes;

        public Octree(int treeDepth, int count, uint[] leafNodeCounts, int internalNodeCount, AABB bbox, string dirPath="Assets/octree") {
            this.treeDepth = treeDepth;
            this.dirPath = dirPath;
            this.bbox = bbox;

            string filePath = $"{dirPath}/octree.bin";
            File.Create(filePath, 4096).Close();

            nodes = new List<OctreeNode>();

            // Initialize octree -- Queue gets BFS
            Queue<OctreeNode> unvisited = new Queue<OctreeNode>();
            unvisited.Enqueue(new OctreeNode());    // Add root node
            // Debug.Log(unvisited.Peek());

            // Initialize internal nodes
            Int64 internalOffset = count * 15;
            for (int l = 0; l < treeDepth-1; l++) {
                AABB[] layerBBox = bbox.Subdivide((int)Mathf.Pow(2, l));
                // Debug.Log(unvisited.Count);

                for (int n = 0; n < Mathf.Pow(8, l); n++) {
                    OctreeNode curr = unvisited.Dequeue();

                    // Debug.Log(curr);
                    // Debug.Log(layerBBox[n]);
                    curr.InitializeInternal(internalNodeCount, internalOffset, filePath, layerBBox[n]);
                    nodes.Add(curr);
                    internalOffset += internalNodeCount * 15;

                    for (int c = 0; c < 8; c++)
                        unvisited.Enqueue(curr.GetChild(c));
                }
            }

            // Initialize leaf nodes

            Int64 leafOffset = 0;

            AABB[] leafBBox = bbox.Subdivide(LeafNodeAxis);

            for (int i = 0; i < LeafNodeTotal; i++) {
                OctreeNode leaf = unvisited.Dequeue();

                leaf.InitializeLeaf((int)leafNodeCounts[i], leafOffset, filePath, leafBBox[i]);
                nodes.Add(leaf);
                leafOffset += (int)leafNodeCounts[i] * 15;
            }    

            // Debug.Log($"Finished building tree with total of {nodes.Count} nodes");
        }

        public async Task WritePoints(Point[] points, uint[] sortedIndices) {
            await Task.Run(() => {
                Dictionary<int, List<Point>> sorted = new Dictionary<int, List<Point>>();
                for (int i = 0; i < points.Length; i++) {
                    if (!sorted.ContainsKey((int)sortedIndices[i]))
                        sorted.Add((int)sortedIndices[i], new List<Point>());

                    sorted[(int)sortedIndices[i]].Add(points[i]);
                }

                List<Task> tasks = new List<Task>();


                foreach (int nidx in sorted.Keys) {
                    try {
                        tasks.Add(GetLeafNode(nidx).WritePoints(sorted[nidx].ToArray()));
                    } catch {
                        throw new Exception($"Error adding task for leaf {nidx} with index {nodes.Count - LeafNodeTotal + nidx}");
                    }
                }

                Task.WaitAll(tasks.ToArray());
            });
            
        }

        // Populate all inner nodes - requires all leaf nodes to be fully written
        public async Task SubsampleTree() {
            if (root.Type == OctreeNode.NodeType.LEAF)
                return;

            await root.PopulateInternal();
        }

        public async Task FlushLeafBuffers() {
            await Task.Run(() => {
                List<Task> tasks = new List<Task>(LeafNodeTotal);

                for (int i = nodes.Count - LeafNodeTotal; i < nodes.Count; i++)
                    tasks.Add(nodes[i].FlushBuffer());

                Task.WaitAll(tasks.ToArray());
            });
        }

        public async Task FlushInternalBuffers() {
            await Task.Run(() => {
                List<Task> tasks = new List<Task>(LeafNodeTotal);

                for (int i = 0; i < nodes.Count - LeafNodeTotal; i++)
                    tasks.Add(nodes[i].FlushBuffer());

                Task.WaitAll(tasks.ToArray());
            });
        }

        public void SelectPoints(Camera cam, Point[] target) {
            int idx = 0;
            nodes[0].SelectPoints(target, ref idx, cam);
        }

        static bool Intersects(Camera cam, AABB bbox) {
            throw new NotImplementedException();
        }

        static float DistanceCoefficient(Camera cam, AABB bbox) {
            throw new NotImplementedException();
        }

        public OctreeNode GetNode(int idx) {
            return nodes[idx];
        }

        public OctreeNode GetLeafNode(int idx) {
            return nodes[nodes.Count - LeafNodeTotal + idx];
        }

        public OctreeNode GetLeafNode(int x, int y, int z) {
            return nodes[nodes.Count - LeafNodeTotal + LeafNodeAxis * LeafNodeAxis * x + LeafNodeAxis * y + z];
        }

        /*
        -- Hierarchy Format --
        byte treeDepth
        */
        public void WriteHierarchy() {
            string filePath = $"{dirPath}/hierarchy.bin";
            BinaryWriter bw = new BinaryWriter(File.Create(filePath));
        }
    }
}