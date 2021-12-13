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
        RenderTexture counts; // Counts for chunking
        bool boundsPopulated = false;
        int pointsCounted;
        bool writing; // Whether data has started writing to file
        float progress = 0.0f;
        Queue<Point[]> countedBatches;

        public void Update() {
            if (data == null) {
                loading = false;
                reading = false;
                writing = false;
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
                counts = new RenderTexture(chunkCount, chunkCount, 0, RenderTextureFormat.R16);
                counts.enableRandomWrite = true;
                counts.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                counts.volumeDepth = chunkCount;
                counts.Create();

                int countHandle = countShader.FindKernel("CountPoints"); // Initialize count shader
                countShader.SetTexture(countHandle, "_Counts", counts);
                countShader.SetInt("_ChunkCount", chunkCount);
                countShader.SetInt("_PointCount", data.PointCount);
                countShader.SetInt("_ThreadBudget", 1);

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
                countShader.DispatchIndirect(countHandle, 8, 8, 8);

                batchBuffer.Dispose();

                countedBatches.Enqueue(batch);
                pointsCounted += batch.Length;
                UnityEngine.Debug.Log("Counted total of " + pointsCounted + " points");

                if (pointsCounted == data.PointCount)
                    Debug.Log("Done counting");
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