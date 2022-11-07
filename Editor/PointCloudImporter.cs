using UnityEngine;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

using System.IO;

using System;
using System.Threading;

namespace FastPoints {
    [ScriptedImporter(1, new string[] {"ply", "las", "laz"})]
    public class PointCloudImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext context) {
            // Type t = AssetDatabase.GetMainAssetTypeAtPath(context.assetPath);
            // Debug.Log("Import at startup.");
            // string assetPath = $"Assets/{Path.GetFileNameWithoutExtension(context.assetPath)}.asset";
            // if (File.Exists(assetPath)) {
            //     Debug.Log($"Asset for point cloud {Path.GetFileNameWithoutExtension(context.assetPath)} already exists, skipping...");
            //     return;
            // }
            // else {
            //     Debug.Log($"No file found at {assetPath}. Creating...");
            // }

            // PointCloudData data = ScriptableObject.CreateInstance<PointCloudData>();
            string expectedPa;
            PointCloudHandle handle = ScriptableObject.CreateInstance<PointCloudHandle>();
            handle.Initialize(
                context.assetPath,
                File.Exists(Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(context.assetPath) + "/metadata.json")
            );
            // data.handle = new PointCloudHandle(context.assetPath);
            // AssetDatabase.CreateAsset(data, assetPath);
            context.AddObjectToAsset("handle", handle);
            context.SetMainObject(handle);
        }
    }
}