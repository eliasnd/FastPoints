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