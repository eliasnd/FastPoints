/* using UnityEngine;

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    [Serializable]
    class OctreeNode {
        [Serializable]
        int count = 0;
        public int PointCount { get { return count; } }
        public int Offset; // Offset into file in bytes
        [Serializable]
        int idx;
        public int Index { get { return idx; } }
        [Serializable]
        OctreeNode[] children = null;  // Child list
        
        [Serializable]
        Vector3 minPoint;
        [Serializable]
        Vector3 maxPoint;
        [Serializable]
        string path = "";
        public string Path { get { return path; } }

        ConcurrentBag<byte> writeBuffer;
        int maxWriteBufferSize = 1000;

        public OctreeNode(int idx) {
            this.idx = idx;
            writeBuffer = new List<byte>();
        }

        public bool Expand() {
            if (children != null)
                return false;

            children = new OctreeNode[8];
            for (int i = 0; i < 8; i++)
                children[i] = new OctreeNode(level+1);
            return true;
        }

        public void CreateFile(string root="Assets/tmp") {
            path = root != "" ? $"{root}/node_{idx}.bin" : $"node_{idx}.bin";
            File.Create(path);
            bw = new BinaryWriter(File.Open(path, FileMode.Append, FileAccess.Write));
        }

        // Writes points to buffer. Writes buffer to file if forcewrite = true or if buffer over given size
        // Passing forceWrite with no points will write buffer, but not add anything
        public async Task WritePoints(Point[] points = null, bool forceWrite = false) {
            if (path == "")
                throw new Exception("File not created!");

            await Task.Run(() => {
                if (points != null) {
                    foreach (Point pt in points)
                        foreach (byte b in points.ToBytes())
                            writeBuffer.Add(b);
                }

                if (writeBuffer.Count > maxWriteBufferSize || forceWrite) {
                    bw.Write(writeBuffer.ToArray(), 0, writeBuffer.Count);
                    Interlocked.Add(count, writeBuffer.Count);
                    writeBuffer.Clear();
                }
            });
        }

        // For deleting file after merging
        public bool DeleteFile() {
            if (path == "")
                throw new Exception("File not created!");

            File.Delete(path);
        }

        public OctreeNode GetChild(int i) {
            if (children == null)
                throw new Exception("Trying to get child of leaf node");
            return children[i];
        }

        public void Subsample(int pointCount) {
            throw new NotImplementedException();
        }
    }
} */