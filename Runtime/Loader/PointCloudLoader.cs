using UnityEngine;
using UnityEditor;

using System;
using System.Threading;

namespace FastPoints {
    [ExecuteInEditMode]
    class PointCloudLoader : MonoBehaviour {
        public PointCloudData data;
        bool loading;
        float progress = 0.0f;

        public void Update() {
            if (data == null && loading)
                loading = false;

            if (data != null && !loading) {
                loading = true;
                LoadPointCloud();
            }
        }

        void LoadPointCloud() {

            if (!data.Init)
                data.Initialize();

            if (!data.DecimatedGenerated)
                data.PopulateSparseCloud();

            if (!data.TreeGenerated)
                data.GenerateTree();
        }
        
    }
}