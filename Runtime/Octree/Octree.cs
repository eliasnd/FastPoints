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
        [SerializeField]
        int treeDepth;
        public int TreeDepth { get { return treeDepth; } }
        public int LeafNodeAxis { get { return (int)Mathf.Pow(2, treeDepth); } }
        public int LeafNodeTotal { get { return LeafNodeAxis * LeafNodeAxis * LeafNodeAxis; } }

        [SerializeField]
        string dirPath;

        [SerializeField]
        OctreeNode root;
        OctreeNode[] leafNodes;

        public Octree(int treeDepth, uint[] leafNodeCounts, string dirPath="Assets/octree") {
            this.treeDepth = treeDepth;
            this.dirPath = dirPath;

            File.Create($"{dirPath}/octree.bin").Close();

            leafNodes = new OctreeNode[LeafNodeTotal];
            int currOffset = 0;
            for (int i = 0; i < LeafNodeTotal; i++) {
                leafNodes[i] = new OctreeNode((int)leafNodeCounts[i], currOffset, $"{dirPath}/octree.bin");
                currOffset += (int)leafNodeCounts[i];
            }
                
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

                foreach (int nidx in sorted.Keys)
                    tasks.Add(leafNodes[nidx].WritePoints(sorted[nidx].ToArray()));

                Task.WaitAll(tasks.ToArray());
            });
            
        }


        public OctreeNode GetLeafNode(int x, int y, int z) {
            return leafNodes[LeafNodeAxis * LeafNodeAxis * x + LeafNodeAxis * y + z];
        }
    }
}