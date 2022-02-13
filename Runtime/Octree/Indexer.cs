using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace FastPoints {
    public class Indexer {

        static int treeDepth = 3;

        public static async Task IndexChunk(PointCloudData data, string chunkPath, Dispatcher dispatcher) {
            Debug.Log("I1");

            // Get BBOX better eventually
            string chunkCode = Path.GetFileNameWithoutExtension(chunkPath);
            AABB curr = new AABB(data.MinPoint, data.MaxPoint);
            for (int i = 1; i < chunkCode.Length; i++) {
                AABB[] bbs = curr.Subdivide(2);
                int idx = chunkCode[i] - '0';
                curr = bbs[idx];
            }

            float[] minPoint = new float[] { curr.Min.x-1E-5f, curr.Min.y-1E-5f, curr.Min.z-1E-5f };
            float[] maxPoint = new float[] { curr.Max.x+1E-5f, curr.Max.y+1E-5f, curr.Max.z+1E-5f };

            Point[] points = Point.ToPoints(File.ReadAllBytes(chunkPath));

            // foreach (Point pt in points) {
            //     if (pt.pos.x < minPoint[0] || pt.pos.y < minPoint[1] || pt.pos.z < minPoint[2])
            //         Debug.LogError("Found point outside min");
            //     if (pt.pos.x > maxPoint[0] || pt.pos.y > maxPoint[1] || pt.pos.z > maxPoint[2])
            //         Debug.LogError("Found point outside max");
            // }

            Debug.Log("I2");

            // DONE INIT CHECKS

            int gridSize = (int)Mathf.Pow(2, treeDepth);
            int gridTotal = gridSize * gridSize * gridSize;
            int threadBudget = Mathf.CeilToInt(points.Length / 4096f);

            int[] mortonIndices = new int[gridSize * gridSize * gridSize];

            for (int x = 0; x < gridSize; x++)
            for (int y = 0; y < gridSize; y++)
            for (int z = 0; z < gridSize; z++)
                mortonIndices[x * gridSize * gridSize + y * gridSize + z] = Convert.ToInt32(Utils.MortonEncode((uint)x, (uint)y, (uint)z));

            ComputeShader computeShader;

            // Action outputs
            uint[] nodeCounts = new uint[gridTotal];
            uint[] nodeStarts = new uint[gridTotal];
            Point[] sortedPoints = new Point[points.Length];

            await dispatcher.EnqueueAsync(() => {
                // INITIALIZE

                computeShader = (ComputeShader)Resources.Load("CountAndSort");

                computeShader.SetInt("_BatchSize", points.Length);
                computeShader.SetInt("_ThreadBudget", threadBudget);
                computeShader.SetFloats("_MinPoint", minPoint);
                computeShader.SetFloats("_MaxPoint", maxPoint);

                ComputeBuffer mortonBuffer = new ComputeBuffer(gridTotal, sizeof(int));
                mortonBuffer.SetData(mortonIndices);
                computeShader.SetBuffer(computeShader.FindKernel("CountPoints"), "_MortonIndices", mortonBuffer);
                computeShader.SetBuffer(computeShader.FindKernel("SortLinear"), "_MortonIndices", mortonBuffer);

                ComputeBuffer pointBuffer = new ComputeBuffer(points.Length, Point.size);
                pointBuffer.SetData(points);

                // COUNT

                ComputeBuffer countBuffer = new ComputeBuffer(gridTotal, sizeof(uint));

                int countHandle = computeShader.FindKernel("CountPoints");
                computeShader.SetBuffer(countHandle, "_Points", pointBuffer);
                computeShader.SetBuffer(countHandle, "_Counts", countBuffer);

                computeShader.Dispatch(countHandle, 64, 1, 1);

                countBuffer.GetData(nodeCounts);

                nodeStarts[0] = 0;
                for (int i = 1; i < nodeStarts.Length; i++)
                    nodeStarts[i] = nodeStarts[i-1] + nodeCounts[i-1];

                // SORT

                ComputeBuffer sortedBuffer = new ComputeBuffer(points.Length, Point.size);
                ComputeBuffer startBuffer = new ComputeBuffer(gridTotal, sizeof(uint));
                startBuffer.SetData(nodeStarts);
                ComputeBuffer offsetBuffer = new ComputeBuffer(gridTotal, sizeof(uint));
                uint[] chunkOffsets = new uint[gridTotal];   // Initialize to 0s?
                offsetBuffer.SetData(chunkOffsets);

                int sortHandle = computeShader.FindKernel("SortLinear");  // Sort points into array
                computeShader.SetBuffer(sortHandle, "_Points", pointBuffer);
                computeShader.SetBuffer(sortHandle, "_SortedPoints", sortedBuffer); // Must output adjacent nodes in order
                computeShader.SetBuffer(sortHandle, "_ChunkStarts", startBuffer);
                computeShader.SetBuffer(sortHandle, "_ChunkOffsets", offsetBuffer);

                computeShader.Dispatch(sortHandle, 64, 1, 1);

                sortedBuffer.GetData(sortedPoints);

                // SUBSAMPLE EVENTUALLY

                // CLEAN UP

                sortedBuffer.Release();
                pointBuffer.Release();
                startBuffer.Release();
                offsetBuffer.Release();
                countBuffer.Release();
                pointBuffer.Release();
            });

            Debug.Log("I3");

            // MAKE SUM PYRAMID

            uint[][] sumPyramid = new uint[treeDepth][];
            sumPyramid[treeDepth-1] = nodeCounts;

            for (int l = treeDepth-2; l >= 0; l--) {
                int layerSize = (int)Mathf.Pow(2, l);
                int lastLayerSize = layerSize * 2;
                uint[] countLayer = new uint[layerSize * layerSize * layerSize];
                uint[] lastLayer = sumPyramid[l+1];

                for (int x = 0; x < layerSize; x++)
                for (int y = 0; y < layerSize; y++)
                for (int z = 0; z < layerSize; z++) {
                    int countLayerIdx = (int)Utils.MortonEncode((uint)x, (uint)y, (uint)z);
                    int lastLayerIdx = (int)Utils.MortonEncode((uint)(2 * x), (uint)(2 * y), (uint)(2 * z));
                    uint sum = lastLayer[lastLayerIdx] + lastLayer[lastLayerIdx + 1] + lastLayer[lastLayerIdx + 2] +
                            lastLayer[lastLayerIdx + 3] + lastLayer[lastLayerIdx + 4] + lastLayer[lastLayerIdx + 5] +
                            lastLayer[lastLayerIdx + 6] + lastLayer[lastLayerIdx + 7];
                    countLayer[countLayerIdx] = sum;
                        
                }

                sumPyramid[l] = countLayer;
            }

            uint[][] offsetPyramid = new uint[treeDepth][];
            for (int l = 0; l < treeDepth; l++)
            {
                uint curr = 0;
                for (int i = 0; i < offsetPyramid[l].Length; i++)
                {
                    offsetPyramid[l][i] = curr;
                    curr += sumPyramid[l][i];
                }
            }
        }
    }
}