using UnityEngine;

using System;
using System.Runtime.InteropServices;
using System.Linq;



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

        public Bounds AsBounds() {
            return new Bounds(Center, Size);
        }

        // Gets AABB from this AABB with transformation applied
        public AABB Transform(Matrix4x4 mat) {
            Vector3[] corners = new Vector3[] {
                Min, new Vector3(Min.x, Min.y, Max.z),
                new Vector3(Min.x, Max.y, Min.z), new Vector3(Min.x, Max.y, Max.z),
                new Vector3(Max.x, Min.y, Min.z), new Vector3(Max.x, Min.y, Min.z),
                new Vector3(Max.x, Max.y, Min.z), new Vector3(Max.x, Max.y, Max.z),
            }.Select(vec => mat.MultiplyVector(vec)).ToArray();

            Vector3 newMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 newMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (Vector3 vec in corners) {
                newMin = Vector3.Min(newMin, vec);
                newMax = Vector3.Max(newMax, vec);
            }

            return new AABB(newMin, newMax);
        }

        // Get planes from frustum with GeometryUtil.CalculateFrustumPlanes(Camera cam)
        public bool Intersects(Plane[] planes) {
            return Utils.TestPlanesAABB(planes, AsBounds());
        }

        public Rect ToScreenRect(Matrix4x4 mat) {
            Vector3 origin = Utils.WorldToScreenPoint(new Vector3(Min.x, Max.y, 0f), mat);
            Vector3 extent = Utils.WorldToScreenPoint(new Vector3(Max.x, Min.y, 0f), mat);
     
            // Create rect in screen space and return - does not account for camera perspective
            // Rect coordinates are range [0, 1] x [0, 1]
            return new Rect(origin.x, 1 - origin.y, extent.x - origin.x, origin.y - extent.y);
        }

        public AABB[] Subdivide(int count) {
            AABB[] result = new AABB[count * count * count];

            double minX, minY, minZ;
            double maxX, maxY, maxZ;
            double xStep, yStep, zStep;

            xStep = (double)Size.x / count;
            yStep = (double)Size.y / count;
            zStep = (double)Size.z / count;

            for (int x = 0; x < count; x++) {
                minX = Min.x + x * xStep;
                maxX = Min.x + (x+1) * xStep;
                for (int y = 0; y < count; y++) {
                    minY = Min.y + y * yStep;
                    maxY = Min.y + (y+1) * yStep;
                    for (int z = 0; z < count; z++) {
                        minZ = Min.z + z * zStep;
                        maxZ = Min.z + (z+1) * zStep;
                        result[Utils.MortonEncode(z, y, x)] = new AABB(
                            new Vector3((float)(minX-1E-5), (float)(minY-1E-5), (float)(minZ-1E-5)), 
                            new Vector3((float)(maxX+1E-5), (float)(maxY+1E-5), (float)(maxZ+1E-5)));
                    }
                }
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