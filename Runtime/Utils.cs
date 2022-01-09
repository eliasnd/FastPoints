using UnityEngine;

using System;
using System.Runtime.InteropServices;

namespace FastPoints {
    public class Utils {
        public static Color IntToColor(int col) {
            return new Color((col >> 24), ((col >> 16) & 0xff), ((col >> 8) & 0xff), (col & 0xff));
        }
    }
}