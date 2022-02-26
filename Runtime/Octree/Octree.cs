using UnityEngine;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    public class Octree : ScriptableObject{
        AABB bbox;
        NodeEntry[] nodes;
        string dirPath;

        public async Task BuildTree(PointCloudData data, int treeDepth, string dirPath, Dispatcher dispatcher) {
            if (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z) 
                await data.PopulateBounds();

            this.dirPath = dirPath;

            // await Chunker.MakeChunks(data, "Resources/Octree", dispatcher);

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

                for (int i = 0; i < chunkPaths.Length; i++)
                {
                    while (idxr.FootprintMB > 1024)
                        Thread.Sleep(50);

                    indexTask = idxr.IndexChunk(data, chunkRoots[i], chunkPaths[i]);
                    await indexTask;
                }

                // CONSTRUCT GLOBAL TREE

                Node root = new();

                Queue<Node> queue = new();
                queue.Enqueue(root);
                for (int l = 0; l < chunkDepth; l++)
                {
                    for (int n = 0; n < (int)Mathf.Pow(2, l); n++)
                    {
                        Node node = queue.Dequeue();
                        node.children = new Node[8];
                        for (int i = 0; i < 8; i++) { 
                            node.children[i] = new();
                            queue.Enqueue(node.children[i]);
                        }
                    }
                }

                for (int c = 0; c < chunkPaths.Length; c++)
                {
                    string chunkID = Path.GetFileNameWithoutExtension(chunkPaths[c])[1..];
                    Node curr = root;
                    for (int i = 0; i < chunkID.Length-1; i++)
                        curr = curr.children[chunkID[i] - '0'];

                    curr.children[chunkID[-1] - '0'] = chunkRoots[c];
                }

                await Indexer.SubsampleNode(root, qw, new System.Random());

                List<NodeEntry> nodeList = new();

                uint AddEntry(Node n)
                {

                    NodeEntry entry = new NodeEntry
                    {
                        pointCount = n.pointCount,
                        offset = n.offset,
                        descendentCount = 0
                    };

                    nodeList.Add(entry);

                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            entry.descendentCount += AddEntry(n.children[i]);

                    return entry.descendentCount;
                }

                nodes = nodeList.ToArray();

            } catch (Exception e)
            {
                Debug.Log($"Exception. Message: {e.Message}, Backtrace: {e.StackTrace}, Inner: {e.InnerException}");
            }
        }
    }

    struct BuildTreeParams {
        public Octree tree;
        public PointCloudData data;
        public Dispatcher dispatcher;
        public BuildTreeParams(Octree tree, PointCloudData data, Dispatcher dispatcher) {
            this.tree = tree;
            this.data = data;
            this.dispatcher = dispatcher;
        }
    }
}