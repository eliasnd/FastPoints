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
using System.Linq;

namespace FastPoints
{
    // axis-aligned bounding box
    [Serializable]
    public struct AABB
    {
        public Vector3 Min;
        public Vector3 Max;
        public Vector3 Size { get { return Max - Min; } }
        public Vector3 Center { get { return Min + Size * 0.5f; } }

        public AABB(Vector3 Min, Vector3 Max)
        {
            this.Min = Min;
            this.Max = Max;
        }

        public override bool Equals(object obj)
        {
            AABB other = (AABB)obj;
            return Min == other.Min && Max == other.Max;
        }

        public override string ToString()
        {
            return $"AABB( Min: ({Min.x}, {Min.y}, {Min.z}), Max: ({Max.x}, {Max.y}, {Max.z}) )";
        }

        public AABB ChildAABB(int index)
        {
            AABB box;

            if ((index & 0b100) == 0)
            {
                box.Min.x = Min.x;
                box.Max.x = Center.x;
            }
            else
            {
                box.Min.x = Center.x;
                box.Max.x = Max.x;
            }

            if ((index & 0b010) == 0)
            {
                box.Min.y = Min.y;
                box.Max.y = Center.y;
            }
            else
            {
                box.Min.y = Center.y;
                box.Max.y = Max.y;
            }

            if ((index & 0b001) == 0)
            {
                box.Min.z = Min.z;
                box.Max.z = Center.z;
            }
            else
            {
                box.Min.z = Center.z;
                box.Max.z = Max.z;
            }

            return box;
        }
    }
}