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
        Octree tree;
        Thread treeThread;

        public void Start() {
            actions = new ConcurrentQueue<Action>();
        }

        public void Update() {
            if (!generateTree) {
                tree = null;
                return;
            }

            if (data == null) {
                tree = null;
                treeThread.Abort(); // Should probably do some cleanup here
                treeThread = null;
                return;
            }
            else if (tree == null) {    // Start building tree
                tree = new Octree(treeLevels, "Resources/Octree", actions);
                treeThread = new Thread(new ParameterizedThreadStart(BuildTreeThread));
                treeThread.Start(new BuildTreeParams(tree));
            }

            if (actions.Count > 0) {
                Action action = actions.Dequeue();
                action();
            }

            if (!treeThread.IsAlive)
                
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

        static void BuildTreeThread(Object obj) {
            ((BuildTreeParams)obj).tree.BuildTree();
        }
    }

    struct BuildTreeParams {
        public Octree tree;
        public BuildTreeParams(Octree tree) {
            this.tree = tree;
        }
    }
}