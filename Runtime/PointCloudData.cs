using UnityEngine;
using UnityEditor;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    public class PointCloudData : ScriptableObject { // , ISerializationCallbackReceiver {
        
        public PointCloudHandle handle;
        public Point testPoint;
        public Point[] decimatedCloud;
        // public uint[] chunkCounts;  // ONLY FOR DEVELOPMENT

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
        [SerializeField]
        bool boundsPopulated = false;
        public bool BoundsPopulated { get { return boundsPopulated; } }

        public void Initialize() {
            // count = 10000000;
            // minPoint = new Vector3(-1,-1,-1);
            // maxPoint = new Vector3(1,1,1);
            // testPoint = new Point(new Vector3(-1,-1,-1), new Color(0.5f,0.5f,0.5f));
            // decimatedCloud = new Point[] {
            //     new Point(new Vector3(-1,-1,-1), new Color(0f,0f,0f)),
            //     new Point(new Vector3(0,0,0), new Color(0.5f,0.5f,0.5f)),
            //     new Point(new Vector3(1,1,1), new Color(1f,1f,1f))
            // };

            // Debug.Log("Initialize");

            BaseStream stream = handle.GetStream();
            count = stream.PointCount;

            // PLY no header needs lazy init of bounds
            minPoint = stream.MinPoint;
            maxPoint = stream.MaxPoint;
            init = true;
        }

        public async Task PopulateSparseCloud(int size = 250000) {
            Debug.Log($"Populating decimated point cloud for {handle.Name}");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            decimatedCloud = new Point[size];
            
            await handle.GetStream().SamplePoints(decimatedCloud.Length, decimatedCloud);

            decimatedGenerated = true;

            AssetDatabase.ForceReserializeAssets();
            watch.Stop();
            Debug.Log($"Decimated cloud for {handle.Name} populated in {watch.ElapsedMilliseconds} ms");
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
                _ = LoadPointBatches(1000000, batches);

                minPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                maxPoint = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                int pointsScanned = 0;

                int j = 0;

                while (pointsScanned < count) {
                    Point[] batch;
                    while (!batches.TryDequeue(out batch)) {}

                    Vector3 minBatch = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 maxBatch = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                    int i = 0;
                    foreach (Point p in batch) { 
                        minPoint = Vector3.Min(minPoint, p.pos);
                        maxPoint = Vector3.Max(maxPoint, p.pos);
                        i++;
                    }

                    j += i;

                    pointsScanned += batch.Length;
                }

                Debug.Log($"Preliminarily populated bounds {minPoint.ToString()}, {maxPoint.ToString()}");
            });

            boundsPopulated = true;

            AssetDatabase.ForceReserializeAssets();
        }
    }
}