using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace FastPoints {
    public class PointCloudData : ScriptableObject {
        
        public PointCloudHandle handle;
        
        public Point[] decimatedCloud;

        [SerializeField]
        int count;
        public int PointCount { get { return count; } }

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
            int decimatedSize = 100000;
            decimatedCloud = new Point[decimatedSize];

            count = handle.GetStream().PointCount;
            init = true;
        }

        public async Task PopulateSparseCloud() {
            await Task.Run(() => {
                handle.GetStream().SamplePoints(decimatedCloud.Length, decimatedCloud);
            });

            decimatedGenerated = true;
        }

        public async Task GenerateTree() {
            int pointBatchSize = 10000;
            ConcurrentQueue<Point[]> pointBatches = new ConcurrentQueue<Point[]>();

            bool allPointsRead = false;

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

                allPointsRead = true;
            });

            Thread t2 = new Thread(() => {
                while (!allPointsRead || pointBatches.Count > 0) {
                    Point[] batch;
                    while (!pointBatches.TryDequeue(out batch)) {} // Dequeue point batch
                    // TODO: Send to GPU
                    Debug.Log(batch.Length);
                }
            });

            t1.Start();
            t2.Start();
        }
    }
}