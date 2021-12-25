using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FastPoints {
    [ExecuteInEditMode]
    class PointCloudLoader : MonoBehaviour {

        #region public
        public PointCloudData data;
        public bool generateTree = true;
        public int decimatedCloudSize = 1000000;
        public float pointSize = 1.5f;
        public int treeLevels = 5;
        #endregion

        ComputeShader countShader;
        ComputeShader sortShader;
        int maxQueued;
        
        GameObject sphere;

        #region checkpoints
        public enum Phase { NONE, WAITING, READING, COUNTING, SORTING, WRITING, DONE }
        Phase currPhase;
        public Phase CurrPhase { get { return currPhase; } }

        #endregion

        // Parameters for reading
        int batchSize = 100000;
        ConcurrentQueue<Point[]> pointBatches;
        
        // Parameters for sorting
        int chunkCount = 0; // Number of chunks per axis, total chunks = chunkCount^3
        ComputeBuffer sortBuffer; // Buffer for point chunk indices
        ComputeBuffer countBuffer;  // Counts for each chunk
        uint[] nodeOffsets;
        uint pointsCounted = 0;
        uint pointsSorted = 0;
        ConcurrentQueue<(Point[], uint[])> sortedBatches;

        public void Start() {
            // Load compute shader
            countShader = (ComputeShader)Resources.Load("CountAndSort");
            sortShader = countShader;
            Mathf.Pow(Mathf.Pow(2, treeLevels-1), 3);
            maxQueued  = (int)((32 * Mathf.Pow(1024, 2)) / (batchSize * System.Runtime.InteropServices.Marshal.SizeOf<Point>()));
        }

        public void Update() {
            if (data == null) {
                currPhase = Phase.NONE;
                pointsCounted = 0;
                pointsSorted = 0;
                return;
            }

            // Start loading if data not null and not loading already
            if (currPhase == Phase.NONE) {
                Initialize();
                
                // Start loading point cloud
                if (!data.Init)
                    data.Initialize();

                if (!data.DecimatedGenerated)
                    data.PopulateSparseCloud(decimatedCloudSize);

                if (!data.TreeGenerated && generateTree) {
                    data.LoadPointBatches(batchSize, pointBatches, true); // Async
                    currPhase = Phase.READING;
                }
                else
                    currPhase = Phase.DONE; // If tree already generated or not generating tree, mark as done

                return;
            }

            // Reading pass - wait until bounds populated
            if (currPhase == Phase.READING) {
                bool boundsPopulated = data.MinPoint.x < data.MaxPoint.x && data.MinPoint.y < data.MaxPoint.y && data.MinPoint.z < data.MaxPoint.z;

                if (boundsPopulated) { // If bounds populated for first time, send to shaders and 
                    countShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z });
                    countShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z });
                    sortShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z });
                    sortShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z });
                    currPhase = Phase.COUNTING;
                }
            }

            // Counting pass
            if (currPhase == Phase.COUNTING) {
                Point[] batch;
                if (!pointBatches.TryDequeue(out batch))
                    return;

                ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                batchBuffer.SetData(batch);

                int countHandle = countShader.FindKernel("CountPoints");
                countShader.SetBuffer(countHandle, "_Points", batchBuffer);
                countShader.SetInt("_BatchSize", batch.Length);
                countShader.Dispatch(countHandle, 64, 1, 1);

                pointsCounted += (uint)batch.Length;

                Debug.Log($"Counted total of {pointsCounted} points");

                batchBuffer.Release();

                if (pointsCounted == data.PointCount) { // If all points counted, write counts to array and start reading again
                    currPhase = Phase.WAITING;
                    data.LoadPointBatches(batchSize, pointBatches, false);  // pointBatches now empty, but don't need to repopulate bounds
                    uint[] chunkCounts = new uint[chunkCount * chunkCount * chunkCount];
                    countBuffer.GetData(chunkCounts);
                    ComputeChunkOffsets(chunkCounts);
                }
            }

            if (currPhase == Phase.SORTING) {
                if (sortedBatches.Count >= maxQueued)   // Wait until space in sorted queue
                    return;

                Point[] batch;
                if (!pointBatches.TryDequeue(out batch))
                    return;
                
                ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                batchBuffer.SetData(batch);
                
                ComputeBuffer sortedBuffer = new ComputeBuffer(batchSize, sizeof(uint));

                int sortHandle = sortShader.FindKernel("SortPoints");
                sortShader.SetBuffer(sortHandle, "_Points", batchBuffer);
                sortShader.SetBuffer(sortHandle, "_ChunkIndices", sortedBuffer);
                sortShader.SetInt("_BatchSize", batch.Length);
                sortShader.Dispatch(sortHandle, 64, 1, 1);

                batchBuffer.Dispose();

                uint[] sortedIndices = new uint[batch.Length];
                sortBuffer.GetData(sortedIndices);
                pointsSorted += (uint)batch.Length;
                UnityEngine.Debug.Log("Sorted total of " + pointsSorted + " points");

                sortedBatches.Enqueue((batch, sortedIndices));

                batchBuffer.Release();
                sortedBuffer.Release();

                if (pointsSorted == data.PointCount) {
                    currPhase = Phase.WRITING;
                    UnityEngine.Debug.Log("Done sorting");
                }
            }

            if (currPhase == Phase.DONE) {
                // TODO: Write to pointclouddata
            } else
                EditorApplication.QueuePlayerLoopUpdate();  // Needed to run update without scene changes
        }

        public void Initialize() {
            // Initialize batch queue
            pointBatches = new ConcurrentQueue<Point[]>();            

            // Number of points each compute shader thread should process
            // 64 thread groups * 64 threads per group = 4096 threads
            int threadBudget = Mathf.CeilToInt(batchSize / 4096f);

            // Initialize count shader
            chunkCount = (int)Mathf.Pow(2, treeLevels);
            countBuffer = new ComputeBuffer(chunkCount * chunkCount * chunkCount, sizeof(UInt32));

            int countHandle = countShader.FindKernel("CountPoints"); // Initialize count shader
            countShader.SetBuffer(countHandle, "_Counts", countBuffer);
            countShader.SetInt("_ChunkCount", chunkCount);
            countShader.SetInt("_ThreadBudget", threadBudget);

            // Initialize sort shader
            int sortHandle = sortShader.FindKernel("SortPoints"); // Initialize count shader
            sortShader.SetInt("_ChunkCount", chunkCount);
            sortShader.SetInt("_ThreadBudget", threadBudget);
        }

        public void OnRenderObject() {
            if (data == null)
                return;

            if (data.TreeGenerated) {
                throw new NotImplementedException();
            } else if (data.DecimatedGenerated) {

                ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                cb.SetData(data.decimatedCloud);

                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", cb);
                mat.SetFloat("_PointSize", pointSize);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);

                cb.Dispose();
            }
        }


        async void ComputeChunkOffsets(uint[] chunkCounts) {
            await Task.Run(() => {

                nodeOffsets = new uint[chunkCount * chunkCount * chunkCount];
                nodeOffsets[0] = 0;
                for (int i = 1; i < chunkCount * chunkCount * chunkCount; i++)
                    nodeOffsets[i] = nodeOffsets[i-1] + chunkCounts[i-1];

            });
            
            Debug.Log("Starting sorting...");
            currPhase = Phase.SORTING;
        }
        
        async void WritePoints() {
            await Task.Run(() => {
                OctreeIO.CreateLeafFile((uint)data.PointCount, "");
                int[] nodeIndices = new int[chunkCount * chunkCount * chunkCount];

                while (currPhase == Phase.SORTING || sortedBatches.Count > 0) {
                    (Point[], uint[]) batch;
                    while (!sortedBatches.TryDequeue(out batch)) {}
                    OctreeIO.WriteLeafNodes(batch.Item1, batch.Item2, nodeOffsets, nodeIndices, "");
                }
            });

            currPhase = Phase.DONE;
        }

        // Populates merge count array -- for each chunk merged, gets assigned index of lowest (x,y,z) coordinate of merged chunk
        /* async void MergeSparseChunks(uint[] counts) {
            int sparseCutoff = 100;

            Func<(int, int, int), uint> getCount = tup => counts[tup.Item1 * chunkCount * chunkCount + tup.Item2 * chunkCount + tup.Item3];
            Func<int, int, int, int, int, int, uint> countChunk = delegate(int x1, int x2, int y1, int y2, int z1, int z2) {
                uint sum = 0;
                for (int i = x1; i < x2; i++)
                    for (int j = y1; j < y2; j++)
                        for (int k = z1; k < z2; k++)
                            sum += getCount((i,j,k));
                return sum;
            };

            Action<int, int, int, int, int, int, uint> markChunk = delegate(int x1, int x2, int y1, int y2, int z1, int z2, uint mergeVal) {
                for (int i = x1; i < x2; i++)
                    for (int j = y1; j < y2; j++)
                        for (int k = z1; k < z2; k++) {
                            int chunkIdx = i * chunkCount * chunkCount + j * chunkCount + k;
                            mergeCounts[chunkIdx] = mergeVal;
                            if (mergeIndices.Contains(chunkIdx))
                                mergeIndices.Remove(chunkIdx);
                        }
                mergeIndices.Add((int)mergeVal);
            };
            

            await Task.Run(() => {
                for (uint i = 0; i < counts.Length; i++) {
                    mergeCounts[i] = i;
                    mergeIndices.Add((int)i);
                }

                for (int size = 2; size < chunkCount; size *= 2) {
                    for (int i = 1; i <= chunkCount / size; i++) {
                        for (int j = 1; j <= chunkCount / size; j++) {
                            for (int k = 1; k <= chunkCount / size; k++) {
                                if (countChunk((i-1)*size,i*size,(j-1)*size,j*size,(k-1)*size,k*size) <= sparseCutoff) {
                                    markChunk((i-1)*size,i*size,(j-1)*size,j*size,(k-1)*size,k*size, (uint)((i-1)*size));
                                }
                            }
                        }
                    }
                }
            });

            writing = true;
        } */
    }
}