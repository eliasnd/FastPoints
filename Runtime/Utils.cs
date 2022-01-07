using UnityEngine;

using System;
using System.Runtime.InteropServices;

namespace FastPoints {
    public class Utils {
        public static Color IntToColor(int col) {
            return new Color((col >> 24), ((col >> 16) & 0xff), ((col >> 8) & 0xff), (col & 0xff));
        }
    }

    [StructLayout(LayoutKind.Sequential), Serializable]
    public struct Point {
        public Vector3 pos;
        public Color col;

        public Point(float x, float y, float z, int col) {
            pos = new Vector3(x, y, z);
            this.col = Utils.IntToColor(col);
        }

        public Point(byte[] bytes) {
            pos = new Vector3(
                BitConverter.ToSingle(bytes, 0),
                BitConverter.ToSingle(bytes, 4),
                BitConverter.ToSingle(bytes, 8)
            );

            col = new Color(bytes[12]/255f, bytes[13]/255f, bytes[14]/255f, 1f);
        }

        public Point(Vector3 pos, Color col) {
            this.pos = pos;
            this.col = col;
        }

        public string ToString() {
            return "Position: " + pos.ToString() + ", Color: " + col.ToString();// Utils.IntToColor(col).ToString();
        }

        public byte[] ToBytes() {
            byte[] x_bytes = BitConverter.GetBytes(pos.x);
            byte[] y_bytes = BitConverter.GetBytes(pos.y);
            byte[] z_bytes = BitConverter.GetBytes(pos.z);

            return new byte[] { 
                x_bytes[0], x_bytes[1], x_bytes[2], x_bytes[3],
                y_bytes[0], y_bytes[1], y_bytes[2], y_bytes[3],
                z_bytes[0], z_bytes[1], z_bytes[2], z_bytes[3],
                Convert.ToByte((int)(col.r * 255.0f)),
                Convert.ToByte((int)(col.g * 255.0f)),
                Convert.ToByte((int)(col.b * 255.0f))
            };
        }
        
    }

    // axis-aligned bounding box
    public struct AABB {
        public Vector3 Min;
        public Vector3 Max;

        public AABB(Vector3 Min, Vector3 Max) {
            this.Min = Min;
            this.Max = Max;
        }

        public AABB[] Subdivide(int count) {
            AABB[] result = new AABB[count * count * count];

            float minX, minY, minZ;
            float maxX, maxY, maxZ;
            float xStep, yStep, zStep;

            minX = minY = minZ = 0;

            xStep = maxX = Mathf.Lerp(Min.x, Max.x, 1f/count);
            yStep = maxY = Mathf.Lerp(Min.y, Max.y, 1f/count);
            zStep = maxZ = Mathf.Lerp(Min.z, Max.z, 1f/count);

            for (int x = 0; x < count; x++) {
                for (int y = 0; y < count; y++) {
                    for (int z = 0; z < count; z++) {
                        result[count * count * x + count * y + z] = new AABB(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
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
    }

}