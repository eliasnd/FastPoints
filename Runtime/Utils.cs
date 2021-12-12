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
    }

    public class Ref<T>
    {
        public Ref() { }
        public Ref(T value) { Value = value; }
        public T Value { get; set; }
        public override string ToString()
        {
            T value = Value;
            return value == null ? "" : value.ToString();
        }
        public static implicit operator T(Ref<T> r) { return r.Value; }
        public static implicit operator Ref<T>(T value) { return new Ref<T>(value); }
    }
}