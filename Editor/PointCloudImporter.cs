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