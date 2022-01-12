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
        public PointCloudData data;
        public bool generateTree = true;
        public int decimatedCloudSize = 1000000;
        public float pointSize = 1.5f;
        public int treeLevels = 5;
        #endregion

        ConcurrentQueue<Action> actions;
        ComputeShader computeShader;
        Octree tree;
        Thread treeThread;
        PointCloudData oldData = null;

        public void Start() {
            actions = new ConcurrentQueue<Action>();
            Debug.Log(actions);
            computeShader = (ComputeShader)Resources.Load("CountAndSort");
        }

        public void Update() {

            void ResetTree() {
                treeThread.Abort(); // Eventually clean up files here
                tree = null;
                treeThread = null;
            }

            if (data == null) {     // If no data
                if (tree != null)
                    ResetTree();
                return;
            }

            if (!generateTree && tree != null) {    // If not set to generate tree
                ResetTree();
            }

            if (data != oldData) {  // If new data put in
                if (tree != null) 
                    ResetTree();

                if (!data.Init)
                    data.Initialize();
                if (!data.DecimatedGenerated)
                    data.PopulateSparseCloud(decimatedCloudSize);

                if (generateTree) {
                    tree = new Octree(treeLevels, "Resources/Octree", actions);
                    treeThread = new Thread(new ParameterizedThreadStart(BuildTreeThread));
                    treeThread.Start(new BuildTreeParams(tree, data));
                }
            }

            if (actions.Count > 0) {
                Action action;
                actions.TryDequeue(out action);
                action();
            }

            if (!treeThread.IsAlive)
                data.TreePath = "Resources/Octree";

            oldData = data;
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

            if (data.TreeGenerated) {
                throw new NotImplementedException();
            } else if (data.DecimatedGenerated) {

                ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, System.Runtime.InteropServices.Marshal.SizeOf<Point>());
                cb.SetData(data.decimatedCloud);

                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", cb);
                mat.SetFloat("_PointSize", pointSize);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);

                cb.Dispose();
            }
        }

        static void BuildTreeThread(object obj) {
            BuildTreeParams p = (BuildTreeParams)obj;
            p.tree.BuildTree(p.data);
        }
    }
}