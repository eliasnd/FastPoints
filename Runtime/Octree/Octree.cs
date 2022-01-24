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
            if (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z) 
                await data.PopulateBounds();

            _ = data.LoadPointBatches(batchSize, readQueue);

            // Debug.Log("Started task");

            int chunkDepth = 5;
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

                // foreach (Point pt in batch) {
                //     if (pt.pos.x < data.MinPoint.x || pt.pos.y < data.MinPoint.y || pt.pos.z < data.MinPoint.z ||
                //         pt.pos.x > data.MaxPoint.x || pt.pos.y > data.MaxPoint.y || pt.pos.z > data.MaxPoint.z)
                //         Debug.LogError($"Point OOB: {pt.ToString()}");
                // }

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

                    batchBuffer.Release();
                    pointsCounted += batch.Length;
                    // watch.Stop();
                    // Debug.Log($"Batch processed in {watch.ElapsedMilliseconds} ms");
                    // Debug.Log($"Points counted: {pointsCounted}");
                }));

                pointsQueued += batch.Length;
            }

            Checkpoint("Queued Counting");

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

            // int sparseChunkCutoff = 10000000;    // Any chunk with more than sparseChunkCutoff points is written to disk

            // uint[] levelGrid = data.chunkCounts;

            // if (doTests) {
            //     uint gridCount = 0;
            //     for (int i = 0; i < levelGrid.Length; i++)
            //         gridCount += levelGrid[i];
                
            //     if (gridCount != data.PointCount)
            //         Debug.LogError($"Grid test failed. Total count is {gridCount}");
            //     else
            //         Debug.Log("Grid test passed");
            // }


            Checkpoint("Big Loop");

            for (int level = chunkDepth; level > 0; level--) {
                int levelGridSize = (int)Mathf.Pow(levelGrid.Length, 1/3f);

                Func<int, int, int, uint> levelGridIndex = (int x, int y, int z) => levelGrid[x * levelGridSize * levelGridSize + y * levelGridSize + z];

                uint[] nextLevelGrid = new uint[(levelGridSize/2) * (levelGridSize/2) * (levelGridSize/2)];

                Checkpoint($"On Level {level}. levelGridSize is {levelGridSize}, nextLevelGrid is {levelGridSize/2}");

                for (int x = 0; x < levelGridSize; x+=2) {
                    for (int y = 0; y < levelGridSize; y+=2)
                        for (int z = 0; z < levelGridSize; z+=2) {
                            // if (level < 4)
                            //     Checkpoint($"X: {x}, Y: {y}, Z: {z}");
                            void addChunks() {
                                // if (level < 4)
                                //     Checkpoint($"Adding chunks to idx {x/2}, {y/2}, {z/2}");
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
                                // if (level < 4)
                                //     Checkpoint($"Added chunks, setting nextlevelGrid of length {nextLevelGrid.Length} at idx {x/2 * (levelGridSize-1) * (levelGridSize-1) + y/2 * (levelGridSize-1) + z/2}");
                                nextLevelGrid[x/2 * (levelGridSize/2) * (levelGridSize/2) + y/2 * (levelGridSize/2) + z/2] = UInt32.MaxValue;
                                // if (level < 4)
                                //     Checkpoint("Set nextlevelgrid");
                            };

                            // if (level < 4)
                            //     Checkpoint($"Got here1");

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
                    // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Did x {x}");
                }

                // Debug.Log($"[{(int)(watch.ElapsedMilliseconds / 1000)}]: Out of loop");

                levelGrid = nextLevelGrid;
                Checkpoint($"Did Level {level}");
            }

           Checkpoint($"Done Merging into {chunks.Count} chunks");

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
            Directory.CreateDirectory($"{dirPath}");
            Directory.CreateDirectory($"{dirPath}/tmp");

            long bufferSize = 60;

            FileStream[] chunkStreams = new FileStream[chunks.Count];
            // (int, byte[])[] chunkBuffers = new (int, byte[])[chunks.Count];
            (int, Point[])[] chunkBuffers = new (int, Point[])[chunks.Count];


            for (int i = 0; i < chunks.Count; i++) {
                Chunk chunk = chunks[i];
                if (chunk.size == 0)
                    continue;

                chunk.path = $"{dirPath}/tmp/chunk_{chunk.x}_{chunk.y}_{chunk.z}_l{chunk.level}.bin";
                chunkStreams[i] = File.Create(chunk.path);
                chunkStreams[i].SetLength(chunk.size * 15);
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

            // Start writing
            Task writeChunksTask = Task.Run(() => {
                while (pointsWritten < data.PointCount) {
                    (Point[] batch, uint[] indices) tup;
                    while (!sortedBatches.TryDequeue(out tup)) {}
                        // Debug.Log("Waiting on batch to write");

                    Debug.Log($"Writing batch of length {tup.batch.Length}");

                    for (int i = 0; i < tup.batch.Length; i++) {
                        // if (i > 0)
                        //     Debug.Log($"Checking: now idx of chunk {lut[tup.indices[i-1]]} is {chunkBuffers[lut[tup.indices[i-1]]].Item1}");
                        // Debug.Log($"Buffered point {i+1}/{tup.batch.Length}, chunkIdx {lut[tup.indices[i]]}");
                        Point pt = tup.batch[i];
                        int chunkIdx = lut[tup.indices[i]];

                        chunkBuffers[chunkIdx].Item2[chunkBuffers[chunkIdx].Item1] = pt;
                        chunkBuffers[chunkIdx].Item1++;

                        if (chunkBuffers[chunkIdx].Item1 == bufferSize) {
                            _ = chunkStreams[chunkIdx].WriteAsync(Point.ToBytes(chunkBuffers[chunkIdx].Item2));
                            chunkBuffers[chunkIdx].Item1 = 0;
                        }

                        // Debug.Log("Checking buffer");
                        /* if (chunkBuffers[chunkIdx].Item1 + 15 >= bufferSize) {
                            byte[] ptBytes = pt.ToBytes();
                            int diff = (int)bufferSize - chunkBuffers[chunkIdx].Item1;
                            // Debug.Log("Filling buffer");
                            for (int j = 0; j < diff; j++)
                                chunkBuffers[chunkIdx].Item2[chunkBuffers[chunkIdx].Item1+j] = ptBytes[j];

                            // Debug.Log("Writing buffer");

                            chunkStreams[chunkIdx].Write(chunkBuffers[chunkIdx].Item2);
                            chunkBuffers[chunkIdx].Item1 = 15 - (int)diff;
                            // Debug.Log("Refilling buffer");
                            for (int j = 0; j < 15 - diff; j++)
                                chunkBuffers[chunkIdx].Item2[j] = ptBytes[diff+j];
                            Debug.Log($"Flushed chunk {chunkIdx}");
                        } else {
                            // Debug.Log("Not flushing");
                            pt.ToBytes(chunkBuffers[chunkIdx].Item2, chunkBuffers[chunkIdx].Item1);
                            // Debug.Log("To bytes done");
                            chunkBuffers[chunkIdx].Item1 += 15;
                        } */
                        // else
                            // Debug.Log($"Not flushing yet, chunk {chunkIdx} has buffer idx of {chunkBuffers[lut[tup.indices[i]]].Item1}");
                    }

                    pointsWritten += tup.batch.Length;
                    Checkpoint($"{pointsWritten} points written");
                }

                // Flush all
                /* for (int i = 0; i < chunks.Count; i++) {
                    if (chunkBuffers[i].Item1 > 0) {
                        Point[] truncatedStream = new Point[chunkBuffers[i].Item1];
                        for (int j = 0; j < truncatedStream.Length; j++)
                            truncatedStream[j] = chunkBuffers[i].Item2[j];

                        chunkStreams[i].Write(Point.ToBytes(truncatedStream));
                        chunkBuffers[i].Item1 = 0;
                    }
                } */
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

            // Debug.Log("Checking chunk files");
            // for (int i = 0; i < chunks.Count; i++) {
            //     if (chunks[i].size * 15 != chunkStreams[i].Length)
            //         Debug.LogError($"Chunk {chunks[i].x}, {chunks[i].y}, {chunks[i].z}, l{chunks[i].level} expected {chunks[i].size * 15} bytes, got {chunkStreams[i].Length}");
            // }

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