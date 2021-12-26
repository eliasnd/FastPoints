using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

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
        [SerializeField]
        Phase currPhase;
        public Phase CurrPhase { get { return currPhase; } }

        #endregion

        // Parameters for reading
        public int batchSize = 100000;
        ConcurrentQueue<Point[]> pointBatches;
        
        // Parameters for sorting
        int chunkCount = 0; // Number of chunks per axis, total chunks = chunkCount^3
        ComputeBuffer sortBuffer; // Buffer for point chunk indices
        ComputeBuffer countBuffer;  // Counts for each chunk
        uint[] nodeOffsets;
        [SerializeField]
        uint pointsCounted = 0;
        [SerializeField]
        uint pointsSorted = 0;
        [SerializeField]
        uint pointsWritten = 0;
        ConcurrentQueue<(Point[], uint[])> sortedBatches;

        bool BoundsPopulated { get { 
            return data.MinPoint.x < data.MaxPoint.x && data.MinPoint.y < data.MaxPoint.y && data.MinPoint.z < data.MaxPoint.z;
        } }

        Stopwatch watch;

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

                if (!data.DecimatedGenerated || data.decimatedCloud.Length != decimatedCloudSize)
                    data.PopulateSparseCloud(decimatedCloudSize);

                if (!data.TreeGenerated && generateTree) {
                    data.LoadPointBatches(batchSize, pointBatches, !BoundsPopulated); // Async
                    currPhase = Phase.READING;
                }
                else
                    currPhase = Phase.DONE; // If tree already generated or not generating tree, mark as done

                return;
            }

            // Reading pass - wait until bounds populated
            if (currPhase == Phase.READING) {
                if (BoundsPopulated) { // If bounds populated for first time, send to shaders and 
                    countShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z });
                    countShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z });
                    sortShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z });
                    sortShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z });
                    Debug.Log($"[{watch.Elapsed.ToString()}]: Reading done");
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

                // Debug.Log($"Counted total of {pointsCounted} points");

                batchBuffer.Release();

                if (pointsCounted == data.PointCount) { // If all points counted, write counts to array and start reading again
                    currPhase = Phase.WAITING;
                    data.LoadPointBatches(batchSize, pointBatches, false);  // pointBatches now empty, but don't need to repopulate bounds
                    uint[] chunkCounts = new uint[chunkCount * chunkCount * chunkCount];
                    countBuffer.GetData(chunkCounts);
                    TimeSpan currTime = watch.Elapsed;
                    Debug.Log($"[{currTime.ToString()}]: Counting done");
                    WritePoints(chunkCounts);
                    // ComputeChunkOffsets(chunkCounts);
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
                sortedBuffer.GetData(sortedIndices);
                pointsSorted += (uint)batch.Length;
                // UnityEngine.Debug.Log("Sorted total of " + pointsSorted + " points");

                sortedBatches.Enqueue((batch, sortedIndices));

                batchBuffer.Release();
                sortedBuffer.Release();

                if (pointsSorted == data.PointCount) {
                    Debug.Log($"[{watch.Elapsed.ToString()}]: Sorting done");
                    Debug.Log($"Currently {sortedBatches.Count} batches queued to write");
                    currPhase = Phase.WRITING;
                }
            }

            if (currPhase == Phase.DONE) {
                watch.Stop();
                Debug.Log($"[{watch.Elapsed.ToString()}]: Writing done");
                // TODO: Write to pointclouddata
            }
        }

        public void Initialize() {
            // Initialize batch queue
            pointBatches = new ConcurrentQueue<Point[]>();    
            sortedBatches = new ConcurrentQueue<(Point[], uint[])>();        

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

            watch = new Stopwatch();
            watch.Start();
            Debug.Log($"[{watch.Elapsed.ToString()}]: Starting");
        }

        public void OnDrawGizmos() {
        #if UNITY_EDITOR
            // Ensure continuous Update calls.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
        #endif
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
            
            Debug.Log($"[{watch.Elapsed.ToString()}]: Chunk offsets done");
            currPhase = Phase.SORTING;
            // WritePoints();  // Starts writing point
        }
        
        async void WritePoints(uint[] chunkCounts) {
            await Task.Run(() => {
                nodeOffsets = new uint[chunkCount * chunkCount * chunkCount];
                nodeOffsets[0] = 0;
                for (int i = 1; i < chunkCount * chunkCount * chunkCount; i++)
                    nodeOffsets[i] = nodeOffsets[i-1] + chunkCounts[i-1];

                currPhase = Phase.SORTING;

                List<Task> tasks = new List<Task>();
                int maxTasks = 100;

                OctreeIO.CreateLeafFile((uint)data.PointCount, "Assets");
                int[] nodeIndices = new int[chunkCount * chunkCount * chunkCount];

                int[] countWrapper = new int[] { (int)pointsWritten };
                string output = "";

                while (currPhase == Phase.SORTING || sortedBatches.Count > 0) {
                    if (tasks.Count == maxTasks) {
                        int t = Task.WaitAny(tasks.ToArray());
                        tasks.RemoveAt(t);
                    } 

                    (Point[], uint[]) batch;
                    while (!sortedBatches.TryDequeue(out batch)) {}
                    tasks.Add(
                        OctreeIO.WriteLeafNodes(batch.Item1, batch.Item2, nodeOffsets, nodeIndices, "Assets", countWrapper)
                        .ContinueWith(t =>
                            {
                                var ex = t.Exception?.GetBaseException();
                                if (ex != null)
                                {
                                    Debug.LogError($"Task faulted and stopped running. ErrorType={ex.GetType()} ErrorMessage={ex.Message}");
                                }
                            },
                            TaskContinuationOptions.OnlyOnFaulted
                        )
                    );
                    pointsWritten = (uint)countWrapper[0];

                    Debug.Log("Looping");
                    if (currPhase != Phase.SORTING)
                        Debug.Log($"{sortedBatches.Count} batches left to count...");
                }

                // Debug.Log($"Here {tasks.Count}");

                while (tasks.Count > 0) {
                    Debug.Log("Still going...");
                    int t = Task.WaitAny(tasks.ToArray());
                    tasks.RemoveAt(t);
                    pointsWritten = (uint)countWrapper[0];

                    if (output != "") {
                        Debug.Log(output);
                        output = "";
                    }
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