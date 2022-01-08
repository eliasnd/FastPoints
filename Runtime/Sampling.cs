using UnityEngine;

using System;

namespace FastPoints {
    public class Sampling {

        public static Point[] PoissonSample(Point[] source, int sampleCount, float minDist, int rejectionCutoff = 30) {
            throw new NotImplementedException();
        }

        // Note: if more samples than points in source, repeats points
        public static Point[] RandomSample(Point[] source, int sampleCount) {
            System.Random rand = new System.Random();

            Point[] result = new Point[sampleCount];

            for (int i = 0; i < sampleCount; i++) {
                int ridx = rand.Next(source.Length);
                try {
                    result[i] = source[ridx];
                } catch {
                    Debug.Log($"Error on index {i}, ridx {ridx}, source length {source.Length}");
                }
                
            }

            return result;
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