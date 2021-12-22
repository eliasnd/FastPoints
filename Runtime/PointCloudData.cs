using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace FastPoints {
    public class PointCloudData : ScriptableObject {
        
        public PointCloudHandle handle;
        
        public Point[] decimatedCloud;

        [SerializeField]
        int count;
        public int PointCount { get { return count; } }
        [SerializeField]
        Vector3 minPoint;
        public Vector3 MinPoint { get { return minPoint; } }
        [SerializeField]
        Vector3 maxPoint;
        public Vector3 MaxPoint { get { return maxPoint; } }

        [SerializeField]
        bool init = false;
        public bool Init { get { return init; } }

        [SerializeField]
        bool decimatedGenerated = false;
        public bool DecimatedGenerated { get { return decimatedGenerated; } }

        [SerializeField]
        bool treeGenerated; // True if tree generation is complete; if not, Renderer uses decimated point cloud
        public bool TreeGenerated { get { return treeGenerated; } }

        public void Initialize() {
            BaseStream stream = handle.GetStream();
            count = stream.PointCount;

            // PLY no header needs lazy init of bounds
            minPoint = stream.MinPoint;
            maxPoint = stream.MaxPoint;
            init = true;
        }

        public async Task PopulateSparseCloud(int size = 250000) {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            decimatedCloud = new Point[size];
            
            await Task.Run(() => {
                handle.GetStream().SamplePoints(decimatedCloud.Length, decimatedCloud);
            });

            decimatedGenerated = true;
            watch.Stop();
            UnityEngine.Debug.Log($"Decimated cloud loaded in {watch.ElapsedMilliseconds} ms");
        }

        public async Task LoadPointBatches(int batchSize, ConcurrentQueue<Point[]> batches) {
            BaseStream stream = handle.GetStream();

            await Task.Run(() => {
                for (int i = 0; i < count / batchSize; i++) {
                    Point[] batch = new Point[batchSize];
                    stream.ReadPoints(batchSize, batch);
                    batches.Enqueue(batch);                    
                    UnityEngine.Debug.Log("Queued " + batchSize + " points");
                }

                Point[] lastBatch = new Point[count % batchSize];
                stream.ReadPoints(count % batchSize, lastBatch);
                batches.Enqueue(lastBatch);
                UnityEngine.Debug.Log("Queued " + (count % batchSize) + " points");
            } );

            minPoint = stream.MinPoint;
            maxPoint = stream.MaxPoint;
        }

        public async Task GenerateTree(ComputeShader countShader, ComputeShader sortShader) {
            Mutex mut = new Mutex();
            Vector3 minPoint = new Vector3(1, 1, 1);
            Vector3 maxPoint = new Vector3(0, 0, 0);

            int pointBatchSize = 10000;
            ConcurrentQueue<Point[]> pointBatches = new ConcurrentQueue<Point[]>();
            Queue<Point[]> countedBatches = new Queue<Point[]>();

            bool allPointsRead = false;

            // Needs to be done on main thread for some reason
            int chunkCount = 128; // Number of chunks on each dimension, total number = chunkCount^3
            Texture3D counts = new Texture3D(chunkCount, chunkCount, chunkCount, TextureFormat.R16, false); // Counts for each chunk

            int countHandle = countShader.FindKernel("CountPoints"); // Initialize count shader
            countShader.SetTexture(countHandle, "_Counts", counts);
            countShader.SetInt("_ChunkCount", chunkCount);
            countShader.SetInt("_PointCount", count);
            countShader.SetInt("_ThreadBudget", 1);

            Thread t1 = new Thread(() => {
                BaseStream stream = handle.GetStream();

                for (int i = 0; i < count / pointBatchSize; i++) {
                    Point[] batch = new Point[pointBatchSize];
                    stream.ReadPoints(pointBatchSize, batch);
                    pointBatches.Enqueue(batch);                    
                }

                Point[] lastBatch = new Point[count % pointBatchSize];
                stream.ReadPoints(count % pointBatchSize, lastBatch);
                pointBatches.Enqueue(lastBatch);

                mut.WaitOne();
                minPoint = stream.MinPoint;
                maxPoint = stream.MaxPoint;
                mut.ReleaseMutex();

                allPointsRead = true;
            });

            Thread t2 = new Thread(() => {
                Stopwatch watch = new Stopwatch();
                watch.Start();


                // Initialize count shader

                // Initialize sort shader
                mut.WaitOne();

                bool boundsPopulated = minPoint.x > maxPoint.x || minPoint.y > maxPoint.y || minPoint.z > maxPoint.z;

                mut.ReleaseMutex();

                while (!boundsPopulated) { // Sleep until min and max points populated for ply
                    Thread.Sleep(300);
                    mut.WaitOne();
                    boundsPopulated = minPoint.x > maxPoint.x || minPoint.y > maxPoint.y || minPoint.z > maxPoint.z;
                    mut.ReleaseMutex();
                }

                countShader.SetFloats("_MinPoint", new float[] { minPoint.x, minPoint.y, minPoint.z });
                countShader.SetFloats("_MaxPoint", new float[] { maxPoint.x, maxPoint.y, maxPoint.z });

                while (!allPointsRead || pointBatches.Count > 0) {
                    Point[] batch;
                    while (!pointBatches.TryDequeue(out batch)) {} // Dequeue point batch
                    // TODO: Send to GPU
                    ComputeBuffer batchBuffer = new ComputeBuffer(pointBatchSize, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                    batchBuffer.SetData(batch);

                    countShader.SetBuffer(countHandle, "_Points", batchBuffer);
                    countShader.Dispatch(countHandle, 8, 8, 8); // Should have a total of 8^3 * 8^2 = 32768 threads

                    UnityEngine.Debug.Log("Counting done! Took " + watch.ElapsedMilliseconds + " ms.");

                    countedBatches.Enqueue(batch);
                }

                while (countedBatches.Count > 0) {
                    Point[] batch = countedBatches.Dequeue();
                }
            });

            t1.Start();
            t2.Start();
        }
    }
}