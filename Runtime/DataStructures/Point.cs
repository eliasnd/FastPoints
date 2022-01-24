using UnityEngine;

using System;
using System.Runtime.InteropServices;

namespace FastPoints {
    [StructLayout(LayoutKind.Sequential), Serializable]
    public struct Point {
        public const int size = 28;
        public Vector3 pos;
        public Color col;

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

        public void ToBytes(byte[] target, int offset = 0) {
            // Debug.Log("New tobytes");
            byte[] x_bytes = BitConverter.GetBytes(pos.x);
            byte[] y_bytes = BitConverter.GetBytes(pos.y);
            byte[] z_bytes = BitConverter.GetBytes(pos.z);

            
            target[offset] = x_bytes[0]; target[offset+1] = x_bytes[1]; target[offset+2] = x_bytes[2]; target[offset+3] = x_bytes[3];
            target[offset+4] = y_bytes[0]; target[offset+5] = y_bytes[1]; target[offset+6] = y_bytes[2]; target[offset+7] = y_bytes[3];
            target[offset+8] = z_bytes[0]; target[offset+9] = z_bytes[1]; target[offset+10] = z_bytes[2]; target[offset+11] = z_bytes[3];
            target[offset+12] = Convert.ToByte((int)(col.r * 255.0f));
            target[offset+13] = Convert.ToByte((int)(col.g * 255.0f));
            target[offset+14] = Convert.ToByte((int)(col.b * 255.0f));
            // Debug.Log("new tobytes done");
        }

        public static void ToBytes(Point[] src, byte[] target, int offset = 0) {
            if (target.Length != src.Length * 15)
                throw new Exception("Bytes array must be 15 times length of points array");

            for (int p = 0; p < src.Length; p++) {
                Point pt = src[p];

                int b = p * 15;

                byte[] x_bytes = BitConverter.GetBytes(pt.pos.x);
                byte[] y_bytes = BitConverter.GetBytes(pt.pos.y);
                byte[] z_bytes = BitConverter.GetBytes(pt.pos.z);

                target[offset+b] = x_bytes[0]; target[offset+b+1] = x_bytes[1]; target[offset+b+2] = x_bytes[2]; target[offset+b+3] = x_bytes[3];
                target[offset+b+4] = y_bytes[0]; target[offset+b+5] = y_bytes[1]; target[offset+b+6] = y_bytes[2]; target[offset+b+7] = y_bytes[3];
                target[offset+b+8] = z_bytes[0]; target[offset+b+9] = z_bytes[1]; target[offset+b+10] = z_bytes[2]; target[offset+b+11] = z_bytes[3];
                target[offset+b+12] = Convert.ToByte((int)(pt.col.r * 255.0f));
                target[offset+b+13] = Convert.ToByte((int)(pt.col.g * 255.0f));
                target[offset+b+14] = Convert.ToByte((int)(pt.col.b * 255.0f));
            }
        }
        
        public static byte[] ToBytes(Point[] src) {
            byte[] bytes = new byte[src.Length*15];
            ToBytes(src, bytes);
            return bytes;
        }

        public static void ToPoints(byte[] src, Point[] target) {
            if (src.Length != target.Length * 15)
                throw new Exception("Bytes array must be 15 times length of points array");

            for (int i = 0; i < target.Length; i++) {
                int startIdx = i * 15;
                target[i] = new Point(
                    new Vector3(
                        BitConverter.ToSingle(src, startIdx),
                        BitConverter.ToSingle(src, startIdx+4),
                        BitConverter.ToSingle(src, startIdx+8)
                    ),
                    new Color(src[startIdx+12]/255f, src[startIdx+13]/255f, src[startIdx+14]/255f, 1f)
                );
            }
        }

        public static Point[] ToPoints(byte[] src) {
            if (src.Length % 15 != 0)
                throw new Exception("Byte array cannot be evenly divided into points!");

            Point[] points = new Point[src.Length / 15];
            ToPoints(src, points);
            return points;
        }

        
    }
}