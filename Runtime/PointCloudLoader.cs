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

        ComputeShader computeShader;
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
        int leafCountAxis { get { return (int)Mathf.Pow(2, treeLevels); } }
        int leafCountTotal { get { return leafCountAxis * leafCountAxis * leafCountAxis; } }
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
            computeShader = (ComputeShader)Resources.Load("CountAndSort");
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
                    computeShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x-1E-5f, data.MinPoint.y-1E-5f, data.MinPoint.z-1E-5f });
                    computeShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x+1E-5f, data.MaxPoint.y+1E-5f, data.MaxPoint.z+1E-5f });
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

                int countHandle = computeShader.FindKernel("CountPoints");
                computeShader.SetBuffer(countHandle, "_Points", batchBuffer);
                computeShader.SetInt("_BatchSize", batch.Length);
                computeShader.Dispatch(countHandle, 64, 1, 1);

                pointsCounted += (uint)batch.Length;

                // Debug.Log($"Counted total of {pointsCounted} points");

                batchBuffer.Release();

                if (pointsCounted == data.PointCount) { // If all points counted, write counts to array and start reading again
                    currPhase = Phase.WAITING;
                    data.LoadPointBatches(batchSize, pointBatches, false);  // pointBatches now empty, but don't need to repopulate bounds
                    uint[] leafNodeCounts = new uint[leafCountTotal];
                    countBuffer.GetData(leafNodeCounts);
                    TimeSpan currTime = watch.Elapsed;
                    Debug.Log($"[{currTime.ToString()}]: Counting done");
                    WritePoints(leafNodeCounts);
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

                int sortHandle = computeShader.FindKernel("SortPoints");
                computeShader.SetBuffer(sortHandle, "_Points", batchBuffer);
                computeShader.SetBuffer(sortHandle, "_ChunkIndices", sortedBuffer);
                computeShader.SetInt("_BatchSize", batch.Length);
                computeShader.Dispatch(sortHandle, 64, 1, 1);

                batchBuffer.Dispose();

                uint[] sortedIndices = new uint[batch.Length];
                sortedBuffer.GetData(sortedIndices);
                pointsSorted += (uint)batch.Length;

                Debug.Log($"Testing sort shader: on point {batch[0].pos.x}, {batch[0].pos.y}, {batch[0].pos.z}, index was {sortedIndices[0]}");
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
            countBuffer = new ComputeBuffer(leafCountTotal, sizeof(UInt32));

            int countHandle = computeShader.FindKernel("CountPoints"); // Initialize count shader
            computeShader.SetBuffer(countHandle, "_Counts", countBuffer);

            computeShader.SetInt("_NodeCount", leafCountAxis);
            computeShader.SetInt("_ThreadBudget", threadBudget);

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
        
        async void WritePoints(uint[] leafNodeCounts) {
            await Task.Run(() => {
                nodeOffsets = new uint[leafCountTotal];
                nodeOffsets[0] = 0;
                for (int i = 1; i < leafCountTotal; i++)
                    nodeOffsets[i] = nodeOffsets[i-1] + leafNodeCounts[i-1];

                currPhase = Phase.SORTING;

                List<Task> tasks = new List<Task>();
                int maxTasks = 100;

                OctreeIO.CreateLeafFile((uint)data.PointCount, "Assets");
                int[] nodeIndices = new int[leafCountTotal];

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
    }
}