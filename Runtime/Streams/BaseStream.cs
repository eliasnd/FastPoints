using UnityEngine;
using Unity.Collections;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

namespace FastPoints {
    public abstract class BaseStream {
        protected string path;
        protected FileStream stream;

        protected int count;
        public int PointCount { get { return count; } }
        protected int index;
        public int PointIndex { get { return index; } }

        // Min and max points for bounding box
        protected Vector3 minPoint;
        public Vector3 MinPoint { get { return minPoint; } }
        protected Vector3 maxPoint;
        public Vector3 MaxPoint { get { return maxPoint; } }

        public BaseStream(string filePath) {
            path = filePath;
            stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public abstract bool ReadPoints(int pointCount, Point[] target);
        public abstract async bool ReadPointsToQueue(ConcurrentQueue<Point> queue, int maxQueued, int batchSize);
        public abstract Task SamplePoints(int pointCount, Point[] target);
    }
}