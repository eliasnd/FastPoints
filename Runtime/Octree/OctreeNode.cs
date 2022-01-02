using UnityEngine;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    [Serializable]
    public class OctreeNode : MonoBehaviour {
        
        

        #region octree
        [SerializeField]
        int count = 0;
        public int PointCount { get { return count; } }

        [SerializeField]
        OctreeNode[] children = null;  // Child list
        
        [SerializeField]
        Vector3 minPoint;
        [SerializeField]
        Vector3 maxPoint;
        [SerializeField]
        #endregion

        #region io
        string filePath = "";
        int offset; // Offset into file in bytes
        int pointsWritten = 0;
        public bool AllPointsWritten { get { return count == pointsWritten; } }

        Mutex bufferLock;
        static int writeBufferSize = 4096;  // Write buffer size in bytes
        byte[] writeBuffer = new byte[writeBufferSize];
        int writeBufferIdx = 0;
        #endregion

        public OctreeNode(int count, int offset, string filePath) {
            this.count = count;
            this.offset = offset;
            this.filePath = filePath;
            bufferLock = new Mutex();
        }

        // Writes points to buffer. Writes buffer to file if forcewrite = true or if buffer over given size
        // Passing forceWrite with no points will write buffer, but not add anything
        public async Task WritePoints(Point[] points) {
            await Task.Run(() => {
                if (pointsWritten + points.Length > count)
                    throw new Exception($"Trying to write {points.Length} points to node with {count-pointsWritten} remaining points allocated");

                bufferLock.WaitOne();

                BinaryWriter bw = new BinaryWriter(File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));

                foreach (Point pt in points)
                    foreach (byte b in pt.ToBytes()) {
                        writeBuffer[writeBufferIdx] = b;
                        writeBufferIdx++;

                        // Flush buffer
                        if (writeBufferIdx == writeBufferSize) {
                            bw.Write(writeBuffer, 0, writeBufferIdx);
                            pointsWritten += writeBufferIdx / 15;
                            writeBufferIdx = 0;
                        }
                    }

                bw.Close();

                bufferLock.ReleaseMutex();
            });
        }

        public async Task FlushBuffer() {
            await Task.Run(() => {
                if (writeBufferIdx == 0)
                    throw new Exception("Trying to flush empty buffer!");

                bufferLock.WaitOne();

                BinaryWriter bw = new BinaryWriter(File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                bw.Write(writeBuffer, 0, writeBufferIdx);
                pointsWritten += writeBufferIdx;
                writeBufferIdx = 0;
                bw.Close();

                bufferLock.ReleaseMutex();
            });
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
}