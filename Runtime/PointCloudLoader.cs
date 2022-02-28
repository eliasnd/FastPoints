using UnityEngine;
using UnityEditor;

using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    [ExecuteInEditMode]
    class PointCloudLoader : MonoBehaviour {

        #region public
        public PointCloudHandle handle;
        PointCloudData data;
        public bool generateTree = false;
        public int decimatedCloudSize = 1000000;
        public float pointSize = 1.5f;
        public int treeLevels = 5;
        #endregion

        int actionsPerFrame = 2;

        Dispatcher dispatcher;
        ComputeShader computeShader;
        Octree tree;
        Thread treeThread;
        PointCloudHandle oldHandle = null;

        public void Awake() {
            dispatcher = new Dispatcher();
            computeShader = (ComputeShader)Resources.Load("CountAndSort");
        }

        public void Reset() {
            dispatcher = new Dispatcher();
            computeShader = (ComputeShader)Resources.Load("CountAndSort");
            oldHandle = null;
        }

        public void Update() {

            void ResetTree() {
                // treeThread.Abort(); // Eventually clean up files here
                tree = null;
                treeThread = null;
                dispatcher = new Dispatcher();
            }

            if (handle == null) {     // If no data
                if (tree != null)
                    ResetTree();
                return;
            }

            if (!generateTree && tree != null) {    // If not set to generate tree
                ResetTree();
            }

            if (handle != oldHandle) {  // If new data put in
                if (!Directory.Exists($"Assets/FastPointsData"))
                    Directory.CreateDirectory($"Assets/FastPointsData");

                if (!File.Exists($"Assets/FastPointsData/{handle.Name}.asset")) {  // Create PointCloudData
                    // Debug.Log("Creating new PointCloudData");
                    data = ScriptableObject.CreateInstance<PointCloudData>();
                    data.handle = handle;
                    AssetDatabase.CreateAsset(data, $"Assets/FastPointsData/{handle.Name}.asset");
                } else {
                    // Debug.Log("Getting PointCloudData");
                    data = AssetDatabase.LoadAssetAtPath<PointCloudData>($"Assets/FastPointsData/{handle.Name}.asset");
                }
                
                // data.PointCount = 10000000;
                // data.Initialize();
                if (tree != null) 
                    ResetTree();

                if (!data.Init)
                    data.Initialize();
                if (!data.DecimatedGenerated)
                    _ = data.PopulateSparseCloud(decimatedCloudSize);

                if (generateTree && !data.TreeGenerated) {
                    tree = new Octree();
                    treeThread = new Thread(new ParameterizedThreadStart(BuildTreeThread));
                    Debug.Log("Starting octree thread");
                    string dirPath = $"Resources/FastPointsOctrees/{handle.Name}";
                    treeThread.Start(new BuildTreeParams(tree, dirPath, data, dispatcher, () => {
                        data.TreePath = dirPath;
                    }));
                }
            }

            for (int i = 0; i < actionsPerFrame; i++) {
                if (dispatcher.Count > 0) {
                    Action action;
                    dispatcher.TryDequeue(out action);
                    action();
                }
            }

            // if (!treeThread.IsAlive)
                // data.TreePath = "Resources/Octree";

            oldHandle = handle;
        }

        public void OnDrawGizmos() {
        #if UNITY_EDITOR
            // Ensure continuous Update calls.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
        #endif
        }

        public void OnRenderObject() {
            if (data == null)
                return;

            // if (data.TreeGenerated) {
            //     throw new NotImplementedException();
            // } else if (data.DecimatedGenerated) {
                // Debug.Log($"Point size: {System.Runtime.InteropServices.Marshal.SizeOf<Vector3>()} + {System.Runtime.InteropServices.Marshal.SizeOf<Color>()}");
                ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, Point.size);
                cb.SetData(data.decimatedCloud);

                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", cb);
                mat.SetFloat("_PointSize", pointSize);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);

                cb.Dispose();
            // }
        }

        static void BuildTreeThread(object obj) {
            BuildTreeParams p = (BuildTreeParams)obj;
            _ = p.tree.BuildTree(p.data, 3, p.dirPath, p.dispatcher, p.cb);
        }
    }
}