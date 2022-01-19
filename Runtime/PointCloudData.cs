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

        public async Task LoadPointBatches(int batchSize, ConcurrentQueue<Point[]> batches) {
            // Maximum allow queue of 32 MB
            int maxQueued = (32 * Utils.MB) / (batchSize * 15);
            BaseStream stream = handle.GetStream();

            await Task.Run(() => {
                stream.ReadPointsToQueue(batches, maxQueued, batchSize);
            });
        }

        public async Task PopulateBounds() {
            await Task.Run(() => {
                ConcurrentQueue<Point[]> batches = new ConcurrentQueue<Point[]>();

                BaseStream stream = handle.GetStream();
                stream.ReadPointsToQueue(batches, 10, 100000);

                minPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                maxPoint = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                int pointsScanned = 0;

                while (pointsScanned < count) {
                    Point[] batch;
                    while (!batches.TryDequeue(out batch)) {}

                    Vector3 minBatch = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 maxBatch = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                    foreach (Point p in batch) {
                        minPoint = Vector3.Min(minPoint, p.pos);
                        maxPoint = Vector3.Max(maxPoint, p.pos);
                    }

                    pointsScanned += batch.Length;

                    // Debug.Log($"Min point: {minBatch.ToString()}, max point: {maxBatch.ToString()}");
                }

                Debug.Log($"Preliminarily populated bounds {minPoint.ToString()}, {maxPoint.ToString()}");
            });
        }
    }
}