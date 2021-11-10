using UnityEngine;

using System;
using System.Collections.Generic;
using System.IO;

namespace FastPoints {
    public abstract class BaseStream {
        protected string path;
        protected FileStream stream;

        protected int count;
        public int PointCount { get { return count; } }
        protected int index;
        public int PointIndex { get { return index; } }

        public BaseStream(string filePath) {
            path = filePath;
            stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public abstract bool ReadPoints(int pointCount, Vector4[] target);
        public abstract bool SamplePoints(int pointCount, Vector4[] target);
    }
}