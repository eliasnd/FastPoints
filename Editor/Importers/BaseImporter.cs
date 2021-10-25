using UnityEngine;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

using System;
using System.IO;

namespace FastPoints {
    public abstract class BaseImporter : ScriptedImporter {

        protected string path;
        protected FileStream stream;

        protected int count;
        int PointCount { get { return count; } }
        protected int index;
        int PointIndex { get { return index; } }
        public abstract bool OpenStream(); 

        public abstract bool ReadPoints(int pointCount, Vector4[] target);
    }
}