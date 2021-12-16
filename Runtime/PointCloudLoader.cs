using UnityEngine;
using UnityEditor;

using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FastPoints {
    [ExecuteInEditMode]
    class PointCloudLoader : MonoBehaviour {
        public PointCloudData data;
        public ComputeShader countShader;
        public ComputeShader sortShader;

        bool loading = false;
        bool reading; // Whether data has started reading from file

        // Parameters for reading
        int batchSize = 20000;
        ConcurrentQueue<Point[]> pointBatches;
        
        // Parameters for chunking
        int chunkCount = 128; // Number of chunks per axis, total chunks = chunkCount^3
        // RenderTexture counts; // Counts for chunking
        ComputeBuffer countBuffer; // Counts for chunking, flattened
        uint[] counts;
        bool boundsPopulated = false;
        uint pointsCounted = 0;
        bool writing; // Whether data has started writing to file
        float progress = 0.0f;
        Queue<Point[]> countedBatches;

        public void Update() {
            if (data == null) {
                loading = false;
                reading = false;
                writing = false;
                pointsCounted = 0;
                boundsPopulated = false;
                return;
            }

            if (countShader == null || sortShader == null)
                return;

            // Start loading if data not null and not loading already
            if (!loading) {
                loading = true;

                // Initialize batch queue
                pointBatches = new ConcurrentQueue<Point[]>();            
                countedBatches = new Queue<Point[]>();

                // Initialize counting shader
                // counts = new RenderTexture(chunkCount, chunkCount, 0, RenderTextureFormat.R16);
                // counts.enableRandomWrite = true;
                // counts.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                // counts.volumeDepth = chunkCount;
                // counts.Create();
                // Use compute buffer instead
                countBuffer = new ComputeBuffer(chunkCount * chunkCount * chunkCount, sizeof(UInt32));
                counts = new uint[chunkCount * chunkCount * chunkCount];

                int countHandle = countShader.FindKernel("CountPoints"); // Initialize count shader
                countShader.SetBuffer(countHandle, "_Counts", countBuffer);
                countShader.SetInt("_ChunkCount", chunkCount);
                countShader.SetInt("_ThreadBudget", Mathf.CeilToInt(batchSize / 4096f));

                // TODO: Initialize sorting shader
                
                // Start loading point cloud

                LoadPointCloud();

                return;
            }

            // Reading pass
            if (reading) {
                // If bounds not populated, wait
                if (!boundsPopulated) {
                    boundsPopulated = data.MinPoint.x < data.MaxPoint.x && data.MinPoint.y < data.MaxPoint.y && data.MinPoint.z < data.MaxPoint.z;
                    if (boundsPopulated) { // If bounds populated for first time, send to count shader
                        countShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z });
                        countShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z });
                    } else {
                        return;
                    }
                }

                // Debug.Log("Bounds: " + data.MinPoint.ToString() + ", " + data.MaxPoint.ToString());

                Point[] batch;
                if (!pointBatches.TryDequeue(out batch))
                    return;

                ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                batchBuffer.SetData(batch);

                int countHandle = countShader.FindKernel("CountPoints");
                countShader.SetBuffer(countHandle, "_Points", batchBuffer);
                countShader.SetInt("_BatchSize", batch.Length);
                countShader.Dispatch(countHandle, 64, 1, 1);

                batchBuffer.Dispose();

                countedBatches.Enqueue(batch);
                pointsCounted += (uint)batch.Length;
                UnityEngine.Debug.Log("Counted total of " + pointsCounted + " points");

                if (pointsCounted == 20000) {
                    countBuffer.GetData(counts);
                    Debug.Log("First Count: " + counts[0]);
                    Debug.Log("Second Count: " + counts[1]);
                }
                if (pointsCounted == data.PointCount) {
                    reading = false;
                    writing = true;
                    countBuffer.GetData(counts);
                    Debug.Log("Done counting");
                }
            }

            if (writing) {
                int sum = 0;
                for (int i = 0; i < chunkCount * chunkCount * chunkCount; i++)
                    sum += (int)counts[i];
                Debug.Log("Total points counted: " + sum);
                if (countedBatches.Count == 0) {
                    writing = false;
                    return;
                }

                Point[] batch = countedBatches.Dequeue();
            }
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
                mat.SetPass(0);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);

                Graphics.DrawProceduralNow(MeshTopology.Points, data.PointCount, 1);

                cb.Dispose();
            }
        }

        // Starts point cloud loading. Returns true if cloud already loaded, otherwise false
        bool LoadPointCloud() {

            if (data.Init && data.DecimatedGenerated && data.TreeGenerated)
                return true;

            if (!data.Init)
                data.Initialize();

            if (!data.DecimatedGenerated)
                data.PopulateSparseCloud();

            if (!data.TreeGenerated)
                GenerateTree();

            return false;
        }

        async void GenerateTree() {
            data.LoadPointBatches(batchSize, pointBatches);
            reading = true;
        }
        
    }
}