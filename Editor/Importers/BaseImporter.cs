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
        public int PointCount { get { return count; } }
        protected int index;
        public int PointIndex { get { return index; } }

        public override void OnImportAsset(AssetImportContext context) {
            path = context.assetPath;
            index = 0;

            GameObject go = new GameObject();
            PointCloudData data = ScriptableObject.CreateInstance<PointCloudData>();
            data.importer = this;
            data.Init();

            context.AddObjectToAsset("prefab", go);
            context.AddObjectToAsset("data", data);
            context.SetMainObject(go);
        }

        public abstract bool OpenStream(); 

        public abstract bool ReadPoints(int pointCount, Vector4[] target);

        // Randomly sample pointCount points from the cloud. Used for generating decimated clouds before tree construction
        public abstract bool SamplePoints(int pointCount, Vector4[] target);
    }
}