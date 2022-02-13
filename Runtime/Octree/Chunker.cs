using System.IO;
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
        static ComputeShader computeShader = (ComputeShader)Resources.Load("CountAndSort");

        public static async Task MakeChunks(PointCloudData data, string targetDir, Dispatcher dispatcher) {
            ConcurrentQueue<Point[]> readQueue = new ConcurrentQueue<Point[]>();

            _ = data.LoadPointBatches(batchSize, readQueue);

            if (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z) 
                await data.PopulateBounds();

            float[] minPoint = new float[] { data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f };
            float[] maxPoint = new float[] { data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f };

            int chunkDepth = 5;
            int chunkGridSize = (int)Mathf.Pow(2, chunkDepth);
            ComputeBuffer chunkGridBuffer = null;  

            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);

            // INIT COMPUTE SHADER

            await dispatcher.EnqueueAsync(() => {
                computeShader = (ComputeShader)Resources.Load("CountAndSort");

                chunkGridBuffer = new ComputeBuffer(chunkGridSize * chunkGridSize * chunkGridSize, sizeof(uint));

                computeShader.SetFloats("_MinPoint", minPoint);
                computeShader.SetFloats("_MaxPoint", maxPoint);

                computeShader.SetInt("_NodeCount", chunkGridSize);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_Counts", chunkGridBuffer);
                computeShader.SetInt("_ThreadBudget", threadBudget);
            });

            // COUNTING

            int pointsCounted = 0;
            int pointsQueued = 0;

            long activeTasks = 0;

            while (pointsQueued < data.PointCount) {
                Point[] batch;
                while (Interlocked.Read(ref activeTasks) > 100 || !readQueue.TryDequeue(out batch))
                    Thread.Sleep(5);

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
            }

            readQueue.Clear();
            _ = data.LoadPointBatches(batchSize, readQueue);   // Start loading to get head start while merging

            while (Interlocked.Read(ref activeTasks) > 0)
                Thread.Sleep(10);

            uint[] leafCounts = new uint[chunkGridSize * chunkGridSize * chunkGridSize];
            await dispatcher.EnqueueAsync(() => { chunkGridBuffer.GetData(leafCounts); chunkGridBuffer.Release(); });

            // MAKE SUM PYRAMID

            uint[][] sumPyramid = new uint[chunkDepth][];
            sumPyramid[chunkDepth-1] = leafCounts;

            for (int level = chunkDepth-1; level > 0; level--) {
                int levelDim = (int)Mathf.Pow(2, level);
                uint[] currLevel = new uint[levelDim * levelDim * levelDim];

                uint[] lastLevel = sumPyramid[level+1];

                for (uint x = 0; x < levelDim; x++)
                    for (uint y = 0; y < levelDim; y++)
                        for (uint z = 0; z < levelDim; z++)
                        {
                            int chunkIdx = (int)Utils.MortonEncode(x, y, z);
                            // Debug.Log($"Test: Morton code for {x}, {y}, {z} = {chunkIdx}");
                            int childIdx = (int)Utils.MortonEncode(2*x, 2*y, 2*z);
                            currLevel[chunkIdx] =
                                lastLevel[childIdx] + lastLevel[childIdx+1] + lastLevel[childIdx+2] + lastLevel[childIdx+3] +
                                lastLevel[childIdx+4] + lastLevel[childIdx+5] + lastLevel[childIdx+6] + lastLevel[childIdx+7];
                        }
            }

            // MAKE LUT

            List<string> chunkPaths = new();
            int[] lut = new int[chunkGridSize * chunkGridSize * chunkGridSize];

            void AddToLUT(int level, uint x, uint y, uint z)
            {
                int idx = (int)Utils.MortonEncode(x, y, z);
                if (level == chunkDepth-1)  // If at bottom, automatically add
                {
                    lut[idx] = chunkPaths.Count;
                    chunkPaths.Add($"{targetDir}/chunks/{ToNodeID(level, (int)Mathf.Pow(2, level), (int)x, (int)y, (int)z)}.dat");
                }
                else if (sumPyramid[level][idx] < maxChunkSize) // Else, add if below max chunk size
                {
                    int chunkIdx = chunkPaths.Count;
                    chunkPaths.Add($"{targetDir}/chunks/{ToNodeID(level, (int)Mathf.Pow(2, level), (int)x, (int)y, (int)z)}.dat");
                    uint chunkSize = (uint)Mathf.Pow(2, chunkDepth-1-level);    // CHECK
                    int childIdx = (int)Utils.MortonEncode(chunkSize*x, chunkSize*y, chunkSize*z);
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

            // START WRITING CHUNKS

            ConcurrentQueue<(Point[], uint[])> sortedBatches = new ConcurrentQueue<(Point[], uint[])>();
            int maxSorted = 100;

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

                        // chunkStreams[i].WriteAsync(Point.ToBytes(sorted[i].ToArray()));
                        chunkWriter.Write(chunkPaths[i], Point.ToBytes(sorted[i].ToArray()));
                        sorted[i].Clear();
                    }

                    pointsToWrite -= tup.batch.Length;
                }
            });

            // SORT POINTS

            int pointsToQueue = data.PointCount;
            while (pointsToQueue > 0) {
                while (sortedBatches.Count > maxSorted) {}

                Point[] batch;
                while (!readQueue.TryDequeue(out batch)) {}
                    // Debug.Log("Waiting on batch from disk");  

                uint[] chunkIndices = new uint[batch.Length];             

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
                });

                pointsToQueue -= batch.Length;
            }

            await writeChunksTask;
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