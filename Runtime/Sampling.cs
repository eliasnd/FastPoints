using UnityEngine;

using System;

namespace FastPoints {
    public class Sampling {

        public static Point[] PoissonSample(Point[] source, int sampleCount, float minDist, int rejectionCutoff = 30) {
            throw new NotImplementedException();
        }

        // Note: if more samples than points in source, repeats points
        public static Point[] RandomSample(Point[] source, int sampleCount) {
            Point[] result = new Point[sampleCount];
            RandomSample(source, result);
            return result;
        }

        // Writes directly to specified region of target 
        public static void RandomSample(Point[] source, Point[] target, int sStartIdx=0, int sEndIdx=-1, int tStartIdx=0, int tEndIdx=-1) {
            if (sEndIdx == -1)
                sEndIdx = source.Length;
            if (tEndIdx == -1)
                tEndIdx = target.Length;

            System.Random rand = new System.Random();

            for (int i = 0; i < tEndIdx-tStartIdx; i++) {
                int ridx = rand.Next(sStartIdx, sEndIdx);
                try {
                    target[tStartIdx+i] = source[ridx];
                } catch {
                    Debug.Log($"Error on index {i}, ridx {ridx}, source length {source.Length}");
                }
                
            }
        }

        public static Point[] UniformSample(Point[] source, int sampleCount) {
            int interval = (int)source.Length / sampleCount;
            Point[] result = new Point[sampleCount];


            for (int i = 0; i < sampleCount; i+=interval)
                result[i] = source[i];

            return result;
        }
    }
}