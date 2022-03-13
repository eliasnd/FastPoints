using UnityEngine;

using System;
using System.Linq;
using System.Collections.Generic;

using Random = System.Random;

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

        public static void Sample(Node node, Action<Node> cb) {
            // if (node != null && node.points != null) {
            //     foreach (Point pt in node.points)
            //         if (Vector3.Min(pt.pos, node.bbox.Min) != node.bbox.Min || Vector3.Max(pt.pos, node.bbox.Max) != node.bbox.Max)
            //             Debug.LogError("Found point OOB");
            // } else
            //     Debug.Log("Sampling null");
                

            void traverse(Node node, Action<Node> cb) {
                foreach (Node child in node.children)
                    if (child != null && !child.subsampled)
                        traverse(child, cb);
                cb(node);
            }

            traverse(node, (Node node) => {
                node.subsampled = true;

                uint pointCount = node.pointCount;

                int gridSize = 128;
                int[] grid = new int[gridSize * gridSize * gridSize];
                for (int i = 0; i < grid.Length; i++)
                    grid[i] = -1;

                int iteration = 0;
                iteration++;

                Vector3 max = node.bbox.Max;
                Vector3 min = node.bbox.Min;

                Vector3 size = max - min;

                (int, double) ToCellIdx(Vector3 pos) {
                    Vector3 normalized = new Vector3((pos.x - min.x) / size.x, (pos.y - min.y) / size.y, (pos.z - min.z) / size.z);

                    float lx = 2f * ((float)gridSize * normalized.x % 1f) - 1f;
                    float ly = 2f * ((float)gridSize * normalized.y % 1f) - 1f;
                    float lz = 2f * ((float)gridSize * normalized.z % 1f) - 1f;

                    float distance = Mathf.Sqrt(lx * lx + ly * ly + lz * lz);

                    int x = Mathf.FloorToInt(gridSize * normalized.x);
                    int y = Mathf.FloorToInt(gridSize * normalized.y);
                    int z = Mathf.FloorToInt(gridSize * normalized.z);

                    int idx = x * gridSize * gridSize + y * gridSize + z;

                    return ( idx, distance );
                }

                if (node.IsLeaf) {
                    int[] indices = new int[node.pointCount];
                    for (int i = 0; i < node.pointCount; i++)
                        indices[i] = i;

                    Random rnd = new Random();
                    indices = indices.OrderBy(i => rnd.Next()).ToArray();

                    Point[] pointBuffer = new Point[node.pointCount];
                    for (int i = 0; i < node.pointCount; i++)
                        try { 
                            pointBuffer[i] = node.points[indices[i]];
                        } catch (Exception e)
                        {
                            Debug.Log(e.Message);
                        }

                    node.points = pointBuffer;
                }
                else
                {
                    List<bool[]> acceptedChildFlags = new();
                    List<int> rejectedCounts = new();
                    int acceptedCount = 0;

                    for (int c = 0; c < 8; c++)
                    {
                        Node child = node.children[c];

                        if (child == null) { 
                            acceptedChildFlags.Add(null);
                            rejectedCounts.Add(0);
                            continue;
                        }

                        CheckBBox(child.points, node.bbox);

                        bool[] acceptedFlags = new bool[(int)child.pointCount];
                        int rejectedCount = 0;

                        for (int i = 0; i < child.pointCount; i++) {
                            (int, double) cellIdx = ToCellIdx(child.points[i].pos);

                            try {

                                bool isAccepted =
                                    child.pointCount < 100 ||
                                    cellIdx.Item2 < 0.7 * Mathf.Sqrt(3) && grid[cellIdx.Item1] < iteration;

                            
                                if (isAccepted)
                                {
                                    grid[cellIdx.Item1] = iteration;
                                    acceptedCount++;
                                } else
                                    rejectedCount++;

                                acceptedFlags[i] = isAccepted;
                            } catch (Exception e) {
                                Debug.Log(e.StackTrace);
                                // int tmp = 0;
                            }
                        }

                        acceptedChildFlags.Add(acceptedFlags);
                        rejectedCounts.Add(rejectedCount);
                    }

                    List<Point> accepted = new(acceptedCount);
                    for (int c = 0; c < 8; c++)
                    {
                        Node child = node.children[c];

                        if (child == null)
                            continue;

                        int rejectedCount = rejectedCounts[c];
                        bool[] acceptedFlags = acceptedChildFlags[c];

                        List<Point> rejected = new(rejectedCount);

                        for (int i = 0; i < child.pointCount; i++)
                        {
                            bool isAccepted = acceptedFlags[i];
                            if (acceptedFlags[i])
                                accepted.Add(child.points[i]);
                            else
                                rejected.Add(child.points[i]);
                        }

                        if (rejectedCount == 0)
                            node.children[c] = null;
                        else
                        {
                            child.points = rejected.ToArray();
                            child.pointCount = (uint)rejectedCount;

                            cb(child);
                        }

                        node.points = accepted.ToArray();
                        node.pointCount = (uint)acceptedCount;
                    }
                }
            });
        }

        static void CheckBBox(Point[] points, AABB bbox) {
            foreach (Point pt in points)
                if (!bbox.InAABB(pt.pos))
                    throw new Exception($"BBox check on {bbox.ToString()} failed on ({pt.pos.x}, {pt.pos.y}, {pt.pos.z})");

            // return $"BBox check on {bbox.ToString()} passed";
        }
    }
}