using UnityEngine;

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FastPoints {
    public class Node {
        public string name;
        public AABB bbox;

        public Point[] points;
        public uint pointCount;
        public Node[] children = new Node[8];
        public uint offset;
        public bool subsampled;
        public bool IsLeaf
        {
            get
            {
                bool result = true;
                for (int i = 0; i < 8; i++)
                    result &= children[i] == null;
                return result;
            }
        }

        
    }

    public class NodeReference
    {
        public string name;
        public uint pointCount;
        public uint offset;
        public int level;
        public int x;
        public int y;
        public int z;
        public AABB bbox;
    }

    public class NodeEntry
    {
        public uint pointCount;
        public uint offset;
        public uint descendentCount;
        public AABB bbox;

        public byte[] ToBytes() {
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(pointCount));
            bytes.AddRange(BitConverter.GetBytes(offset));
            bytes.AddRange(BitConverter.GetBytes(descendentCount));
            bytes.AddRange(bbox.ToBytes());

            return bytes.ToArray();
        }

        public NodeEntry() {}

        public NodeEntry(byte[] bytes, int startIdx=0) {
            pointCount = BitConverter.ToUInt32(bytes, startIdx);
            offset = BitConverter.ToUInt32(bytes, startIdx+4);
            descendentCount = BitConverter.ToUInt32(bytes, startIdx+8);
            bbox = new AABB(bytes, startIdx+12);
        }
    }
}