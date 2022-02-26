using UnityEngine;

using System;
using System.Runtime.InteropServices;

namespace FastPoints {
    public class Utils
    {
        public const int KB = 1024;
        public const int MB = 1048576;

        public static float SizeKB(uint bytes)
        {
            return bytes / 1024f;
        }

        public static float SizeMB(uint bytes)
        {
            return bytes / (1024f * 1024f);
        }

        // STOLEN FROM POTREE 2.0
        static ulong SplitBy3(uint a)
        {
            ulong x = a & 0x1fffff; // we only look at the first 21 bits
            x = (x | x << 32) & 0x1f00000000ffff; // shift left 32 bits, OR with self, and 00011111000000000000000000000000000000001111111111111111
            x = (x | x << 16) & 0x1f0000ff0000ff; // shift left 32 bits, OR with self, and 00011111000000000000000011111111000000000000000011111111
            x = (x | x << 8) & 0x100f00f00f00f00f; // shift left 32 bits, OR with self, and 0001000000001111000000001111000000001111000000001111000000000000
            x = (x | x << 4) & 0x10c30c30c30c30c3; // shift left 32 bits, OR with self, and 0001000011000011000011000011000011000011000011000011000100000000
            x = (x | x << 2) & 0x1249249249249249;
            return x;
        }

        // see https://www.forceflow.be/2013/10/07/morton-encodingdecoding-through-bit-interleaving-implementations/
        public static int MortonEncode(int x, int y, int z)
        {
            ulong answer = 0;
            answer |= SplitBy3((uint)x) | SplitBy3((uint)y) << 1 | SplitBy3((uint)z) << 2;
            return Convert.ToInt32(answer);
        }
    }
}