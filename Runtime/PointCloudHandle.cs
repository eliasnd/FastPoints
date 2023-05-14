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
using System.IO;

namespace FastPoints {

    [Serializable]
    public class PointCloudHandle : ScriptableObject {
        public enum FileType { PLY, LAS, LAZ };
        [SerializeField]
        FileType type;
        public FileType Type { get { return type; } }
        [SerializeField]
        public string path;
        public string Name { get { return Path.GetFileNameWithoutExtension(path); } }
        public bool Converted;

        public void Initialize(string path, bool converted) {
            this.path = path;
            Converted = converted;

            switch (Path.GetExtension(path)) {
                case ".ply":
                    type = FileType.PLY;
                    break;
                case ".las":
                    type = FileType.LAS;
                    break;
                case ".laz":
                    type = FileType.LAZ;
                    break;
            }
        }

        public void Initialize(string path, FileType type) {
            this.path = path;
            this.type = type;
        }
    }
}