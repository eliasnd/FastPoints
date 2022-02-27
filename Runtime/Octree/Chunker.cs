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
            Debug.Log("C1");
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

            await dispatcher.EnqueueAsync(() => {
                computeShader = (ComputeShader)Resources.Load("CountAndSort");

                chunkGridBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(uint));

                computeShader.SetFloats("_MinPoint", minPoint);
                computeShader.SetFloats("_MaxPoint", maxPoint);

                ComputeBuffer mortonBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(int));
                mortonBuffer.SetData(mortonIndices);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_MortonIndices", mortonBuffer);
                computeShader.SetBuffer(computeShader.FindKernel("SortPoints"), "_MortonIndices", mortonBuffer);

                computeShader.SetInt("_NodeCount", chunkGridSize);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);
                computeShader.SetInt("_ThreadBudget", threadBudget);
            });

            Debug.Log("C3");

            // COUNTING

            int pointsCounted = 0;
            int pointsQueued = 0;

            long activeTasks = 0;

            // DEBUG_CODE
            uint[] testCounts = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            AABB[] bbs = new AABB(data.MinPoint, data.MaxPoint).Subdivide(chunkGridSize);

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

            while (pointsQueued < data.PointCount) {
                Point[] batch;
                while (Interlocked.Read(ref activeTasks) > 100 || !readQueue.TryDequeue(out batch))
                    Thread.Sleep(5);

                // DEBUG_CODE
                // bool testPassed = true;
                // if (pointsQueued == 0) {
                //     Debug.Log($"BBox test. Data min point: {data.MinPoint}, max point: {data.MaxPoint}");
                //     (float, float)[] xRanges = bbs.Select((bb) => (bb.Min.x, bb.Max.x)).ToArray();
                //     (float, float)[] yRanges = bbs.Select((bb) => (bb.Min.y, bb.Max.y)).ToArray();;
                //     (float, float)[] zRanges = bbs.Select((bb) => (bb.Min.z, bb.Max.z)).ToArray();;
                //     Debug.Log($"Got inner x ranges {xRanges.ToString()}");
                //     Debug.Log($"Got inner y ranges {yRanges.ToString()}");
                //     Debug.Log($"Got inner z ranges {zRanges.ToString()}");
                // }
                // if (testPassed) { // Test bboxes until one breaks
                //     // BBOX TEST
                //     int GetChunk(Point p) {
                //         float threshold = 1f / chunkGridSize; // Used to calculate which index for each dimension

                //         int x = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(data.MinPoint.x, data.MaxPoint.x, p.pos.x) / threshold), chunkGridSize-1);
                //         int y = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(data.MinPoint.y, data.MaxPoint.y, p.pos.y) / threshold), chunkGridSize-1);
                //         int z = Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(data.MinPoint.z, data.MaxPoint.z, p.pos.z) / threshold), chunkGridSize-1);
                //         return mortonIndices[x * chunkGridSize * chunkGridSize + y * chunkGridSize + z];
                //     }

                //     foreach (Point p in batch) {
                //         int chunk = GetChunk(p);
                //         testCounts[chunk]++;
                //         if (Vector3.Max(bbs[chunk].Max, p.pos) != bbs[chunk].Max || Vector3.Min(bbs[chunk].Min, p.pos) != bbs[chunk].Min) {
                //             Debug.LogError($@"Found that point {p.pos.ToString()} is in bbox {bbs[chunk].ToString()}.
                //                 Got x coefficient of {Mathf.InverseLerp(data.MinPoint.x, data.MaxPoint.x, p.pos.x)}, x index of {Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(data.MinPoint.x, data.MaxPoint.x, p.pos.x) / (1f/chunkGridSize)), chunkGridSize-1)}
                //                 Got y coefficient of {Mathf.InverseLerp(data.MinPoint.y, data.MaxPoint.y, p.pos.y)}, y index of {Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(data.MinPoint.y, data.MaxPoint.y, p.pos.y) / (1f/chunkGridSize)), chunkGridSize-1)}
                //                 Got z coefficient of {Mathf.InverseLerp(data.MinPoint.z, data.MaxPoint.z, p.pos.z)}, z index of {Mathf.Min(Mathf.FloorToInt(Mathf.InverseLerp(data.MinPoint.z, data.MaxPoint.z, p.pos.z) / (1f/chunkGridSize)), chunkGridSize-1)}
                //             ");
                //             testPassed = false;
                //         }
                //     }

                //     Debug.Log(testPassed ? "Test passed" : "Test failed");
                // }

                foreach (Point pt in batch) {
                    int c = GetChunk(pt);
                    if (!bbs[c].InAABB(pt.pos)) {
                        Debug.LogError("GetChunk issue!");
                        GetChunk(pt);
                    }
                    testCounts[c]++;
                }


                Interlocked.Increment(ref activeTasks);
                dispatcher.Enqueue(() => {
                    ComputeBuffer batchBuffer = new ComputeBuffer(batch.Length, Point.size);
                    batchBuffer.SetData(batch);

                    computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);

                    int countHandle = computeShader.FindKernel("CountPoints");
                    computeShader.SetBuffer(countHandle, "_Points", batchBuffer);
                    computeShader.SetInt("_BatchSize", batch.Length);
                    computeShader.Dispatch(countHandle, 64, 1, 1);

                    batchBuffer.Release();
                    pointsCounted += batch.Length;
                    Interlocked.Decrement(ref activeTasks);
                });

                pointsQueued += batch.Length;
                Debug.Log($"{pointsQueued} points queued");
            }

            readQueue.Clear();
            _ = data.LoadPointBatches(batchSize, readQueue);   // Start loading to get head start while merging

            while (Interlocked.Read(ref activeTasks) > 0)
                Thread.Sleep(10);

            uint[] leafCounts = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            await dispatcher.EnqueueAsync(() => { chunkGridBuffer.GetData(leafCounts); chunkGridBuffer.Release(); });

            // DEBUG_CODE
            for (int i = 0; i < chunkGridSize * chunkGridSize * chunkGridSize; i++)
                if (leafCounts[i] != testCounts[i])
                    Debug.LogError($"Chunk {i} with bbox {bbs[i]} had cpu count {testCounts[i]}, gpu count {leafCounts[i]}");

            // Debug.Log("Test done");

            Debug.Log("C4");

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

            Debug.Log("C5");

            // MAKE LUT

            List<string> chunkPaths = new();
            List<AABB> chunkBBox = new();   // DEBUG
            int[] lut = new int[chunkGridSize * chunkGridSize * chunkGridSize];

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

            Debug.Log("C6");

            Debug.Log("Chunks are:");
            for (int i = 0; i < chunkPaths.Count; i++)
                Debug.Log($"Chunk {i} at path {chunkPaths[i]} with bbox {chunkBBox[i].ToString()}");

            // throw new Exception();

            // START WRITING CHUNKS

            ConcurrentQueue<(Point[], uint[])> sortedBatches = new ConcurrentQueue<(Point[], uint[])>();
            int maxSorted = 100;

            Directory.CreateDirectory($"{targetDir}");
            Directory.CreateDirectory($"{targetDir}/chunks");

            ThreadedWriter chunkWriter = new ThreadedWriter(12);

            Task writeChunksTask = Task.Run(() => {
                int chunkCount = chunkPaths.Count;

                List<Point>[] sorted = new List<Point>[chunkCount];
                for (int i = 0; i < chunkCount; i++)
                    sorted[i] = new();

                int pointsToWrite = data.PointCount;

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

                        foreach (Point p in sorted[i])
                            if (!chunkBBox[i].InAABB(p.pos))
                                Debug.LogError($"Chunking found points outside bbox for {chunkPaths[i]}");

                        // chunkStreams[i].WriteAsync(Point.ToBytes(sorted[i].ToArray()));
                        chunkWriter.Write(chunkPaths[i], Point.ToBytes(sorted[i].ToArray()));
                        sorted[i].Clear();
                    }

                    pointsToWrite -= tup.batch.Length;
                }
            });

            Debug.Log("C7");

            // SORT POINTS

            int pointsToQueue = data.PointCount;
            while (pointsToQueue > 0) {
                while (sortedBatches.Count > maxSorted) {}

                Point[] batch;
                while (Interlocked.Read(ref activeTasks) > 100 || !readQueue.TryDequeue(out batch))
                // while (!readQueue.TryDequeue(out batch))
                    Thread.Sleep(5);
                    // Debug.Log("Waiting on batch from disk");  

                uint[] chunkIndices = new uint[batch.Length];             

                Interlocked.Increment(ref activeTasks);
                dispatcher.Enqueue(() => {
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
                    Interlocked.Decrement(ref activeTasks);
                });

                // uint[] ptIndices = new uint[batch.Length];
                // for (int p = 0; p < batch.Length; p++)
                //     ptIndices[p] = (uint)GetChunk(batch[p]); 

                // sortedBatches.Enqueue((batch, ptIndices));

                pointsToQueue -= batch.Length;
            }

            Debug.Log("C8");

            await writeChunksTask;

            Debug.Log("C9");
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
    }
}