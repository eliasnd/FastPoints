// FastPoints
// Copyright (C) 2023  Elias Neuman-Donihue

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using UnityEngine;

using System;
using System.Runtime.InteropServices;

namespace FastPoints
{
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

        public static bool TestPlanesAABB(Plane[] frustum, AABB box, Vector3 offset, float[] scale)
        {
            if (frustum == null)
                Debug.LogError("Frustum null!");
            bool inside;

            float lx = (box.Min.x + offset.x) / scale[0];
            float ly = (box.Min.y + offset.y) / scale[1];
            float lz = (box.Min.z + offset.z) / scale[2];

            float ux = (box.Max.x + offset.x) / scale[0];
            float uy = (box.Max.y + offset.y) / scale[1];
            float uz = (box.Max.z + offset.z) / scale[2];

            for (int i = 0; i < 5; i++)
            {
                inside = false;
                Plane plane = frustum[i];   //Ignore Far Plane, because it doesnt work because of inf values
                inside |= plane.GetSide(new Vector3(lx, ly, lz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(lx, ly, uz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(lx, uy, lz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(lx, uy, uz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(ux, ly, lz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(ux, ly, uz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(ux, uy, lz));
                if (inside) continue;
                inside |= plane.GetSide(new Vector3(ux, uy, uz));
                if (!inside) return false;
            }
            return true;
        }

        //public static bool TestPlanesAABB(Plane[] planes, Bounds bounds)
        //{
        //    for (int i = 0; i < planes.Length; i++)
        //    {
        //        Plane plane = planes[i];
        //        Vector3 normal_sign = new Vector3(Math.Sign(plane.normal.x), Math.Sign(plane.normal.y), Math.Sign(plane.normal.z));
        //        Vector3 test_point = (bounds.center) + new Vector3(bounds.extents.x * normal_sign.x, bounds.extents.y * normal_sign.y, bounds.extents.z * normal_sign.z);

        //        float dot = Vector3.Dot(test_point, plane.normal);
        //        if (dot + plane.distance < 0)
        //            return false;
        //    }

        //    return true;
        //}

        public static Plane[] CalculateFrustumPlanes(Matrix4x4 mat)
        {

            float[] left = new float[4];
            float[] right = new float[4];
            float[] bottom = new float[4];
            float[] top = new float[4];
            float[] near = new float[4];
            float[] far = new float[4];

            for (int i = 3; i >= 0; i--) left[i] = mat[i, 3] + mat[i, 0];
            for (int i = 3; i >= 0; i--) right[i] = mat[i, 3] - mat[i, 0];
            for (int i = 3; i >= 0; i--) bottom[i] = mat[i, 3] + mat[i, 1];
            for (int i = 3; i >= 0; i--) top[i] = mat[i, 3] - mat[i, 1];
            for (int i = 3; i >= 0; i--) near[i] = mat[i, 3] + mat[i, 2];
            for (int i = 3; i >= 0; i--) far[i] = mat[i, 3] - mat[i, 2];

            System.Numerics.Plane numLeftPlane = new System.Numerics.Plane(left[0], left[1], left[2], left[3]);
            System.Numerics.Plane numRightPlane = new System.Numerics.Plane(right[0], right[1], right[2], right[3]);
            System.Numerics.Plane numBottomPlane = new System.Numerics.Plane(bottom[0], bottom[1], bottom[2], bottom[3]);
            System.Numerics.Plane numTopPlane = new System.Numerics.Plane(top[0], top[1], top[2], top[3]);
            System.Numerics.Plane numNearPlane = new System.Numerics.Plane(near[0], near[1], near[2], near[3]);
            System.Numerics.Plane numFarPlane = new System.Numerics.Plane(far[0], far[1], far[2], far[3]);

            Vector3 ToUnityV3(System.Numerics.Vector3 v)
            {
                return new Vector3(v.X, v.Y, v.Z);
            }

            return new Plane[] {
                new Plane(ToUnityV3(numLeftPlane.Normal), numLeftPlane.D), new Plane(ToUnityV3(numRightPlane.Normal), numRightPlane.D),
                new Plane(ToUnityV3(numBottomPlane.Normal), numBottomPlane.D), new Plane(ToUnityV3(numTopPlane.Normal), numTopPlane.D),
                new Plane(ToUnityV3(numNearPlane.Normal), numNearPlane.D), new Plane(ToUnityV3(numFarPlane.Normal), numFarPlane.D)
            };
        }

        public static Vector3 WorldToScreenPoint(Vector3 wp, Matrix4x4 mat)
        {
            // multiply world point by VP matrix
            Vector4 temp = mat * new Vector4(wp.x, wp.y, wp.z, 1f);

            if (temp.w == 0f)
            {
                // point is exactly on camera focus point, screen point is undefined
                // unity handles this by returning 0,0,0
                return Vector3.zero;
            }
            else
            {
                // convert x and y from clip space to window coordinates
                temp.x = (temp.x / temp.w + 1f) * .5f;  // Range [0, 1]
                temp.y = (temp.y / temp.w + 1f) * .5f;  // Range [0, 1]
                return new Vector3(temp.x, temp.y, wp.z);
            }
        }
    }
}