using UnityEngine;

using System;
using System.Runtime.InteropServices;



namespace FastPoints {
    // axis-aligned bounding box
    [Serializable]
    public struct AABB {
        public Vector3 Min;
        public Vector3 Max;
        public Vector3 Size { get { return Max - Min; } }
        public Vector3 Center { get { return Min + Size * 0.5f; } }

        public AABB(Vector3 Min, Vector3 Max) {
            this.Min = Min;
            this.Max = Max;
        }

        public AABB(byte[] bytes, int startIdx=0) {
            Min = new Vector3(
                BitConverter.ToSingle(bytes, startIdx+0),
                BitConverter.ToSingle(bytes, startIdx+4),
                BitConverter.ToSingle(bytes, startIdx+8)
            );

            Max = new Vector3(
                BitConverter.ToSingle(bytes, startIdx+12),
                BitConverter.ToSingle(bytes, startIdx+16),
                BitConverter.ToSingle(bytes, startIdx+20)
            );
        }

        public AABB[] Subdivide(int count) {
            AABB[] result = new AABB[count * count * count];

            float minX, minY, minZ;
            float maxX, maxY, maxZ;
            float xStep, yStep, zStep;

            xStep = Mathf.Lerp(Min.x, Max.x, 1f/count) - Min.x;
            yStep = Mathf.Lerp(Min.y, Max.y, 1f/count) - Min.y;
            zStep = Mathf.Lerp(Min.z, Max.z, 1f/count) - Min.z;

            minX = Min.x;
            maxX = Min.x + xStep;

            for (int x = 0; x < count; x++) {
                minY = Min.y;
                maxY = Min.y + yStep;
                for (int y = 0; y < count; y++) {
                    minZ = Min.z;
                    maxZ = Min.z + zStep;
                    for (int z = 0; z < count; z++) {
                        result[Utils.MortonEncode(z, y, x)] = new AABB(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
                        minZ = maxZ;
                        maxZ += zStep;
                    }
                    minY = maxY;
                    maxY += yStep;
                }
                minX = maxX;
                maxX += xStep;
            }
             
            return result;
        }

        public bool InAABB(Vector3 pos) {
            return (
                Min.x <= pos.x && pos.x <= Max.x &&
                Min.y <= pos.y && pos.y <= Max.y &&
                Min.z <= pos.z && pos.z <= Max.z
            );
        }

        public override bool Equals(object obj)
        {
            AABB other = (AABB)obj;
            return Min == other.Min && Max == other.Max;
        }

        public byte[] ToBytes() {
            byte[] minXBytes = BitConverter.GetBytes(Min.x);
            byte[] minYBytes = BitConverter.GetBytes(Min.y);
            byte[] minZBytes = BitConverter.GetBytes(Min.z);
            byte[] maxXBytes = BitConverter.GetBytes(Max.x);
            byte[] maxYBytes = BitConverter.GetBytes(Max.y);
            byte[] maxZBytes = BitConverter.GetBytes(Max.z);

            return new byte[] {
                minXBytes[0], minXBytes[1], minXBytes[2], minXBytes[3],
                minYBytes[0], minYBytes[1], minYBytes[2], minYBytes[3],
                minZBytes[0], minZBytes[1], minZBytes[2], minZBytes[3],
                maxXBytes[0], maxXBytes[1], maxXBytes[2], maxXBytes[3],
                maxYBytes[0], maxYBytes[1], maxYBytes[2], maxYBytes[3],
                maxZBytes[0], maxZBytes[1], maxZBytes[2], maxZBytes[3],
            };
            
        }

        public override string ToString() {
            return $"AABB( Min: ({Min.x}, {Min.y}, {Min.z}), Max: ({Max.x}, {Max.y}, {Max.z}) )";
        }

        public AABB child(int index) {
            AABB box;

            if ((index & 0b100) == 0) {
                box.Min.x = Min.x;
                box.Max.x = Center.x;
            } else {
                box.Min.x = Center.x;
                box.Max.x = Max.x;
            }

            if ((index & 0b010) == 0) {
                box.Min.y = Min.y;
                box.Max.y = Center.y;
            } else {
                box.Min.y = Center.y;
                box.Max.y = Max.y;
            }

            if ((index & 0b001) == 0) {
                box.Min.z = Min.z;
                box.Max.z = Center.z;
            } else {
                box.Min.z = Center.z;
                box.Max.z = Max.z;
            }

            return box;
        }
    }
}