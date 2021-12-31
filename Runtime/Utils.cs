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

}