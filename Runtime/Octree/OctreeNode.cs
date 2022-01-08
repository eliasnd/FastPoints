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
    public class OctreeNode {
        
        

        #region octree
        [SerializeField]
        int count = 0;
        public int PointCount { get { return count; } }

        [SerializeField]
        OctreeNode[] children = null;  // Child list
        public enum NodeType { INTERNAL, LEAF };
        public NodeType Type { get { return children == null ? NodeType.LEAF : NodeType.INTERNAL; } }
        
        [SerializeField]
        public AABB BBox;
        #endregion

        #region io
        bool initialized = false;
        string filePath = "";
        Int64 offset; // Offset into file in bytes
        int bytesWritten = 0;
        int pointsWritten { get { return bytesWritten / 15; } }
        public bool AllPointsWritten { get { return count == pointsWritten; } }

        readonly object writeLock = new object();
        static int writeBufferSize = 4096;  // Write buffer size in bytes
        byte[] writeBuffer = new byte[writeBufferSize];
        int writeBufferIdx = 0;
        #endregion

        public OctreeNode() {}

        public OctreeNode(int count, Int64 offset, string filePath, AABB bbox) {
            InitializeLeaf(count, offset, filePath, bbox);
        }

        public void InitializeInternal(int count, Int64 offset, string filePath, AABB bbox) {
            this.count = count;
            this.offset = offset;
            this.filePath = filePath;

            // Debug.Log($"Initialized internal with {PointCount} points");

            this.children = new OctreeNode[8];
            for (int i = 0; i < 8; i++)
                children[i] = new OctreeNode();

            BBox = bbox;
            initialized = true;
        }

        public void InitializeLeaf(int count, Int64 offset, string filePath, AABB bbox) {
            this.count = count;
            this.offset = offset;
            this.filePath = filePath;

            // Debug.Log($"Initialized leaf with {PointCount} points");
            
            BBox = bbox;
            initialized = true;
        }

        // Writes points to buffer. Writes buffer to file if forcewrite = true or if buffer over given size
        // Passing forceWrite with no points will write buffer, but not add anything
        public async Task WritePoints(Point[] points) {
            await Task.Run(() => {
                if (pointsWritten + points.Length > count)
                    throw new Exception($"Trying to write {points.Length} points to node with {count-pointsWritten} remaining points allocated");

                lock (writeLock) {
                    BinaryWriter bw = new BinaryWriter(File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                    try {
                        bw.BaseStream.Seek(offset + bytesWritten, SeekOrigin.Begin);
                    } catch {
                        throw new Exception($"Error trying to seek offset {offset} + bytesWritten {bytesWritten} = {offset+bytesWritten}");
                    }
                    

                    foreach (Point pt in points) {
                        try {
                            foreach (byte b in pt.ToBytes()) {
                                writeBuffer[writeBufferIdx] = b;
                                writeBufferIdx++;

                                // Flush buffer
                                if (writeBufferIdx == writeBufferSize) {
                                    bw.Write(writeBuffer, 0, writeBufferIdx);
                                    bytesWritten += writeBufferIdx;
                                    writeBufferIdx = 0;
                                }
                            }
                        } catch {
                            throw new Exception($"Exception while writing points, writeBufferIdx is {writeBufferIdx}");
                        }
                    }

                    bw.Close();
                }

                
            });
        }

        public void Expand() {
            children = new OctreeNode[8];
        }

        public async Task FlushBuffer() {
            await Task.Run(() => {
                if (writeBufferIdx == 0)
                    return;
                    // throw new Exception("Trying to flush empty buffer!");

                lock (writeLock) {
                    BinaryWriter bw = new BinaryWriter(File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                    bw.BaseStream.Seek(offset + bytesWritten, SeekOrigin.Begin); 
                    bw.Write(writeBuffer, 0, writeBufferIdx);
                    bytesWritten += writeBufferIdx;
                    writeBufferIdx = 0;
                    bw.Close();
                }
            });
        }

        public async Task ReadPoints(Point[] target, int idx=0) {
            if (!AllPointsWritten)
                throw new Exception("Cannot read points before writing finished!");
                
            await Task.Run(() => {
                BinaryReader br = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                for (int i = 0; i < count; i++)
                    target[idx+i] = new Point(br.ReadBytes(15));
            });
        }

        public OctreeNode GetChild(int i) {
            if (Type == NodeType.LEAF)
                throw new Exception("Trying to get child of leaf node");
            return children[i];
        }

        // Populates points and bbox of internal node
        public async Task PopulateInternal() {
            await Task.Run(async () => {
                int nonEmptyChildren = 0;
                foreach (OctreeNode child in children)
                    if (child.PointCount > 0)
                        nonEmptyChildren++;

                foreach (OctreeNode child in children) {
                    List<Task> populateChildren = new List<Task>();

                    if (!child.AllPointsWritten)
                        if (child.Type == NodeType.LEAF)
                            throw new Exception("Cannot populate internal nodes until all descendent leaf nodes written");
                        else
                            populateChildren.Add(child.PopulateInternal());
                        
                    Task.WaitAll(populateChildren.ToArray());

                }

                foreach (OctreeNode child in children) {
                    if (child.PointCount == 0)
                        continue;

                    Point[] childPoints = new Point[child.PointCount];
                    // Debug.Log($"Count of child is {child.PointCount}");
                    child.ReadPoints(childPoints);
                    await WritePoints(Sampling.RandomSample(childPoints, count / nonEmptyChildren));
                }
            });
            
        }

        // Traverses tree DFS and populates target array. Need to implement point budget.
        public void SelectPoints(Point[] target, ref int idx, Camera cam) {
            if (!Intersects(cam))
                return;

            if (Type == NodeType.LEAF || DistanceCoefficient(cam) < 1f) {
                int startIdx = Interlocked.Add(ref idx, count) - count; // Reserve space in target array
                ReadPoints(target, startIdx);
            } else if (Type == NodeType.INTERNAL) {
                foreach (OctreeNode child in children)
                    child.SelectPoints(target, ref idx, cam);
            }
        }

        bool Intersects(Camera cam) {
            throw new NotImplementedException();
        }

        float DistanceCoefficient(Camera cam) {
            throw new NotImplementedException();
        }
    }
}