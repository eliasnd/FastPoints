using UnityEngine;

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FastPoints {
    public struct Node {
        public AABB bbox;
        public uint descendentCount;

        public Point[] points;
        public uint pointCount;
        public uint offset;
        public Node[] children;
        
        public Node(Point[] points, AABB bbox) {
            this.points = points;
            this.pointCount = (uint)points.Length;
            this.bbox = bbox;
            this.descendentCount = 0;
            this.children = null;
            this.offset = 0;
        }

        public Node(Node[] children, int pointCount) {
            Debug.Log("Making inner node");
            this.children = children;
            this.offset = 0;

            bbox = new AABB(children[0].bbox.Min, children[7].bbox.Max);
            points = new Point[pointCount];
            this.pointCount = (uint)pointCount;

            
            List<Node> nonEmptyChildren = new List<Node>();
            foreach (Node c in children)
                if (c.pointCount > 0)
                    nonEmptyChildren.Add(c);

            // Debug.Log("Got nonEmpty Children");

            descendentCount = 8;
            for (int i = 0; i < nonEmptyChildren.Count; i++)
            { // Get same number of points from all children
                descendentCount += nonEmptyChildren[i].descendentCount;
                Sampling.RandomSample(nonEmptyChildren[i].points, points, tStartIdx: i * (pointCount / nonEmptyChildren.Count), tEndIdx: (i + 1) * (pointCount / nonEmptyChildren.Count));
            }

            // Debug.Log("Sampled nonempty children");

            // Debug.Log("Made Inner Node");
        }

        public void WriteNode(QueuedWriter qw) {
            // Debug.Log($"Writing node with {points.Length} points");
            offset = (uint)qw.Enqueue(Point.ToBytes(points));
            // points = null;
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

        public async Task FlattenTree(Node[] arr, int startIdx=0) {
            arr[startIdx] = this;
            Node[] children = this.children;

            if (children != null) {
                await Task.Run(() => {
                    Task[] cTasks = new Task[8];
                    int offset=1;
                    for (int i = 0; i < 8; i++) {
                        Node n = children[i];
                        cTasks[i] = n.FlattenTree(arr, offset);
                        offset += (int)n.descendentCount+1;
                    }

                    Task.WaitAll(cTasks);
                });
            }
        }
    }
}