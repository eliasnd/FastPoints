using UnityEngine;

using System;
using System.IO;

namespace FastPoints {
    public class PointCloudHandle {
        public enum FileType { PLY, LAS, LAZ };
        FileType type;
        string path;

        public PointCloudHandle(string path) {
            this.path = path;

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

        public PointCloudHandle(string path, FileType type) {
            this.path = path;
            this.type = type;
        }

        public BaseStream GetStream() {
            switch (type) {
                case FileType.PLY:
                    return new PlyStream(path);
                case FileType.LAS:
                    return new LasStream(path);
                case FileType.LAZ:
                    return new LazStream(path);
            }

            return null;
        }
    }
}