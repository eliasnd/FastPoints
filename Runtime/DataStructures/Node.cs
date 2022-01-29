using UnityEngine;

using System.Runtime.InteropServices;

namespace FastPoints {
    public struct Node {
        public Point[] points;
        public Node[] children;
        public AABB bbox;        

        public Node() {}

        public Node(Point[] points, AABB bbox) {
            this.points = points;
            this.bbox = bbox;
            this.children = null;
        }

        public Node(Node[] children, int pointCount) {
            this.children = children;
            // Should be able to do this much faster by exploiting xyz ordering of children
            Vector3 minPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxPoint = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Node c in children) {
                minPoint = Vector3.Min(minPoint, c.bbox.Min);
                maxPoint = Vector3.Max(maxPoint, c.bbox.Max);
            }
            bbox = new AABB(minPoint, maxPoint);

            points = new Point[pointCount];
            for (int i = 0; i < 8; i++) // Get same number of points from all children
                Sampling.RandomSample(children[i].points, points, tStartIdx=i*(pointCount/8), tEndIdx=(i+1)*(pointCount/8));
            this.points = points;
        }

        public int DescendentCount() {
            if (descendentCount == -1) {
                descendentCount = 0;
                if (children != null)
                    foreach (Node c in children)
                        descendentCount += c.DescendentCount();
            }
            return descendentCount;
        }

        public int CountPoints() {
            if (children == null)
                return points.Length;
            
            int count = points.Length;
            for (int i = 0; i < 8; i++)
                count += children[i].CountPoints();
            return count;
        }

        // Recursively populate hierarchy with nodeSize, descendentCount, descendentPointCount
        public async Task PopulateHierarchy(List<NodeReference> h, QueuedWriter w) {
            Task<uint> wt = w.Enqueue(Point.ToBytes(points));

            NodeReference nr = new NodeReference();
            int idx = h.Count;
            h.Add(nr);

            uint descendentCount = 0;
            uint pointCount = points.Length;
            uint offset;

            if (children != null) {
                Task[] cTasks = new Task[8];
                for (int i = 0; i < 8; i++)
                    cTasks[i] = children[i].PopulateHierarchy(h, w);

                await wt;
                offset = wt.Result;

                Task.WaitAll(cTasks);
                for (int i = 0; i < 8; i++)
                    descendentCount += h[descendentCount+1].descendentCount;
            } else {
                await wt;
                offset = wt.Result;
            }

            nr.bbox = bbox;
            nr.descendentCount = descendentCount;
            nr.pointCount = pointCount;
            nr.offset = offset;

            if (h[idx] != nr)
                Debug.LogError("NodeReference didn't populate properly");
        }
    }

    // Reference to node on disk
    public struct NodeReference {
        public const uint size = 36;
        public AABB bbox;
        public uint descendentCount;

        public uint pointCount;
        public uint offset;

        public NodeReference() {
            this.descendentCount = 0;
            this.pointCount = 0;
            this.offset = 0;
            this.bbox = null;
        }

        public NodeReference(uint pointCount, uint offset, uint descendentCount, AABB bbox) {
            this.descendentCount = descendentCount;
            this.pointCount = pointCount;
            this.offset = offset;
            this.bbox = bbox;
        }

        public NodeReference(byte[] bytes) {
            this.descendentCount = BitConverter.GetUInt32(bytes, 0);
            this.pointCount = BitConverter.GetUInt32(bytes, 4);
            this.offset = BitConverter.GetUInt32(bytes, 8);
            this.bbox = new AABB(bytes, 12);
        }

        public void ToBytes(byte[] target, int startIdx=0) {
            byte[] dBytes = BitConverter.GetBytes(descendentCount);
            byte[] pBytes = BitConverter.GetBytes(pointCount);
            byte[] oBytes = BitConverter.GetBytes(offset);
            byte[] bBytes = bbox.ToBytes();

            target[startIdx] = dBytes[0]; target[startIdx+1] = dBytes[1]; target[startIdx+2] = dBytes[2]; target[startIdx+3] = dBytes[3]; 
            target[startIdx+4] = pBytes[0]; target[startIdx+5] = pBytes[1];  target[startIdx+6] = pBytes[2]; target[startIdx+7] = pBytes[3]; 
            target[startIdx+8] = oBytes[0]; target[startIdx+9] = oBytes[1];  target[startIdx+10] = oBytes[2]; target[startIdx+11] = oBytes[3];

            target[startIdx+12] = bBytes[0]; target[startIdx+13] = bBytes[1]; target[startIdx+14] = bBytes[2]; target[startIdx+15] = bBytes[3];
            target[startIdx+16] = bBytes[4]; target[startIdx+17] = bBytes[5]; target[startIdx+18] = bBytes[6]; target[startIdx+19] = bBytes[7];
            target[startIdx+20] = bBytes[8]; target[startIdx+21] = bBytes[9]; target[startIdx+22] = bBytes[10]; target[startIdx+23] = bBytes[11];
            target[startIdx+24] = bBytes[12]; target[startIdx+25] = bBytes[13]; target[startIdx+26] = bBytes[14]; target[startIdx+27] = bBytes[15];
            target[startIdx+28] = bBytes[16]; target[startIdx+29] = bBytes[17]; target[startIdx+30] = bBytes[18]; target[startIdx+31] = bBytes[19];
            target[startIdx+32] = bBytes[20]; target[startIdx+33] = bBytes[21]; target[startIdx+34] = bBytes[22]; target[startIdx+35] = bBytes[23];
        }
    }
}