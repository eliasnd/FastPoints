using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading;

namespace FastPoints {
    public class PointCloudData : ScriptableObject {
        public PointCloudHandle handle;
        public Vector4[] decimatedCloud;
        int count;
        public int PointCount { get { return count; } }

        bool treeGenerated; // True if tree generation is complete; if not, Renderer uses decimated point cloud

        public void Init() {
            int decimatedSize = 1000;
            decimatedCloud = new Vector4[decimatedSize];

            BaseStream stream = handle.GetStream();

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
        }
    }

    class SparseCloudParams {

        public SparseCloudParams(BaseStream stream, Vector4[] target) {
            this.stream = stream;
            this.target = target;
        }

        public BaseStream stream;
        public Vector4[] target;
    }
}