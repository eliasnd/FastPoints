using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

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
        public string TreePath = ""; // True if tree generation is complete; if not, Renderer uses decimated point cloud
        public bool TreeGenerated { get { return TreePath != ""; } }


        public void Initialize() {
            Debug.Log("Initialize");
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
            
            await handle.GetStream().SamplePoints(decimatedCloud.Length, decimatedCloud);
            
            decimatedGenerated = true;
            watch.Stop();
            UnityEngine.Debug.Log($"Decimated cloud loaded in {watch.ElapsedMilliseconds} ms");
        }

        public async Task LoadPointBatches(int batchSize, ConcurrentQueue<Point[]> batches, bool populateBoundsAsync = false) {
            // Maximum allow queue of 32 MB
            int maxQueued = (32 * Utils.MB) / (batchSize * 15);
            BaseStream stream = handle.GetStream();

            Task t1 = Task.Run(() => {
                /* for (int i = 0; i < count / batchSize; i++) {
                    Point[] batch = new Point[batchSize];
                    stream.ReadPoints(batchSize, batch);
                    batches.Enqueue(batch);                    
                    while (batches.Count >= maxQueued) Thread.Sleep(75);   // Wait for queue to empty enough for new batch
                }

                Point[] lastBatch = new Point[count % batchSize];
                stream.ReadPoints(count % batchSize, lastBatch);
                batches.Enqueue(lastBatch); */
                stream.ReadPointsToQueue(batches, maxQueued, batchSize);

                /* if (!populateBoundsAsync && handle.Type == PointCloudHandle.FileType.PLY) {
                    minPoint = stream.MinPoint;
                    maxPoint = stream.MaxPoint;
                } */
            });

            // Async bounds
            Task t2 = Task.Run(() => {
                if (populateBoundsAsync && handle.Type == PointCloudHandle.FileType.PLY) {
                    PlyStream scanStream = (PlyStream)handle.GetStream();
                    ConcurrentQueue<Point[]> scanBatches = new ConcurrentQueue<Point[]>();
                    Task t = stream.ReadPointsToQueue(scanBatches, maxQueued, batchSize);

                    minPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    maxPoint = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                    int batchCount = 1;

                    for (int i = 0; i < count / batchSize + 1; i++) {
                        Point[] scanBatch;
                        while (!scanBatches.TryDequeue(out scanBatch)) {}

                        foreach (Point p in scanBatch) {
                            minPoint = Vector3.Min(minPoint, p.pos);
                            maxPoint = Vector3.Max(maxPoint, p.pos);
                        }
                        if ((i+1) % 500 == 0)
                            Debug.Log($"Scanning batch {i+1}/{(int)(count / batchSize)}");
                    }


                    /* scanStream.SetComputeBounds(true);

                    Debug.Log("Set compute bounds");

                    Point[] batch = new Point[batchSize];
                    for (int i = 0; i < count / batchSize; i++) {
                        scanStream.ReadPoints(batchSize, batch);
                        Debug.Log($"Scanning batch {i+1}/{(int)(count / batchSize)}");
                    }

                    batch = new Point[count % batchSize];
                    scanStream.ReadPoints(count % batchSize, batch);

                    Debug.Log($"Scanning batch {(int)(count / batchSize)}/{(int)(count / batchSize)}");

                    minPoint = scanStream.MinPoint;
                    maxPoint = scanStream.MaxPoint; */

                    Debug.Log($"Preliminarily populated bounds {minPoint.ToString()}, {maxPoint.ToString()}");
                }
            });

            await t2;
            await t1;
        }
    }
}