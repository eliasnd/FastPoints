using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FastPoints {
    [ExecuteInEditMode]
    class PointCloudLoader : MonoBehaviour {

        #region public
        public PointCloudData data;
        public ComputeShader countShader;
        public ComputeShader sortShader;

        #endregion

        #region checkpoints

        bool loading = false;
        bool reading = false; // Whether data has started reading from file
        bool merging = false;
        bool writing = false;

        #endregion

        // Parameters for reading
        int batchSize = 20000;
        ConcurrentQueue<Point[]> pointBatches;
        
        // Parameters for chunking
        int chunkCount = 128; // Number of chunks per axis, total chunks = chunkCount^3
        // RenderTexture counts; // Counts for chunking
        ComputeBuffer countBuffer; // Counts for chunking, flattened
        uint[] counts;
        uint[] mergeCounts;
        List<int> mergeIndices; // List of which indices are being used to represent merged chunks
        bool boundsPopulated = false;
        uint pointsCounted = 0;
        float progress = 0.0f;
        Queue<Point[]> countedBatches;

        public void Update() {
            if (data == null) {
                loading = false;
                reading = false;
                merging = false;
                writing = false;
                pointsCounted = 0;
                boundsPopulated = false;
                return;
            }

            if (countShader == null || sortShader == null)
                return;

            // Start loading if data not null and not loading already
            if (!loading) {
                loading = true;

                // Initialize batch queue
                pointBatches = new ConcurrentQueue<Point[]>();            
                countedBatches = new Queue<Point[]>();

                // Initialize counting shader
                // counts = new RenderTexture(chunkCount, chunkCount, 0, RenderTextureFormat.R16);
                // counts.enableRandomWrite = true;
                // counts.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                // counts.volumeDepth = chunkCount;
                // counts.Create();
                // Use compute buffer instead
                countBuffer = new ComputeBuffer(chunkCount * chunkCount * chunkCount, sizeof(UInt32));
                counts = new uint[chunkCount * chunkCount * chunkCount];
                mergeCounts = new uint[chunkCount * chunkCount * chunkCount];
                mergeIndices = new List<int>();

                int countHandle = countShader.FindKernel("CountPoints"); // Initialize count shader
                countShader.SetBuffer(countHandle, "_Counts", countBuffer);
                countShader.SetInt("_ChunkCount", chunkCount);
                countShader.SetInt("_ThreadBudget", Mathf.CeilToInt(batchSize / 4096f));

                // TODO: Initialize sorting shader
                
                // Start loading point cloud

                LoadPointCloud();

                return;
            }

            // Reading pass
            if (reading) {
                // If bounds not populated, wait
                if (!boundsPopulated) {
                    boundsPopulated = data.MinPoint.x < data.MaxPoint.x && data.MinPoint.y < data.MaxPoint.y && data.MinPoint.z < data.MaxPoint.z;
                    if (boundsPopulated) { // If bounds populated for first time, send to count shader
                        countShader.SetFloats("_MinPoint", new float[] { data.MinPoint.x, data.MinPoint.y, data.MinPoint.z });
                        countShader.SetFloats("_MaxPoint", new float[] { data.MaxPoint.x, data.MaxPoint.y, data.MaxPoint.z });
                    } else {
                        return;
                    }
                }

                // Debug.Log("Bounds: " + data.MinPoint.ToString() + ", " + data.MaxPoint.ToString());

                Point[] batch;
                if (!pointBatches.TryDequeue(out batch))
                    return;

                ComputeBuffer batchBuffer = new ComputeBuffer(batchSize, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                batchBuffer.SetData(batch);

                int countHandle = countShader.FindKernel("CountPoints");
                countShader.SetBuffer(countHandle, "_Points", batchBuffer);
                countShader.SetInt("_BatchSize", batch.Length);
                countShader.Dispatch(countHandle, 64, 1, 1);

                batchBuffer.Dispose();

                countedBatches.Enqueue(batch);
                pointsCounted += (uint)batch.Length;
                UnityEngine.Debug.Log("Counted total of " + pointsCounted + " points");

                if (pointsCounted == 20000) {
                    countBuffer.GetData(counts);
                    Debug.Log("First Count: " + counts[0]);
                    Debug.Log("Second Count: " + counts[1]);
                }
                if (pointsCounted == data.PointCount) {
                    reading = false;
                    merging = true;
                    countBuffer.GetData(counts);
                    Debug.Log("Done counting");
                }
            }

            if (merging) {
                MergeSparseChunks(counts);
                merging = false;
            }

            if (writing) {
                if (countedBatches.Count == 0) {
                    writing = false;
                    return;
                }

                Point[] batch = countedBatches.Dequeue();
            }
        }

        public void OnRenderObject() {
            if (data == null)
                return;

            if (data.TreeGenerated) {
                throw new NotImplementedException();
            } else if (data.DecimatedGenerated) {

                ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                cb.SetData(data.decimatedCloud);

                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", cb);
                mat.SetPass(0);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);

                Graphics.DrawProceduralNow(MeshTopology.Points, data.PointCount, 1);

                cb.Dispose();
            }
        }

        // Starts point cloud loading. Returns true if cloud already loaded, otherwise false
        bool LoadPointCloud() {

            if (data.Init && data.DecimatedGenerated && data.TreeGenerated)
                return true;

            if (!data.Init)
                data.Initialize();

            if (!data.DecimatedGenerated)
                data.PopulateSparseCloud();

            if (!data.TreeGenerated)
                GenerateTree();

            return false;
        }

        async void GenerateTree() {
            data.LoadPointBatches(batchSize, pointBatches);
            reading = true;
        }

        // Populates merge count array -- for each chunk merged, gets assigned index of lowest (x,y,z) coordinate of merged chunk
        async void MergeSparseChunks(uint[] counts) {
            int sparseCutoff = 100;

            Func<(int, int, int), uint> getCount = tup => counts[tup.Item1 * chunkCount * chunkCount + tup.Item2 * chunkCount + tup.Item3];
            Func<int, int, int, int, int, int, uint> countChunk = delegate(int x1, int x2, int y1, int y2, int z1, int z2) {
                uint sum = 0;
                for (int i = x1; i < x2; i++)
                    for (int j = y1; j < y2; j++)
                        for (int k = z1; k < z2; k++)
                            sum += getCount((i,j,k));
                return sum;
            };

            Action<int, int, int, int, int, int, uint> markChunk = delegate(int x1, int x2, int y1, int y2, int z1, int z2, uint mergeVal) {
                for (int i = x1; i < x2; i++)
                    for (int j = y1; j < y2; j++)
                        for (int k = z1; k < z2; k++) {
                            int chunkIdx = i * chunkCount * chunkCount + j * chunkCount + k;
                            mergeCounts[chunkIdx] = mergeVal;
                            if (mergeIndices.Contains(chunkIdx))
                                mergeIndices.Remove(chunkIdx);
                        }
                mergeIndices.Add((int)mergeVal);
            };
            

            await Task.Run(() => {
                for (uint i = 0; i < counts.Length; i++) {
                    mergeCounts[i] = i;
                    mergeIndices.Add((int)i);
                }

                for (int size = 2; size < chunkCount; size *= 2) {
                    for (int i = 1; i <= chunkCount / size; i++) {
                        for (int j = 1; j <= chunkCount / size; j++) {
                            for (int k = 1; k <= chunkCount / size; k++) {
                                if (countChunk((i-1)*size,i*size,(j-1)*size,j*size,(k-1)*size,k*size) <= sparseCutoff) {
                                    markChunk((i-1)*size,i*size,(j-1)*size,j*size,(k-1)*size,k*size, (uint)((i-1)*size));
                                }
                            }
                        }
                    }
                }
            });

            writing = true;
        }

        async void CreateChunkFiles(string root = "") {
            await Task.Run(() => {
               for (int i = 0; i < mergeIndices.Count; i++) {
                    String path = root != "" ? $"{root}/chunk{i}.ply" : $"chunk{i}.ply";
                    String header = $@"ply
                    format binary_little_endian 1.0
                    element vertex {mergeCounts[mergeIndices[i]]}
                    property float x
                    property float y
                    property float z
                    property uchar red
                    property uchar green
                    property uchar blue
                    end_header
                    ";
                    System.IO.File.WriteAllText(path, header);
               } 
            });
        }

        // Create binary ply files for tree nodes
        async void WriteChunk(int chunkIdx, Point[] points, string root = "") {
            await Task.Run(() => {

                String path = root == "" ? $"chunk{chunkIdx}.ply" : $"{root}/chunk{chunkIdx}.ply";

                BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Append));
                for (int i = 0; i < points.Length; i++) {
                    Point pt = points[i];
                    bw.Write(pt.pos.x);
                    bw.Write(pt.pos.y);
                    bw.Write(pt.pos.z);
                    bw.Write(Convert.ToByte((int)(pt.col.r * 255.0f)));
                    bw.Write(Convert.ToByte((int)(pt.col.g * 255.0f)));
                    bw.Write(Convert.ToByte((int)(pt.col.b * 255.0f)));
                }
                bw.Dispose();
            });
        }
    }
}