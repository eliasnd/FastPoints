using UnityEngine;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

using System;

namespace FastPoints {
    [ScriptedImporter(1, new string[] {"ply", "las", "laz"})]
    public class PointCloudImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext context) {
            PointCloudData data = ScriptableObject.CreateInstance<PointCloudData>();
            data.handle = new PointCloudHandle(context.assetPath);

            data.Init();

            context.AddObjectToAsset("data", data);
            context.SetMainObject(data);
        }
    }
}