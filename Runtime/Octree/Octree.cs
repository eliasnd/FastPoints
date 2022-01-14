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
        ConcurrentQueue<Action> unityActions;   // Used for sending compute shader calls to main thread
        ComputeShader computeShader = (ComputeShader)Resources.Load("CountAndSort");
        int treeDepth;
        string dirPath;
        AABB bbox;

        // Construction params
        int batchSize = 100000;  // Size of batches for reading and running shader
        public Octree(int treeDepth, string dirPath, ConcurrentQueue<Action> unityActions) {
            this.treeDepth = treeDepth;
            this.unityActions = unityActions;
            this.dirPath = dirPath;
        }

        public async Task BuildTree(PointCloudData data) {
            /* ConcurrentQueue<Point[]> readQueue = new ConcurrentQueue<Point[]>();
            Task loadTask = data.LoadPointBatches(batchSize, readQueue, true);

            int chunkDepth = 7;
            int chunkGridSize = (int)Mathf.Pow(2, chunkDepth);
            ComputeBuffer chunkGridBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(UInt32));

            // 4096 threads per shader pass call
            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);

            while (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z)
                Thread.Sleep(300);  // Block until bounds populated

            float[] minPoint = new float[] { data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f };
            float[] maxPoint = new float[] { data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f };

            bbox = new AABB(
                new Vector3(data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f), 
                new Vector3(data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f)
            );
            
            // Initialize compute shader
            unityActions.Enqueue(() => {
                computeShader.SetFloats("_MinPoint", minPoint);
                computeShader.SetFloats("_MaxPoint", maxPoint);

                computeShader.SetInt("_ChunkCount", chunkGridSize * chunkGridSize * chunkGridSize);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);
                computeShader.SetInt("_ThreadCount", threadBudget);
            });

            while (unityActions.Count > 0) // Wait for unity thread to complete action
                    Thread.Sleep(300);

            // Counting loop
            while (loadTask.Status == TaskStatus.Running || readQueue.Count > 0) {
                Point[] batch;
                while (!readQueue.TryDequeue(out batch))
                    Thread.Sleep(300);

                ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, Point.size);
                batchBuffer.SetData(batch);

                unityActions.Enqueue(() => {
                    int countHandle = computeShader.FindKernel("CountPoints");
                    computeShader.SetBuffer(countHandle, "_Points", batchBuffer);
                    computeShader.SetInt("_BatchSize", batch.Length);
                    computeShader.Dispatch(countHandle, 64, 1, 1);

                    batchBuffer.Release();
                });
            }

            readQueue.Clear();
            loadTask = data.LoadPointBatches(batchSize, readQueue, true);   // Start loading to get headstart while merging

            while (unityActions.Count > 0) // Wait for unity thread to complete actions
                Thread.Sleep(300);

            // Merge sparse chunks

            List<Chunk> chunks;

            int sparseChunkCutoff = 10000000;    // Any chunk with more than sparseChunkCutoff points is written to disk

            uint[] levelGrid = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            chunkGridBuffer.GetData(grid);
            unityActions.Enqueue(() => { chunkGridBuffer.Release(); });

            for (int level = chunkDepth; level > 0; level--) {
                int levelGridSize = Mathf.Pow(levelGrid.Length, 1/3f);

                var levelGridIndex = (int x, int y, int z) => levelGrid[x * levelGridSize * levelGridSize + y * levelGridSize + z];

                uint[] nextLevelGrid = new uint[(levelGridSize-1) * (levelGridSize-1) * (levelGridSize-1)];

                for (int x = 0; x < levelGridSize; x+=2)
                    for (int y = 0; y < levelGridSize; y+=2)
                        for (int z = 0; z < levelGridSize; z+=2) {
                            var addChunks = () => {
                                chunks.Add(new Chunk(x, y, z, level)); chunks.Add(new Chunk(x, y, z+1, level));
                                chunks.Add(new Chunk(x, y+1, z, level)); chunks.Add(new Chunk(x, y+1, z+1, level));
                                chunks.Add(new Chunk(x+1, y, z, level)); chunks.Add(new Chunk(x+1, y, z+1, level));
                                chunks.Add(new Chunk(x+1, y+1, z, level)); chunks.Add(new Chunk(x+1, y+1, z+1, level));
                                nextLevelGrid[gridIndex(x/2, y/2, z/2)] = UInt32.MaxValue;
                            };

                            // If any chunks in cube previously added
                            if (levelGridIndex(x, y, z) == UInt32.MaxValue || levelGridIndex(x, y, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x, y+1, z) == UInt32.MaxValue || levelGridIndex(x, y+1, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x+1, y, z) == UInt32.MaxValue || levelGridIndex(x+1, y, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x+1, y+1, z) == UInt32.MaxValue || levelGridIndex(x+1, y+1, z+1) == UInt32.MaxValue) {
                                addChunks();
                            }

                            uint cubeSum =  
                                levelGridIndex(x, y, z) + levelGridIndex(x, y, z+1) + 
                                levelGridIndex(x, y+1, z) + levelGridIndex(x, y+1, z+1) + 
                                levelGridIndex(x+1, y, z) + levelGridIndex(x+1, y, z+1) + 
                                levelGridIndex(x+1, y+1, z) + levelGridIndex(x+1, y+1, z+1);

                            // If cube below threshold
                            if (cubeSum < sparseChunkCutoff)
                                nextLevelGrid[gridIndex(x/2, y/2, z/2)] = cubeSum;
                            else
                                addChunks();
                        }

                levelGrid = nextLevelGrid;
            }

            AABB[] chunkBBox = bbox.Subdivide(chunkGridSize);

            // Create lookup table
            int[] lut = new int[chunkGridSize * chunkGridSize * chunkGridSize];
            for (int i = 0; i < chunks.Count; i++) {
                Chunk chunk = chunks[i];

                if (chunk.level == chunkDepth) {
                    lut[chunk.x * chunkGridSize * chunkGridSize + chunk.y * chunkGridSize + chunk.z] = i;
                    chunk.bbox = chunkBBox[i];
                }
                else {
                    int chunkSize = Mathf.Pow(2, chunkDepth-chunk.level);
                    chunk.bbox = new AABB(
                        chunks[
                            chunk.x * chunkSize * chunkGridSize * chunkGridSize + 
                            chunk.y * chunkSize * chunkGridSize + chunk.z * chunkSize
                        ].bbox.Min, 
                        chunks[
                            ((chunk.x+1) * chunkSize - 1) * chunkGridSize * chunkGridSize + 
                            ((chunk.y+1) * chunkSize - 1) * chunkGridSize
                            ((chunk.z+1) * chunkSize - 1)
                        ].bbox.Max
                    );
                    for (int x = chunk.x * chunkSize; x < (chunk.x + 1) * chunkSize; x++)
                        for (int y = chunk.y * chunkSize; y < (chunk.y + 1) * chunkSize; y++)
                            for (int z = chunk.z * chunkSize; z < (chunk.z + 1) * chunkSize; z++)
                                lut[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z] = i;
                }
            }

            // Create chunk files
            Directory.CreateDirectory($"{dirPath}/tmp");

            FileStream[] chunkStreams = new FileStream[];
            List<Point>[] chunkBuffers = new List<Point>[chunks.Count];

            for (int i = 0; i < chunks.Count; i++) {
                Chunk chunk = chunks[i];
                chunks[i].path = $"{dirPath}/tmp/chunk_{chunk.x}_{chunk.y}_{chunk.z}_l{chunk.level}";
                chunkStreams[i] = File.Create(chunks[i].path);
                chunkBuffers[i] = new List<Point>();
            }

            // Distribute points
            ConcurrentQueue<(Point[], uint[])> sortedBatches;
            int maxSorted = 100;

            // Start writing
            bool allDistributed = false;
            Task writeChunksTask = Task.Run(() => {
                while (!allDistributed || sortedBatches.Count > 0) {
                    (Point[] batch, uint[] indices) tup;
                    while (!sortedBatches.TryDequeue(out tup))
                        Thread.Sleep(300);

                    for (int i = 0; i < tup.batch.Length; i++) {
                        int chunkIdx = lut[tup.indices[i]];
                        chunkBuffers[chunkIdx].Add(batch[i]);
                        if (chunkBuffers[chunkIdx].Count == 10) {   // Flush when ten points queued
                            chunkStreams[chunkIdx].Write(chunkBuffers[chunkIdx].ToArray());
                            chunkBuffers[chunkIdx].Clear();
                        }
                    }
                }
            });

            // Sort points
            while (loadTask.Status == TaskStatus.Running || readQueue.Count > 0) {
                while (sortedBatches.Count > maxSorted)
                    Thread.Sleep(300);

                Point[] batch;
                while (!readQueue.TryDequeue(out batch))
                    Thread.Sleep(300);   

                uint[] chunkIndices = new uint[batch.Length];             

                unityActions.Enqueue(() => {
                    ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, Point.size);
                    batchBuffer.SetData(batch);

                    ComputeBuffer sortedBuffer = new ComputeBuffer(batchSize, sizeof(uint));

                    int sortHandle = computeShader.FindKernel("SortPoints");
                    computeShader.SetBuffer(sortHandle, "_Points", batchBuffer);
                    computeShader.SetBuffer(sortHandle, "_ChunkIndices", sortedBuffer);
                    computeShader.SetInt("_BatchSize", batch.Length);
                    computeShader.Dispatch(sortHandle, 64, 1, 1);

                    batchBuffer.Release();

                    sortedBuffer.GetData(chunkIndices);
                    sortedBuffer.Release();
                });

                sortedBatches.Enqueue((batch, chunkIndices));
            }

            allDistributed = true;

            // Subsample chunks

            ThreadedWriter tw = new ThreadedWriter($"{dirPath}/octree.bin");
            tw.Start();

            for (int i = 0; i < chunks.Count; i++) {
                Point[] chunk = Point.ToPoints(File.ReadAllBytes(chunks[i].path));
                BuildOctree(chunk, chunks[i].bbox, 2);  // Go two layers at a time to get shallow tree
            }

            tw.Stop();
        }

        Node BuildLocalOctree(Point[] points, AABB bbox, int layers) {
            int maxNodeSize = 2000000;
            int totalNodeCount = (int)Mathf.Pow(8, layers);
            bool actionComplete = false;

            uint[] nodeGrid = new uint[(int)Mathf.Pow(8, layers)];
            uint[] nodeIndices = new uint[points.Length];

            unityActions.Enqueue(() => {
                ComputeBuffer pointBuffer = new ComputeBuffer(batchSize, Point.size);
                pointBuffer.SetData(points);

                ComputeBuffer nodeGridBuffer = new ComputeBuffer(totalNodeCount, sizeof(UInt32));
                int threadBudget = Mathf.CeilToInt(points.Length / 4096f);

                ComputeShader sortedBuffer = new ComputeBuffer(points.Length, sizeof(UInt32));

                int sortHandle = computeShader.FindKernel("SortPoints");
                int countHandle = computeShader.FindKernel("CountPoints");

                computeShader.SetFloats("_MinPoint", new float[] { bbox.Min.x, bbox.Min.y, bbox.Min.z });
                computeShader.SetFloats("_MaxPoint", new float[] { bbox.Max.x, bbox.Max.y, bbox.Max.z });
                computeShader.SetInt("_ChunkCount", totalNodeCount);
                computeShader.SetInt("_ThreadCount", threadBudget);
                computeShader.SetInt("_BatchSize", points.Length);

                computeShader.SetBuffer(sortHandle, "_Points", pointBuffer);
                computeShader.SetBuffer(sortHandle, "_ChunkIndices", sortedBuffer);

                computeShader.Dispatch(sortHandle, 64, 1, 1);

                pointBuffer.Release();

                nodeGridBuffer.GetData(nodeGrid);
                nodeGridBuffer.Release();

                sortedBuffer.GetData(nodeIndices);
                sortedBuffer.Release();

                actionComplete = true;
            });

            // Create leaf nodes

            List<Point>[] sorted = new List<Point>[totalNodeCount];
            for (int i = 0; i < totalNodeCount; i++)
                sorted[i] = new List<Point>();

            while (!actionComplete)
                Thread.Sleep(300);

            for (int i = 0; i < points.Length; i++)
                sorted[nodeIdx].Add(points[i]);

            Node[] nodes = new Node[totalNodeCount];
            AABB[] nodeBBox = bbox.Subdivide((int)Mathf.Pow(2, layers));

            for (int i = 0; i < totalNodeCount; i++)
                nodes[i] = 
                    sorted[nodeIdx].Count > maxNodeSize ? 
                    BuildLocalOctree(sorted[nodeIdx].ToArray(), nodeBBox[i], layers) : 
                    new Node(sorted[nodeIdx].ToArray());

            // Construct octree structure
            Node[] prevLayer = nodes;
            for (int l = layers-1; l > 0; l++) {
                Node[] nextLayer = new Node[(int)Mathf.Pow(8, l)];
                for (int n = 0; n < nextLayer.Length; n++) {
                    Node node = new Node(null, true);
                    for (int i = 0; i < 8; i++)
                        node.children[i] = prevLayer[n * 8 + i];
                    
                }
                prevLayer = nextLayer;
            }*/

        }
    }

    struct BuildTreeParams {
        public Octree tree;
        public PointCloudData data;
        public BuildTreeParams(Octree tree, PointCloudData data) {
            this.tree = tree;
            this.data = data;
        }
    }

    struct Node {
        public Point[] points;
        public Node[] children;

        public Node(Point[] points, bool hasChildren = false) {
            this.points = points;
            if (hasChildren)
                children = new Node[8];
            else
                children = null;
        }
    }

    struct Chunk {
        public AABB bbox;
        public string path;
        public int level;
        public int x;
        public int y;
        public int z;
        public Chunk(int x, int y, int z, int level) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.level = level;
            this.bbox = new AABB(new Vector3(1,1,1), new Vector3(0,0,0));
            this.path = null;
        }
    }
}