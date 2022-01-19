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

    public class Octree {
        ConcurrentQueue<Action> unityActions;   // Used for sending compute shader calls to main thread
        ComputeShader computeShader = (ComputeShader)Resources.Load("CountAndSort");
        int treeDepth;
        string dirPath;
        AABB bbox;

        // Construction params
        int batchSize = 1000000;  // Size of batches for reading and running shader

        bool doTests = true;

        public Octree(int treeDepth, string dirPath, ConcurrentQueue<Action> unityActions) {
            this.treeDepth = treeDepth;
            this.unityActions = unityActions;
            this.dirPath = dirPath;
        }

        public async Task BuildTree(PointCloudData data) {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Starting");

            ConcurrentQueue<Point[]> readQueue = new ConcurrentQueue<Point[]>();
            if (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z) 
                await data.PopulateBounds();

            data.LoadPointBatches(batchSize, readQueue);

            // Debug.Log("Started task");

            int chunkDepth = 7;
            int chunkGridSize = (int)Mathf.Pow(2, chunkDepth);
            // Debug.Log("Allocating compute buffer");
            ComputeBuffer chunkGridBuffer = null;    // Can't allocate from thread - have to trickery to get around type checker
            // Debug.Log("Allocated compute buffer");

            // 4096 threads per shader pass call
            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);

            // Debug.Log("Waiting on bounds");

            // while (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z)
                // Thread.Sleep(300);  // Block until bounds populated

            // Debug.Log("Bounds populated");

            Debug.Log($"Min Point: {data.MinPoint.ToString()}");
            Debug.Log($"Max Point: {data.MaxPoint.ToString()}");

            float[] minPoint = new float[] { data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f };
            float[] maxPoint = new float[] { data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f };

            bbox = new AABB(
                new Vector3(data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f), 
                new Vector3(data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f)
            );
            
            // Initialize compute shader
            await ExecuteAction(() => {
                computeShader = (ComputeShader)Resources.Load("CountAndSort");

                chunkGridBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(UInt32));

                computeShader.SetFloats("_MinPoint", minPoint);
                computeShader.SetFloats("_MaxPoint", maxPoint);

                computeShader.SetInt("_NodeCount", chunkGridSize * chunkGridSize * chunkGridSize);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);
                computeShader.SetInt("_ThreadBudget", threadBudget);
            });

            // Debug.Log("Added action");

            /* while (unityActions.Count > 0) // Wait for unity thread to complete action
                    Thread.Sleep(5); */

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Starting counting");

            // Debug.Log($"Counting, size {System.Runtime.InteropServices.Marshal.SizeOf<Point>()}");
            int pointsCounted = 0;
            int pointsQueued = 0;

            List<Task> countTasks = new List<Task>();

            // Counting loop
            while (pointsQueued < data.PointCount) {
                Point[] batch;
                while (!readQueue.TryDequeue(out batch))
                    Thread.Sleep(5);

                /* foreach (Point pt in batch) {
                    if (pt.pos.x < data.MinPoint.x || pt.pos.y < data.MinPoint.y || pt.pos.z < data.MinPoint.z ||
                        pt.pos.x > data.MaxPoint.x || pt.pos.y > data.MaxPoint.y || pt.pos.z > data.MaxPoint.z)
                        Debug.LogError($"Point OOB: {pt.ToString()}");
                } */

                // Debug.Log("Dequeued batch");

                countTasks.Add(ExecuteAction(() => {
                    // Stopwatch watch = new Stopwatch();
                    // watch.Start();
                    ComputeBuffer batchBuffer = new ComputeBuffer(batch.Length, Point.size);
                    batchBuffer.SetData(batch);

                    // Debug.Log(batch[0].ToString());

                    computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);

                    int countHandle = computeShader.FindKernel("CountPoints");
                    computeShader.SetBuffer(countHandle, "_Points", batchBuffer);
                    computeShader.SetInt("_BatchSize", batch.Length);
                    computeShader.Dispatch(countHandle, 64, 1, 1);

                    /* uint[] testGrid = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
                    chunkGridBuffer.GetData(testGrid);

                    uint gridCount = 0;
                    for (int i = 0; i < testGrid.Length; i++)
                        gridCount += testGrid[i];
                    Debug.Log($"Current grid count is {gridCount}"); */

                    batchBuffer.Release();
                    pointsCounted += batch.Length;
                    // watch.Stop();
                    // Debug.Log($"Batch processed in {watch.ElapsedMilliseconds} ms");
                    // Debug.Log($"Points counted: {pointsCounted}");
                }));

                pointsQueued += batch.Length;
            }

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Queued Counting");

            readQueue.Clear();
            data.LoadPointBatches(batchSize, readQueue);   // Start loading to get head start while merging

            Task.WaitAll(countTasks.ToArray());

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done Counting");

            // Merge sparse chunks

            List<Chunk> chunks = new List<Chunk>();

            int sparseChunkCutoff = 10000000;    // Any chunk with more than sparseChunkCutoff points is written to disk

            uint[] levelGrid = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            await ExecuteAction(() => { chunkGridBuffer.GetData(levelGrid); chunkGridBuffer.Release(); 
                uint gridCount = 0;
                for (int i = 0; i < levelGrid.Length; i++)
                    gridCount += levelGrid[i];
                Debug.Log($"Final grid count is {gridCount}");
            });

            if (doTests) {
                uint gridCount = 0;
                for (int i = 0; i < levelGrid.Length; i++)
                    gridCount += levelGrid[i];
                
                if (gridCount != data.PointCount)
                    Debug.LogError($"Grid test failed. Total count is {gridCount}");
                else
                    Debug.Log("Grid test passed");
            }

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Big Loop");

            for (int level = chunkDepth; level > 0; level--) {
                int levelGridSize = (int)Mathf.Pow(levelGrid.Length, 1/3f);

                Func<int, int, int, uint> levelGridIndex = (int x, int y, int z) => levelGrid[x * levelGridSize * levelGridSize + y * levelGridSize + z];

                uint[] nextLevelGrid = new uint[(levelGridSize/2) * (levelGridSize/2) * (levelGridSize/2)];

                Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: On Level {level}. levelGridSize is {levelGridSize}, nextLevelGrid is {nextLevelGrid}");

                for (int x = 0; x < levelGridSize; x+=2) {
                    for (int y = 0; y < levelGridSize; y+=2)
                        for (int z = 0; z < levelGridSize; z+=2) {
                            void addChunks() {
                                chunks.Add(new Chunk(x, y, z, level)); chunks.Add(new Chunk(x, y, z+1, level));
                                chunks.Add(new Chunk(x, y+1, z, level)); chunks.Add(new Chunk(x, y+1, z+1, level));
                                chunks.Add(new Chunk(x+1, y, z, level)); chunks.Add(new Chunk(x+1, y, z+1, level));
                                chunks.Add(new Chunk(x+1, y+1, z, level)); chunks.Add(new Chunk(x+1, y+1, z+1, level));
                                nextLevelGrid[x/2 * (levelGridSize-1) * (levelGridSize-1) + y/2 * (levelGridSize-1) + z/2] = UInt32.MaxValue;
                            };

                            // If any chunks in cube previously added
                            if (levelGridIndex(x, y, z) == UInt32.MaxValue || levelGridIndex(x, y, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x, y+1, z) == UInt32.MaxValue || levelGridIndex(x, y+1, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x+1, y, z) == UInt32.MaxValue || levelGridIndex(x+1, y, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x+1, y+1, z) == UInt32.MaxValue || levelGridIndex(x+1, y+1, z+1) == UInt32.MaxValue) {
                                addChunks();
                                continue;
                            }

                            uint cubeSum =  
                                levelGridIndex(x, y, z) + levelGridIndex(x, y, z+1) + 
                                levelGridIndex(x, y+1, z) + levelGridIndex(x, y+1, z+1) + 
                                levelGridIndex(x+1, y, z) + levelGridIndex(x+1, y, z+1) + 
                                levelGridIndex(x+1, y+1, z) + levelGridIndex(x+1, y+1, z+1);

                            // If cube below threshold
                            if (cubeSum < sparseChunkCutoff)
                                nextLevelGrid[x/2 * (levelGridSize/2) * (levelGridSize/2) + y/2 * (levelGridSize/2) + z/2] = cubeSum;
                            else
                                addChunks();
                        }
                    // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Did x {x}");
                }

                // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Out of loop");

                levelGrid = nextLevelGrid;
                Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Did Level {level}");
            }

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done Merging");

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
                    int chunkSize = (int)Mathf.Pow(2, chunkDepth-chunk.level);
                    chunk.bbox = new AABB(
                        chunks[
                            chunk.x * chunkSize * chunkGridSize * chunkGridSize + 
                            chunk.y * chunkSize * chunkGridSize + chunk.z * chunkSize
                        ].bbox.Min, 
                        chunks[
                            ((chunk.x+1) * chunkSize - 1) * chunkGridSize * chunkGridSize + 
                            ((chunk.y+1) * chunkSize - 1) * chunkGridSize +
                            ((chunk.z+1) * chunkSize - 1)
                        ].bbox.Max
                    );
                    for (int x = chunk.x * chunkSize; x < (chunk.x + 1) * chunkSize; x++)
                        for (int y = chunk.y * chunkSize; y < (chunk.y + 1) * chunkSize; y++)
                            for (int z = chunk.z * chunkSize; z < (chunk.z + 1) * chunkSize; z++)
                                lut[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z] = i;
                }
            }

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done LUT");

            /* if (doTests) {
                uint gridCount = 0;
                for (int i = 0; i < levelGrid.Length; i++)
                    gridCount += levelGrid[i];
                
                if (gridCount != data.PointCount)
                    Debug.LogError("Grid test failed");
                else
                    Debug.Log("Grid test passed");
            }*/

            // Create chunk files
            Directory.CreateDirectory($"{dirPath}/tmp");

            FileStream[] chunkStreams = new FileStream[chunks.Count];
            (int, Point[])[] chunkBuffers = new (int, Point[])[chunks.Count];

            for (int i = 0; i < chunks.Count; i++) {
                Chunk chunk = chunks[i];
                chunk.path = $"{dirPath}/tmp/chunk_{chunk.x}_{chunk.y}_{chunk.z}_l{chunk.level}";
                chunkStreams[i] = File.Create(chunk.path);
                chunkBuffers[i] = (0, new Point[10]);
            }

            // Distribute points
            ConcurrentQueue<(Point[], uint[])> sortedBatches = new ConcurrentQueue<(Point[], uint[])>();
            int maxSorted = 100;

            
            pointsQueued = 0;
            int pointsSorted = 0;
            int pointsWritten = 0;

            // Start writing
            Task writeChunksTask = Task.Run(() => {
                while (pointsWritten < data.PointCount) {
                    Debug.Log("Writing points");
                    (Point[] batch, uint[] indices) tup;
                    while (!sortedBatches.TryDequeue(out tup)) {}
                        // Debug.Log("Waiting on batch to write");

                    Debug.Log($"Writing batch of length {tup.batch.Length}");

                    for (int i = 0; i < tup.batch.Length; i++) {
                        Debug.Log($"Buffered point {i+1}/{tup.batch.Length}, chunkIdx {lut[tup.indices[i]]}");
                        int chunkIdx = lut[tup.indices[i]];
                        Debug.Log("Adding point to buffer");
                        (int idx, Point[] buffer) bufferTup = chunkBuffers[chunkIdx];
                        Debug.Log("Got buffer");
                        bufferTup.buffer[bufferTup.idx] = tup.batch[i];
                        Debug.Log("Added point");
                        bufferTup.idx++;
                        Debug.Log("Incremented idx");
                        if (bufferTup.idx == 10) {   // Flush when ten points queued
                            Debug.Log("Flushing");
                            chunkStreams[chunkIdx].Write(Point.ToBytes(chunkBuffers[chunkIdx].Item2));
                            chunkBuffers[chunkIdx].Item1 = 0;
                            Debug.Log("Flushed");
                        }
                    }

                    pointsWritten += tup.batch.Length;
                    Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: {pointsWritten} points written");
                }

                // Flush all
                for (int i = 0; i < chunks.Count; i++) {
                    if (chunkBuffers[i].Item1 > 0) {
                        Point[] truncatedStream = new Point[chunkBuffers[i].Item1];
                        for (int j = 0; j < truncatedStream.Length; j++)
                            truncatedStream[j] = chunkBuffers[i].Item2[j];

                        chunkStreams[i].Write(Point.ToBytes(truncatedStream));
                        chunkBuffers[i].Item1 = 0;
                    }
                }
            });

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Started writing");

            // Sort points
            while (pointsQueued < data.PointCount) {
                while (sortedBatches.Count > maxSorted) {}

                Point[] batch;
                while (!readQueue.TryDequeue(out batch)) {}
                    // Debug.Log("Waiting on batch from disk");  

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

                    sortedBatches.Enqueue((batch, chunkIndices));
                    pointsSorted += batch.Length;
                    Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: {pointsSorted} points sorted, {sortedBatches.Count} batches queued");
                });

                pointsQueued += batch.Length;
            }

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Queued sorting");

            while (pointsSorted < data.PointCount) {}
            
            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done sorting");

            while (pointsWritten < data.PointCount) {}

            Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done writing");

            watch.Stop();
            // Subsample chunks

            /* ThreadedWriter tw = new ThreadedWriter($"{dirPath}/octree.bin");
            tw.Start();

            for (int i = 0; i < chunks.Count; i++) {
                Point[] chunk = Point.ToPoints(File.ReadAllBytes(chunks[i].path));
                BuildOctree(chunk, chunks[i].bbox, 2);  // Go two layers at a time to get shallow tree
            }

            tw.Stop(); */
        }

        /* Node BuildLocalOctree(Point[] points, AABB bbox, int layers) {
            int maxNodeSize = 2000000;
            int totalNodeCount = (int)Mathf.Pow(8, layers);
            bool actionComplete = false;

            uint[] nodeGrid = new uint[(int)Mathf.Pow(8, layers)];
            uint[] nodeIndices = new uint[points.Length];

            unityActions.Enqueue(() => {
                ComputeBuffer pointBuffer = new ComputeBuffer(batchSize, 15);
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
            }

        } */

        async Task ExecuteAction(Action a) {
            bool actionCompleted = false;

            unityActions.Enqueue(() => { a(); actionCompleted = true; });

            while (!actionCompleted)
                Thread.Sleep(5);
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