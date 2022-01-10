using UnityEngine;

using System;
using System.Runtime.InteropServices;

namespace FastPoints {
    public class Utils {
        public const int KB = 1024;
        public const int MB = 1048576;

        public static float SizeKB(uint bytes) {
            return bytes / 1024f;
        }

        public static float SizeMB(uint bytes) {
            return bytes / (1024f * 1024f);
        }
    }
}