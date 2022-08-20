using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace FastPoints {
    public class Chunker {
        static int batchSize = 1000000;
        static int chunkDepth = 5;
        static int maxChunkSize = 10000000;

        public static async Task MakeChunks(PointCloudData data, string targetDir, Dispatcher dispatcher) {
            // Debug.Log("C1");
            ComputeShader computeShader = null;
            ConcurrentQueue<Point[]> readQueue = new ConcurrentQueue<Point[]>();

            _ = data.LoadPointBatches(batchSize, readQueue);
            // _ = data.LoadPointBatches(100, readQueue);  // DEBUG_CODE

            // float[] minPoint = new float[] { data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f };
            // float[] maxPoint = new float[] { data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f };
            float[] minPoint = new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z };
            float[] maxPoint = new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z };

            int chunkDepth = 5;
            int chunkGridSize = (int)Mathf.Pow(2, chunkDepth-1);
            ComputeBuffer chunkGridBuffer = null;  

            AABB[] testSubdivide = new AABB(new Vector3(0, 0, 0), new Vector3(16, 16, 16)).Subdivide(chunkGridSize);

            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);

            // Debug.Log("C2");

            // INIT COMPUTE SHADER

            int[] mortonIndices = new int[chunkGridSize * chunkGridSize * chunkGridSize];

            for (int x = 0; x < chunkGridSize; x++)
            for (int y = 0; y < chunkGridSize; y++)
            for (int z = 0; z < chunkGridSize; z++)
                mortonIndices[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z] = Utils.MortonEncode(z, y, x);

            // int maxMorton = mortonIndices.Max();

            // await dispatcher.EnqueueAsync(() => {
            //     computeShader = (ComputeShader)Resources.Load("CountAndSort");

            //     chunkGridBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(uint));

            //     computeShader.SetFloats("_MinPoint", minPoint);
            //     computeShader.SetFloats("_MaxPoint", maxPoint);

            //     ComputeBuffer mortonBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(int));
            //     mortonBuffer.SetData(mortonIndices);
            //     computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_MortonIndices", mortonBuffer);
            //     computeShader.SetBuffer(computeShader.FindKernel("SortPoints"), "_MortonIndices", mortonBuffer);

            //     computeShader.SetInt("_NodeCount", chunkGridSize);
            //     computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);
            //     computeShader.SetInt("_ThreadBudget", threadBudget);
            // });

            // Debug.Log("C3");

            // COUNTING

            int pointsCounted = 0;
            int pointsQueued = 0;

            long activeTasks = 0;

            // DEBUG_CODE
            uint[] testCounts = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            AABB[] bbs = new AABB(data.MinPoint, data.MaxPoint).Subdivide(chunkGridSize);

            int threadCount = 10;
            Thread[] countThreads = new Thread[threadCount];
            CountThreadParams[] threadParams = new CountThreadParams[threadCount];
            object counterLock = new();

            uint pointsToCount = (uint)data.PointCount;

            // for (int i = 0; i < threadCount; i++) {
            //     countThreads[i] = new Thread(new ParameterizedThreadStart(CountThread));
            //     threadParams[i] = new CountThreadParams(readQueue, new AABB(data.MinPoint, data.MaxPoint), mortonIndices, counterLock, testCounts, (uint batchSize) => {
            //         pointsToCount -= batchSize;
            //         Debug.Log($"Points counted: {data.PointCount - pointsToCount}");
            //     }, 3);
            //     countThreads[i].Start(threadParams[i]);
            // }

            // while (pointsToCount > 0)
            //     Thread.Sleep(300);

            int GetChunk(Point p) {
                Vector3 size = data.MaxPoint - data.MinPoint;

                float x_norm = (p.pos.x - data.MinPoint.x) / size.x;
                float y_norm = (p.pos.y - data.MinPoint.y) / size.y;
                float z_norm = (p.pos.z - data.MinPoint.z) / size.z;

                int x = (int)Mathf.Min(x_norm * chunkGridSize, chunkGridSize-1);
                int y = (int)Mathf.Min(y_norm * chunkGridSize, chunkGridSize-1);
                int z = (int)Mathf.Min(z_norm * chunkGridSize, chunkGridSize-1);

                return mortonIndices[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z];
            }

            while (pointsToCount > 0) {
                Point[] batch;
                while (!readQueue.TryDequeue(out batch)) 
                    Thread.Sleep(5);
                for (int j = 0; j < batch.Length; j++)
                    testCounts[GetChunk(batch[j])]++;
                pointsToCount -= (uint)batch.Length;
                Debug.Log($"{pointsToCount} points left to count");
            }

            // for (int i = 0; i < threadCount; i++) {
            //     lock (threadParams[i].paramsLock) {
            //         threadParams[i].stopSignal = true;
            //     }
            // }

            // for (int i = 0; i < threadCount; i++)
            //     countThreads[i].Join();

            Debug.Log("Joined");

            readQueue.Clear();
            _ = data.LoadPointBatches(batchSize, readQueue);   // Start loading to get head start while merging

            while (Interlocked.Read(ref activeTasks) > 0)
                Thread.Sleep(10);

            // uint[] leafCounts = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            // await dispatcher.EnqueueAsync(() => { chunkGridBuffer.GetData(leafCounts); chunkGridBuffer.Release(); });

            uint[] leafCounts = testCounts;

            // DEBUG_CODE
            // for (int i = 0; i < chunkGridSize * chunkGridSize * chunkGridSize; i++)
            //     if (leafCounts[i] != testCounts[i])
            //         Debug.LogError($"Chunk {i} with bbox {bbs[i]} had cpu count {testCounts[i]}, gpu count {leafCounts[i]}");

            // Debug.Log("Test done");

            // Debug.Log("C4");

            uint sum = 0;
            for (int i = 0; i < leafCounts.Length; i++)
                sum += leafCounts[i];
            if (sum != data.PointCount)
                Debug.LogError($"Expected {data.PointCount} points, got {sum}");

            // MAKE SUM PYRAMID

            uint[][] sumPyramid = new uint[chunkDepth][];
            sumPyramid[chunkDepth-1] = leafCounts;

            for (int level = chunkDepth-2; level >= 0; level--) {
                int levelDim = (int)Mathf.Pow(2, level);
                uint[] currLevel = new uint[levelDim * levelDim * levelDim];

                uint[] lastLevel = sumPyramid[level+1];

                for (int x = 0; x < levelDim; x++)
                    for (int y = 0; y < levelDim; y++)
                        for (int z = 0; z < levelDim; z++)
                        {
                            int chunkIdx = Utils.MortonEncode(z, y, x);
                            // Debug.Log($"Test: Morton code for {x}, {y}, {z} = {chunkIdx}");
                            int childIdx = Utils.MortonEncode(2*z, 2*y, 2*x);
                            currLevel[chunkIdx] =
                                lastLevel[childIdx] + lastLevel[childIdx+1] + lastLevel[childIdx+2] + lastLevel[childIdx+3] +
                                lastLevel[childIdx+4] + lastLevel[childIdx+5] + lastLevel[childIdx+6] + lastLevel[childIdx+7];
                        }

                sumPyramid[level] = currLevel;
            }

            // Debug.Log("C5");

            // MAKE LUT

            List<string> chunkPaths = new();
            List<AABB> chunkBBox = new();   // DEBUG
            int[] lut = new int[chunkGridSize * chunkGridSize * chunkGridSize];

            uint total = 0;

            void AddToLUT(int level, int x, int y, int z)
            {
                int idx = Utils.MortonEncode(z, y, x);
                AABB bbox = new AABB(data.MinPoint, data.MaxPoint).Subdivide((int)Mathf.Pow(2, level))[idx];

                if (sumPyramid[level][idx] == 0)
                    return;

                if (level == chunkDepth-1)  // If at bottom, automatically add
                {
                    lut[idx] = chunkPaths.Count;
                    chunkPaths.Add($"{targetDir}/chunks/{ToNodeID(level, (int)Mathf.Pow(2, level), (int)x, (int)y, (int)z)}.dat");
                    chunkBBox.Add(bbox);
                    total += sumPyramid[level][idx];
                }
                else if (sumPyramid[level][idx] < maxChunkSize) // Else, add if below max chunk size
                {
                    int chunkIdx = chunkPaths.Count;
                    chunkPaths.Add($"{targetDir}/chunks/{ToNodeID(level, (int)Mathf.Pow(2, level), (int)x, (int)y, (int)z)}.dat");
                    chunkBBox.Add(bbox);
                    int chunkSize = (int)Mathf.Pow(2, chunkDepth-1-level);    // CHECK
                    int childIdx = Utils.MortonEncode(chunkSize*z, chunkSize*y, chunkSize*x);
                    for (int i = 0; i < chunkSize * chunkSize * chunkSize; i++)
                        lut[childIdx+i] = chunkIdx;
                    total += sumPyramid[level][idx];
                }
                else   // Recursively call on children
                {
                    AddToLUT(level+1, 2*x, 2*y, 2*z);
                    AddToLUT(level+1, 2*x, 2*y, 2*z+1);
                    AddToLUT(level+1, 2*x, 2*y+1, 2*z);
                    AddToLUT(level+1, 2*x, 2*y+1, 2*z+1);
                    AddToLUT(level+1, 2*x+1, 2*y, 2*z);
                    AddToLUT(level+1, 2*x+1, 2*y, 2*z+1);
                    AddToLUT(level+1, 2*x+1, 2*y+1, 2*z);
                    AddToLUT(level+1, 2*x+1, 2*y+1, 2*z+1);
                }
            }

            AddToLUT(0, 0, 0, 0);

            Debug.Log($"Total count is {total}");

            // Debug.Log("C6");

            // throw new Exception();

            // START WRITING CHUNKS

            ConcurrentQueue<(Point[], uint[])> sortedBatches = new ConcurrentQueue<(Point[], uint[])>();
            int maxSorted = 100;

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.CreateDirectory($"{targetDir}");
            Directory.CreateDirectory($"{targetDir}/chunks");

            // Debug.Log("Chunks are:");
            FileStream[] chunkStreams = new FileStream[chunkPaths.Count];
            for (int i = 0; i < chunkPaths.Count; i++) {
                chunkStreams[i] = File.OpenWrite(chunkPaths[i]);
                // Debug.Log($"Chunk {i} at path {chunkPaths[i]} with bbox {chunkBBox[i].ToString()}");
            }


            ThreadedWriter chunkWriter = new ThreadedWriter(1);

            Task writeChunksTask = Task.Run(() => {
                int chunkCount = chunkPaths.Count;

                List<Point>[] sorted = new List<Point>[chunkCount];
                for (int i = 0; i < chunkCount; i++)
                    sorted[i] = new();

                int pointsToWrite = data.PointCount;

                List<Point> r245Points = new List<Point>();

                while (pointsToWrite > 0) {
                    (Point[] batch, uint[] indices) tup;
                    while (!sortedBatches.TryDequeue(out tup))
                        Thread.Sleep(5);

                    for (int i = 0; i < tup.batch.Length; i++)
                        sorted[lut[tup.indices[i]]].Add(tup.batch[i]);
                       
                    

                    for (int i = 0; i < chunkCount; i++) {
                        if (sorted[i].Count == 0)
                            continue;

                        Vector3 min = chunkBBox[i].Min;
                        Vector3 max = chunkBBox[i].Max;

                        // foreach (Point p in sorted[i]) {
                        //     if (!chunkBBox[i].InAABB(p.pos))
                        //         Debug.LogError($"Chunking found points outside bbox for {chunkPaths[i]}");
                        //     else if (i == 76 && Mathf.Abs(p.pos.x - 8.98805f) < 0.00001f && Mathf.Abs(p.pos.y - 0.3535893f) < 0.00001f && Mathf.Abs(p.pos.z - -1.575418f) < 0.0001f) {
                        //         Debug.Log("Heretest");
                        //         float z_diff = p.pos.z - -1.575418f;
                        //         chunkBBox[i].InAABB(p.pos);
                        //     }

                        //     if (Path.GetFileNameWithoutExtension(chunkPaths[i]) == "r245")
                        //         r245Points.Add(p);

                        // }
                        // File.OpenWrite(chunkPaths[i]).WriteAsync(Point.ToBytes(sorted[i].ToArray()));
                        chunkStreams[i].Write(Point.ToBytes(sorted[i].ToArray()));
                        // chunkWriter.Write(chunkPaths[i], Point.ToBytes(sorted[i].ToArray()));
                        sorted[i].Clear();
                    }

                    pointsToWrite -= tup.batch.Length;
                    Debug.Log($"{pointsToWrite} points left to write");
                }

                while (chunkWriter.BytesWritten < data.PointCount * 15)
                    Thread.Sleep(500);

                for (int i = 0; i < chunkPaths.Count; i++) {
                    chunkStreams[i].Close();
                }

                // r245Points.Sort();
                // foreach (Point pt in r245Points)
                //     Debug.Log($"r245: {pt.ToString()}");
            });

            // Debug.Log("C7");

            // SORT POINTS

            // int GetChunk(Point p) {
            //     Vector3 size = data.MaxPoint - data.MinPoint;

            //     float x_norm = (p.pos.x - data.MinPoint.x) / size.x;
            //     float y_norm = (p.pos.y - data.MinPoint.y) / size.y;
            //     float z_norm = (p.pos.z - data.MinPoint.z) / size.z;

            //     int x = (int)Mathf.Min(x_norm * chunkGridSize, chunkGridSize-1);
            //     int y = (int)Mathf.Min(y_norm * chunkGridSize, chunkGridSize-1);
            //     int z = (int)Mathf.Min(z_norm * chunkGridSize, chunkGridSize-1);

            //     return mortonIndices[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z];
            // }

            int pointsToQueue = data.PointCount;
            while (pointsToQueue > 0) {
                while (sortedBatches.Count > maxSorted) {}

                Point[] batch;
                while (Interlocked.Read(ref activeTasks) > 100 || !readQueue.TryDequeue(out batch))
                // while (!readQueue.TryDequeue(out batch))
                    Thread.Sleep(5);
                    // Debug.Log("Waiting on batch from disk");  

                uint[] chunkIndices = new uint[batch.Length];             

                // Interlocked.Increment(ref activeTasks);
                // dispatcher.Enqueue(() => {
                //     ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, Point.size);
                //     batchBuffer.SetData(batch);

                //     ComputeBuffer sortedBuffer = new ComputeBuffer(batchSize, sizeof(uint));

                //     int sortHandle = computeShader.FindKernel("SortPoints");
                //     computeShader.SetBuffer(sortHandle, "_Points", batchBuffer);
                //     computeShader.SetBuffer(sortHandle, "_ChunkIndices", sortedBuffer);
                //     computeShader.SetInt("_BatchSize", batch.Length);
                //     computeShader.Dispatch(sortHandle, 64, 1, 1);

                //     batchBuffer.Release();

                //     sortedBuffer.GetData(chunkIndices);
                //     sortedBuffer.Release();

                //     sortedBatches.Enqueue((batch, chunkIndices));
                //     Interlocked.Decrement(ref activeTasks);
                // });

                uint[] ptIndices = new uint[batch.Length];
                for (int p = 0; p < batch.Length; p++)
                    ptIndices[p] = (uint)GetChunk(batch[p]); 

                sortedBatches.Enqueue((batch, ptIndices));

                pointsToQueue -= batch.Length;
            }

            // Debug.Log("C8");

            await writeChunksTask;

            // Debug.Log("C9");
        }

        static string ToNodeID(int level, int gridSize, int x, int y, int z) {

		    string id = "r";

		    int currentGridSize = gridSize;
		    int lx = x;
		    int ly = y;
		    int lz = z;

		    for (int i = 0; i < level; i++) {

			    int index = 0;

			    if (lx >= currentGridSize / 2) {
				    index += 0b100;
				    lx -= currentGridSize / 2;
			    }

			    if (ly >= currentGridSize / 2) {
				    index += 0b010;
				    ly -= currentGridSize / 2;
			    }

			    if (lz >= currentGridSize / 2) {
				    index += 0b001;
				    lz -= currentGridSize / 2;
			    }

			    id += index;
			    currentGridSize /= 2;
		    }

		    return id;
	    }

        static void CountThread(object obj) {
            CountThreadParams p = (CountThreadParams)obj;

            int gridSize = (int)Mathf.Pow(p.counter.Length, 1/3f);
            uint[] localCounter;

            void FlushCounter() {
                lock (p.counterLock) {
                    uint localSum = 0;
                    uint sum = 0;

                    for (int i = 0; i < p.counter.Length; i++) {
                        p.counter[i] += localCounter[i];
                        localSum += localCounter[i];
                        sum += p.counter[i];
                    }

                    // Debug.Log($"Flushed {localSum}, p.counter now has size {sum}");

                    
                }
            }

            int GetChunk(Point pt) {
                Vector3 size = p.bbox.Size;

                float x_norm = (pt.pos.x - p.bbox.Min.x) / size.x;
                float y_norm = (pt.pos.y - p.bbox.Min.y) / size.y;
                float z_norm = (pt.pos.z - p.bbox.Min.z) / size.z;

                int x = (int)Mathf.Min(x_norm * gridSize, gridSize-1);
                int y = (int)Mathf.Min(y_norm * gridSize, gridSize-1);
                int z = (int)Mathf.Min(z_norm * gridSize, gridSize-1);

                return p.mortonIndices[x * gridSize * gridSize + y * gridSize + z];
            }
            
            while (true) {
                localCounter = new uint[p.counter.Length];
                for (int i = 0; i < p.flushRate; i++) {
                    Point[] batch;
                    while (!p.batches.TryDequeue(out batch)) {
                        lock (p.paramsLock) {
                            if (p.stopSignal) {
                                FlushCounter();
                                return;
                            }
                        }
                        Thread.Sleep(50);
                    }

                    for (int j = 0; j < batch.Length; j++)
                        localCounter[GetChunk(batch[j])]++;

                    p.countCallback((uint)batch.Length);
                }

                FlushCounter();
            }   
        }
    }

    class CountThreadParams {
        public ConcurrentQueue<Point[]> batches;
        public object counterLock;
        public object paramsLock;
        public uint[] counter;
        public int[] mortonIndices;
        public AABB bbox;
        public int flushRate;
        public bool stopSignal;
        public Action<uint> countCallback;
        public CountThreadParams(ConcurrentQueue<Point[]> batches, AABB bbox, int[] mortonIndices, object counterLock, uint[] counter, Action<uint> countCallback, int flushRate = 1) {
            this.batches = batches;
            this.mortonIndices = mortonIndices;
            this.bbox = bbox;
            this.counterLock = counterLock;
            this.counter = counter;
            this.flushRate = flushRate;
            this.stopSignal = false;
            this.countCallback = countCallback;
            this.paramsLock = new object();
        }
    }
}