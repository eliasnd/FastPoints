using UnityEngine;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

[ScriptedImporter(1, "laz")]
class LazImporter : ScriptedImporter {
    public override void OnImportAsset(AssetImportContext context) {

    }
}