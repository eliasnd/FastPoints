using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading;

namespace FastPoints {
    public class PointCloudData : ScriptableObject {
        
        public PointCloudHandle handle;
        
        public Point[] decimatedCloud;

        [SerializeField]
        int count;
        public int PointCount { get { return count; } }

        [SerializeField]
        bool treeGenerated; // True if tree generation is complete; if not, Renderer uses decimated point cloud
        public bool TreeGenerated { get { return treeGenerated; } }

        public void Init() {
            int decimatedSize = 100000;
            decimatedCloud = new Point[decimatedSize];

            BaseStream stream = handle.GetStream();
            count = stream.PointCount;

            // stream.SamplePoints(decimatedSize, decimatedCloud);
            // stream.ReadPoints(decimatedSize, decimatedCloud);

            Debug.Log(decimatedCloud[100].ToString());
    
            Thread decimateThread = new Thread(GetSparseCloud);
            decimateThread.Start(new SparseCloudParams(stream, decimatedCloud));
        }

        static void GetSparseCloud(System.Object obj) {
            SparseCloudParams threadParams;

            try {
                threadParams = (SparseCloudParams)obj;
            } catch (InvalidCastException) {
                throw new ArgumentException();
            }

            threadParams.stream.SamplePoints(threadParams.target.Length, threadParams.target);

            Debug.Log("Thread done!");
            Point point = threadParams.target[threadParams.target.Length-1];
            Debug.Log(point.ToString());
        }
    }

    class SparseCloudParams {

        public SparseCloudParams(BaseStream stream, Point[] target) {
            this.stream = stream;
            this.target = target;
        }

        public BaseStream stream;
        public Point[] target;
    }
}