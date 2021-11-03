using UnityEngine;

using System;
using System.Collections.Generic;

namespace FastPoints{
    public class PointCloudData : ScriptableObject {
        public BaseImporter importer;
        public Vector4[] decimatedCloud;
        int count;
        public int PointCount { get { return count; } }

        bool treeGenerated; // True if tree generation is complete; if not, Renderer uses decimated point cloud

        public void Init() {
            int decimatedSize = 1000;
            count = importer.PointCount;
            decimatedCloud = new Vector4[decimatedSize];

            importer.OpenStream();
            importer.SamplePoints(decimatedSize, decimatedCloud);
        }
    }
}