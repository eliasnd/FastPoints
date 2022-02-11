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
        int batchSize = 5000000;  // Size of batches for reading and running shader

        bool doTests = true;

        // Debug
        Stopwatch watch;
        TimeSpan checkpoint = new TimeSpan();

        public Octree(int treeDepth, string dirPath, ConcurrentQueue<Action> unityActions) {
            this.treeDepth = treeDepth;
            this.unityActions = unityActions;
            this.dirPath = dirPath;
        }

        public async Task BuildTree(PointCloudData data) {
            watch = new Stopwatch();
            watch.Start();
            Checkpoint("Starting");

            ConcurrentQueue<Point[]> readQueue = new ConcurrentQueue<Point[]>();

            _ = data.LoadPointBatches(batchSize, readQueue);

            if (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z) 
                await data.PopulateBounds();

            int chunkDepth = 5;
            int chunkGridSize = (int)Mathf.Pow(2, chunkDepth);
            ComputeBuffer chunkGridBuffer = null;    // Can't allocate from thread - have to trickery to get around type checker

            // 4096 threads per shader pass call
            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);

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

                computeShader.SetInt("_NodeCount", chunkGridSize);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);
                computeShader.SetInt("_ThreadBudget", threadBudget);
            });

            // Debug.Log("Added action");

            Checkpoint("Starting counting");

            // Debug.Log($"Counting, size {System.Runtime.InteropServices.Marshal.SizeOf<Point>()}");
            int pointsCounted = 0;
            int pointsQueued = 0;

            List<Task> countTasks = new List<Task>();

            // Counting loop
            while (pointsQueued < data.PointCount) {
                Point[] batch;
                while (!readQueue.TryDequeue(out batch))
                    Thread.Sleep(5);

                countTasks.Add(ExecuteAction(() => {
                    ComputeBuffer batchBuffer = new ComputeBuffer(batch.Length, Point.size);
                    batchBuffer.SetData(batch);

                    computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);

                    int countHandle = computeShader.FindKernel("CountPoints");
                    computeShader.SetBuffer(countHandle, "_Points", batchBuffer);
                    computeShader.SetInt("_BatchSize", batch.Length);
                    computeShader.Dispatch(countHandle, 64, 1, 1);

                    batchBuffer.Release();
                    pointsCounted += batch.Length;
                }));

                pointsQueued += batch.Length;
            }

            readQueue.Clear();
            data.LoadPointBatches(batchSize, readQueue);   // Start loading to get head start while merging

            Task.WaitAll(countTasks.ToArray());

            Checkpoint("Done Counting");

            // Merge sparse chunks

            List<Chunk> chunks = new List<Chunk>();

            int sparseChunkCutoff = 10000000;    // Any chunk with more than sparseChunkCutoff points is written to disk

            uint[] leafCounts = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            await ExecuteAction(() => { chunkGridBuffer.GetData(leafCounts); chunkGridBuffer.Release(); });

            uint[] levelGrid = leafCounts;
            // data.chunkCounts = leafCounts;

            // IF CHUNKCOUNTS POPULATED

            // List<Chunk> chunks = new List<Chunk>();

            for (int level = chunkDepth; level > 0; level--) {
                int levelGridSize = (int)Mathf.Pow(levelGrid.Length, 1/3f);

                Func<int, int, int, uint> levelGridIndex = (int x, int y, int z) => levelGrid[x * levelGridSize * levelGridSize + y * levelGridSize + z];

                uint[] nextLevelGrid = new uint[(levelGridSize/2) * (levelGridSize/2) * (levelGridSize/2)];

                for (int x = 0; x < levelGridSize; x+=2) {
                    for (int y = 0; y < levelGridSize; y+=2)
                        for (int z = 0; z < levelGridSize; z+=2) {
                            void addChunks() {
                                if (levelGridIndex(x, y, z) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x, y, z, level, levelGridIndex(x, y, z))); 
                                if (levelGridIndex(x, y, z+1) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x, y, z+1, level, levelGridIndex(x, y, z+1)));
                                if (levelGridIndex(x, y+1, z) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x, y+1, z, level, levelGridIndex(x, y+1, z))); 
                                if (levelGridIndex(x, y+1, z+1) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x, y+1, z+1, level, levelGridIndex(x, y+1, z+1)));
                                if (levelGridIndex(x+1, y, z) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x+1, y, z, level, levelGridIndex(x+1, y, z))); 
                                if (levelGridIndex(x+1, y, z+1) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x+1, y, z+1, level, levelGridIndex(x+1, y, z+1)));
                                if (levelGridIndex(x+1, y+1, z) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x+1, y+1, z, level, levelGridIndex(x+1, y+1, z))); 
                                if (levelGridIndex(x+1, y+1, z+1) != UInt32.MaxValue)
                                    chunks.Add(new Chunk(x+1, y+1, z+1, level, levelGridIndex(x+1, y+1, z+1)));
                                nextLevelGrid[x/2 * (levelGridSize/2) * (levelGridSize/2) + y/2 * (levelGridSize/2) + z/2] = UInt32.MaxValue;
                            };

                            // If any chunks in cube previously added
                            if (levelGridIndex(x, y, z) == UInt32.MaxValue || levelGridIndex(x, y, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x, y+1, z) == UInt32.MaxValue || levelGridIndex(x, y+1, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x+1, y, z) == UInt32.MaxValue || levelGridIndex(x+1, y, z+1) == UInt32.MaxValue ||
                                levelGridIndex(x+1, y+1, z) == UInt32.MaxValue || levelGridIndex(x+1, y+1, z+1) == UInt32.MaxValue) {
                                addChunks();
                                continue;
                            }

                            // if (level < 4)
                            //     Checkpoint($"Got here2");

                            uint cubeSum =  
                                levelGridIndex(x, y, z) + levelGridIndex(x, y, z+1) + 
                                levelGridIndex(x, y+1, z) + levelGridIndex(x, y+1, z+1) + 
                                levelGridIndex(x+1, y, z) + levelGridIndex(x+1, y, z+1) + 
                                levelGridIndex(x+1, y+1, z) + levelGridIndex(x+1, y+1, z+1);

                            // if (level < 4)
                            //     Checkpoint($"Got here3");

                            // If cube below threshold
                            if (cubeSum < sparseChunkCutoff)
                                nextLevelGrid[x/2 * (levelGridSize/2) * (levelGridSize/2) + y/2 * (levelGridSize/2) + z/2] = cubeSum;
                            else
                                addChunks();
                        }
                }

                levelGrid = nextLevelGrid;
            }


            AABB[] chunkBBox = bbox.Subdivide(chunkGridSize);

            // Checkpoint($"Subdivided into {chunkBBox.Length} bbox");

            // Create lookup table
            int[] lut = new int[chunkGridSize * chunkGridSize * chunkGridSize];
            for (int i = 0; i < chunks.Count; i++) {
                // Checkpoint($"Processing chunk {i}");
                Chunk chunk = chunks[i];

                try {

                    if (chunk.level == chunkDepth) {
                        // Checkpoint($"Handling leaf");
                        int chunkIdx = chunk.x * chunkGridSize * chunkGridSize + chunk.y * chunkGridSize + chunk.z;
                        lut[chunkIdx] = i;
                        chunk.bbox = chunkBBox[i];
                        chunks[i] = chunk;
                    }
                    else {
                        // Checkpoint($"Handling merged");
                        int chunkSize = (int)Mathf.Pow(2, chunkDepth-chunk.level);
                        chunk.bbox = new AABB(
                            chunkBBox[
                                chunk.x * chunkSize * chunkGridSize * chunkGridSize + 
                                chunk.y * chunkSize * chunkGridSize + chunk.z * chunkSize
                            ].Min, 
                            chunkBBox[
                                ((chunk.x+1) * chunkSize - 1) * chunkGridSize * chunkGridSize + 
                                ((chunk.y+1) * chunkSize - 1) * chunkGridSize +
                                ((chunk.z+1) * chunkSize - 1)
                            ].Max
                        );
                        chunks[i] = chunk;
                        // Checkpoint($"Made bbox, now writing {chunkSize * chunkSize * chunkSize} lut entries");
                        for (int x = chunk.x * chunkSize; x < (chunk.x + 1) * chunkSize; x++)
                            for (int y = chunk.y * chunkSize; y < (chunk.y + 1) * chunkSize; y++)
                                for (int z = chunk.z * chunkSize; z < (chunk.z + 1) * chunkSize; z++) 
                                    lut[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z] = i;
                        // Checkpoint("Wrote lut");
                    }
                    // Checkpoint($"Processed chunk {i}");
                } catch (Exception e) {
                    throw e;
                }
            }

            Checkpoint($"Done LUT");

            // Create chunk files
            Directory.CreateDirectory($"{dirPath}");
            Directory.CreateDirectory($"{dirPath}/tmp");

            long bufferSize = 60;

            // FileStream[] chunkStreams = new FileStream[chunks.Count];
            // (int, byte[])[] chunkBuffers = new (int, byte[])[chunks.Count];
            (int, Point[])[] chunkBuffers = new (int, Point[])[chunks.Count];


            for (int i = 0; i < chunks.Count; i++) {

                Chunk chunk = chunks[i];
                if (chunk.size == 0)
                    continue;

                chunk.path = $"{dirPath}/tmp/chunk_{chunk.x}_{chunk.y}_{chunk.z}_l{chunk.level}.bin";
                chunks[i] = chunk;
                // chunkStreams[i] = File.Create(chunk.path);
                // chunkStreams[i].SetLength(chunk.size * 15);
                // chunkStreams[i] = File.OpenWrite(chunk.path);
                // Debug.Log($"Intial file length {i}: {new FileInfo(chunk.path).Length}");
                // chunkBuffers[i] = (0, new byte[bufferSize]);
                chunkBuffers[i] = (0, new Point[bufferSize]);
            }

            // Distribute points
            ConcurrentQueue<(Point[], uint[])> sortedBatches = new ConcurrentQueue<(Point[], uint[])>();
            int maxSorted = 100;

            
            pointsQueued = 0;
            int pointsSorted = 0;
            int pointsWritten = 0;

            ThreadedWriter chunkWriter = new ThreadedWriter(12);

            // Start writing
            Task writeChunksTask = Task.Run(() => {
                List<Point>[] sorted = new List<Point>[chunks.Count];
                for (int i = 0; i < chunks.Count; i++)
                    sorted[i] = new List<Point>();

                while (pointsWritten < data.PointCount) {
                    (Point[] batch, uint[] indices) tup;
                    while (!sortedBatches.TryDequeue(out tup)) {}
                        // Debug.Log("Waiting on batch to write");

                    // Debug.Log($"Writing batch of length {tup.batch.Length}");

                    for (int i = 0; i < tup.batch.Length; i++)
                        sorted[lut[tup.indices[i]]].Add(tup.batch[i]);
                    

                    for (int i = 0; i < chunks.Count; i++) {
                        if (sorted[i].Count == 0)
                            continue;

                        // chunkStreams[i].WriteAsync(Point.ToBytes(sorted[i].ToArray()));
                        chunkWriter.Write(chunks[i].path, Point.ToBytes(sorted[i].ToArray()));
                        sorted[i].Clear();
                    }

                    pointsWritten += tup.batch.Length;
                    Checkpoint($"{pointsWritten} points written");
                }
            });

            Checkpoint("Started writing");

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
                    // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: {pointsSorted} points sorted, {sortedBatches.Count} batches queued");
                });

                pointsQueued += batch.Length;
            }

            // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Queued sorting");

            while (pointsSorted < data.PointCount) {}

            Checkpoint("Done sorting");
            
            // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done sorting");

            while (pointsWritten < data.PointCount) {}

            // Debug.Log("Checking chunk files");
            // for (int i = 0; i < chunks.Count; i++) {
            //     if (chunks[i].size * 15 != chunkStreams[i].Length)
            //         Debug.LogError($"Chunk {chunks[i].x}, {chunks[i].y}, {chunks[i].z}, l{chunks[i].level} expected {chunks[i].size * 15} bytes, got {chunkStreams[i].Length}");
            // }

            Checkpoint("Done writing");

            // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Done writing");

            // for (int i = 0; i < chunkStreams.Length; i++)
            //     if (chunkStreams[i] != null)
            //         chunkStreams[i].Close();

            string treePath = $"{dirPath}/octree.dat";
            string metaPath = $"{dirPath}/meta.dat";
            // File.Create(treePath).Close();
            // File.Create(metaPath).Close();

            QueuedWriter treeQW = new QueuedWriter(treePath);
            QueuedWriter metaQW = new QueuedWriter(metaPath);   // Leave room at front for one uint pointer per chunk

            // Debug.Log("Starting writers");

            treeQW.Start();
            metaQW.Start((uint)(chunks.Count * 4));
            
            int parallelChunkCount = 4; // Number of chunks to do in parallel

            uint[] chunkOffsets = new uint[chunks.Count];   // Offsets of each chunk into meta.dat

            int activeTasks = 0;

            uint maxMemoryUsage = 1024 * 1024 * 1024;    // 1 GB Ram
            object memoryUsageLock = new object();
            uint memoryUsage = 0;

            int tmp = 0;
            foreach (Chunk c in chunks) {
                int idx = tmp;
                tmp++;
                while (true)
                {
                    if (memoryUsage + (c.size * Point.size) >= maxMemoryUsage)
                        Thread.Sleep(50);
                    else
                    {
                        lock (memoryUsageLock)
                        {
                            if (memoryUsage + (c.size * Point.size) >= maxMemoryUsage)
                                continue;

                            memoryUsage += c.size * Point.size;
                            break;
                        }
                    }
                }

                Task.Run(async () => {
                    // Debug.Log($"Building local octree for chunk {idx}");

                    // string treePath = $"{dirPath}/tmp/octree_{chunks[idx].x}_{chunks[idx].y}_{chunks[idx].z}_l{chunks[idx].level}.dat";
                    // string metaPath = $"{dirPath}/tmp/meta_{chunks[idx].x}_{chunks[idx].y}_{chunks[idx].z}_l{chunks[idx].level}.dat";
                    // File.Create(treePath).Close();
                    // File.Create(metaPath).Close();

                    // QueuedWriter treeQW = new QueuedWriter(treePath);
                    // QueuedWriter metaQW = new QueuedWriter(metaPath); 

                    // QueuedWriter localTreeQW = new QueuedWriter(new MemoryStream((int)(chunks[idx].size * 15)));

                    // localTreeQW.Start();
                    Debug.Log($"Writing octree for chunk {idx}");

                    Task<Node> bt = BuildLocalOctree(c, treeQW);
                    await bt;
                    Node root = bt.Result;

                    Debug.Log($"Chunk {idx} written to file");

                    Node[] nodes = new Node[root.descendentCount+1];
                    await root.FlattenTree(nodes);

                    byte[] hierarchyBytes = new byte[nodes.Length * 36];
                    for (int n = 0; n < nodes.Length; n++)
                        nodes[n].ToBytes(hierarchyBytes, n*36);

                    Debug.Log($"Chunk {idx} hierarchy generated");

                    metaQW.Enqueue(BitConverter.GetBytes(metaQW.Enqueue(hierarchyBytes)), (uint)(idx * 4));   // Write index of chunk hierarchy to header

                    Debug.Log($"Finished chunk {idx}");
                    lock (memoryUsageLock)
                    {
                        memoryUsage -= chunks[idx].size * Point.size;
                    }
                    activeTasks--;
                });

                Thread.Sleep(25);
                activeTasks++;
            }

            watch.Stop();
        }

        async Task<Node> BuildLocalOctree(Chunk c, QueuedWriter qw) {
            FileStream fs = File.Open(c.path, FileMode.Open);
            byte[] bytes = new byte[c.size * 15];
            fs.Read(bytes);
            Point[] points = Point.ToPoints(bytes);
            Task<Node> t = BuildLocalOctree(points, c.bbox, qw);
            await t;
            return t.Result;
        }

        // Look into using single point array to avoid unnecessary reallocation
        async Task<Node> BuildLocalOctree(Point[] points, AABB bbox, QueuedWriter qw) {

            // Local octree parameters
            int subsampleCount = 100000;    // Subsample 100000 points per inner node
            int sparseNodeCutoff = 3000000;
            int nodeCountAxis = (int)Mathf.Pow(2, treeDepth);
            int nodeCountTotal = nodeCountAxis * nodeCountAxis * nodeCountAxis;

            // Action inputs
            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);
            float[] minPoint = new float[] { bbox.Min.x-1E-5f, bbox.Min.y-1E-5f, bbox.Min.z-1E-5f };
            float[] maxPoint = new float[] { bbox.Max.x+1E-5f, bbox.Max.y+1E-5f, bbox.Max.z+1E-5f };

            // Action outputs
            uint[] nodeCounts = new uint[nodeCountTotal];
            uint[] nodeStarts = new uint[nodeCountTotal];
            Point[] sortedPoints = new Point[points.Length];

            // Maybe separate into two executeaction calls?
            await ExecuteAction(() => {
                // Initialize shader

                computeShader.SetInt("_BatchSize", points.Length);
                computeShader.SetInt("_ThreadBudget", threadBudget);
                computeShader.SetFloats("_MinPoint", minPoint);
                computeShader.SetFloats("_MaxPoint", maxPoint);

                ComputeBuffer pointBuffer = new ComputeBuffer(points.Length, Point.size);
                pointBuffer.SetData(points);

                // Count shader

                ComputeBuffer countBuffer = new ComputeBuffer(nodeCountTotal, sizeof(uint));

                int countHandle = computeShader.FindKernel("CountPoints");
                computeShader.SetBuffer(countHandle, "_Points", pointBuffer);
                computeShader.SetBuffer(countHandle, "_Counts", countBuffer);

                computeShader.Dispatch(countHandle, 64, 1, 1);

                // Create start idx from counts - maybe do on octree thread?
                countBuffer.GetData(nodeCounts);

                nodeStarts[0] = 0;
                for (int i = 1; i < nodeStarts.Length; i++)
                    nodeStarts[i] = nodeStarts[i-1] + nodeCounts[i-1];

                // Sort shader

                ComputeBuffer sortedBuffer = new ComputeBuffer(points.Length, Point.size);
                ComputeBuffer startBuffer = new ComputeBuffer(nodeCountTotal, sizeof(uint));
                startBuffer.SetData(nodeStarts);
                ComputeBuffer offsetBuffer = new ComputeBuffer(nodeCountTotal, sizeof(uint));
                uint[] chunkOffsets = new uint[nodeCountTotal];   // Initialize to 0s?
                offsetBuffer.SetData(chunkOffsets);

                int sortHandle = computeShader.FindKernel("SortLinear");  // Sort points into array
                computeShader.SetBuffer(sortHandle, "_Points", pointBuffer);
                computeShader.SetBuffer(sortHandle, "_SortedPoints", sortedBuffer); // Must output adjacent nodes in order
                computeShader.SetBuffer(sortHandle, "_ChunkStarts", startBuffer);
                computeShader.SetBuffer(sortHandle, "_ChunkOffsets", offsetBuffer);

                computeShader.Dispatch(sortHandle, 64, 1, 1);

                sortedBuffer.GetData(sortedPoints);

                // Subsample shader eventually

                // int subsampleHandle = computeShader.FindKernel("SubsamplePoints");
                // computeShader.SetBuffer(sortHandle, "_SortedPoints", sortedBuffer);
                // computeShader.SetBuffer(sortHandle, "_Counts", countBuffer);
                // computeShader.SetInt("_SubsampleCount", subsampleCount);

                // int i = treeDepth;
                // for (i; i > 4; i--) {   // GPU subsampling up to cutoff

                // }

                // Clean up
                sortedBuffer.Release();
                pointBuffer.Release();
                startBuffer.Release();
                offsetBuffer.Release();
                countBuffer.Release();
                pointBuffer.Release();
            });

            Debug.Log("Did sorting call");

            // Recursively build tree
            AABB[] subdivisions = bbox.Subdivide(nodeCountAxis);

            Node[] currLayer = new Node[nodeCountTotal];

            List<Task<Node>> recursiveCalls = new List<Task<Node>>();
            int[] recursiveCallIndices = new int[nodeCountTotal];
            int recursiveCount = 0;

            for (int i = 0; i < nodeCountTotal; i++) {
                Point[] nodePoints = new Point[nodeCounts[i]];
                for (int j = 0; j < nodeCounts[i]; j++) {
                    nodePoints[j] = sortedPoints[nodeStarts[i]+j];
                }
                if (nodeCounts[i] > sparseNodeCutoff) {  // Maximum size for single node
                    recursiveCallIndices[i] = recursiveCalls.Count;
                    recursiveCalls.Add(BuildLocalOctree(nodePoints, subdivisions[i], qw));
                    recursiveCount++;
                }
                else {
                    currLayer[i] = new Node(nodePoints, subdivisions[i]);
                    if (nodeCounts[i] > 0)
                        currLayer[i].WriteNode(qw);

                    recursiveCallIndices[i] = -1;
                }
            }

            Debug.Log($"Waiting on {recursiveCount} recursive calls");

            Task.WaitAll(recursiveCalls.ToArray());

            Debug.Log("Recursive calls done");

            uint totalCount = 0;
            foreach (Node n in currLayer)
                totalCount += n.pointCount;
            if (totalCount < points.Length)
                Debug.LogError($"Expected {points.Length} points, got {totalCount}");

            for (int i = 0; i < nodeCountTotal; i++) {
                if (recursiveCallIndices[i] != -1)
                    currLayer[i] = recursiveCalls[recursiveCallIndices[i]].Result;
            }

            //int maxLevel = -1;
            //foreach (Node n in currLayer)
            //    maxLevel = n.level > maxLevel ? n.level : maxLevel;
            // foreach (Node n in currLayer)
            //     n.SetLevel(maxLevel);

            // Populate inner nodes
            Debug.Log("Populating inner nodes");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            int layerSize = nodeCountAxis;
            for (int i = treeDepth-1; i >= 0; i--) {
                Func<int, int, int, Node> levelGridIndex = (int x, int y, int z) => currLayer[x*layerSize*layerSize + y*layerSize + z];

                int nextLayerSize = (int)Mathf.Pow(2, i);
                Node[] nextLayer = new Node[nextLayerSize * nextLayerSize * nextLayerSize];

                Debug.Log($"Populating layer {i} with size {nextLayerSize} at {watch.ElapsedMilliseconds} ms");


                for (int z = 0; z < nextLayerSize; z++) {
                    for (int y = 0; y < nextLayerSize; y++) {
                        for (int x = 0; x < nextLayerSize; x++) {
                            if (levelGridIndex(x*2, y*2, z*2).pointCount == 0 && levelGridIndex(x*2, y*2, z*2+1).pointCount == 0 && 
                                levelGridIndex(x*2, y*2+1, z*2).pointCount == 0 && levelGridIndex(x*2, y*2+1, z*2+1).pointCount == 0 && 
                                levelGridIndex(x*2+1, y*2, z*2).pointCount == 0 && levelGridIndex(x*2+1, y*2, z*2+1).pointCount == 0 && 
                                levelGridIndex(x*2+1, y*2+1, z*2).pointCount == 0 && levelGridIndex(x*2+1, y*2+1, z*2+1).pointCount == 0) {
                                Node n = new Node(new Point[0], new AABB(
                                    levelGridIndex(x*2,y*2,z*2).bbox.Min, 
                                    levelGridIndex(x*2+1,y*2+1,z*2+1).bbox.Max
                                ));
                                nextLayer[x*nextLayerSize*nextLayerSize+y*nextLayerSize+z] = n;
                            } else {
                                Debug.Log("Else");
                                Node n = new Node(new Node[] {
                                    levelGridIndex(x*2, y*2, z*2), levelGridIndex(x*2, y*2, z*2+1), 
                                    levelGridIndex(x*2, y*2+1, z*2), levelGridIndex(x*2, y*2+1, z*2+1), 
                                    levelGridIndex(x*2+1, y*2, z*2), levelGridIndex(x*2+1, y*2, z*2+1), 
                                    levelGridIndex(x*2+1, y*2+1, z*2), levelGridIndex(x*2+1, y*2+1, z*2+1)
                                }, subsampleCount);
                                Debug.Log("Here1");
                                n.WriteNode(qw);
                                Debug.Log("Here2");
                                nextLayer[x*nextLayerSize*nextLayerSize+y*nextLayerSize+z] = n;
                            }
                        }
                    }
                    
                    
                }
                currLayer = nextLayer;
                layerSize = nextLayerSize;
            }

            watch.Stop();
            Debug.Log($"Populating inner nodes took {watch.ElapsedMilliseconds} ms");

            return currLayer[0];
        }

        async Task ExecuteAction(Action a) {
            bool actionCompleted = false;

            unityActions.Enqueue(() => { a(); actionCompleted = true; });

            while (!actionCompleted)
                Thread.Sleep(5);
        }

        void Checkpoint(string msg) {
            Debug.Log($"[{watch.Elapsed.ToString(@"mm\:ss\:fff")} ({watch.Elapsed.Subtract(checkpoint).ToString(@"mm\:ss\:fff")} since last checkpoint)]: {msg}");
            checkpoint = watch.Elapsed;
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

    struct Chunk {
        public uint size;
        public AABB bbox;
        public string path;
        public int level;
        public int x;
        public int y;
        public int z;
        public Chunk(int x, int y, int z, int level, uint size) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.level = level;
            this.bbox = new AABB(new Vector3(1,1,1), new Vector3(0,0,0));
            this.path = null;
            this.size = size;
        }
    }
}