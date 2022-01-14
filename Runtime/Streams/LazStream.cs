using UnityEngine;
using Unity.Collections;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;

namespace FastPoints {
    public class LazStream : BaseStream {
        
        public LazStream(string path) : base(path) {}

        public override bool ReadPoints(int pointCount, Point[] target) {
            throw new NotImplementedException();
        }

        public override async Task  SamplePoints(int pointCount, Point[] target) {
            throw new NotImplementedException();
        }

        public override async Task ReadPointsToQueue(ConcurrentQueue<Point[]> queue, int maxQueued, int batchSize) {
            throw new NotImplementedException();
        }
    }
}