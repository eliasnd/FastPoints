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
    public class PointCloudLoader : MonoBehaviour {

        #region public
        public PointCloudHandle handle;
        PointCloudData data;
        public bool generateTree = true;
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

        #region rendering
        Thread readThread;
        ReadTreeParams rp;
        ComputeBuffer pointBuffer;
        Material mat;
        #endregion

        bool treeGeneratedOld = false;

        public void Awake() {
            dispatcher = new Dispatcher();
            mat = new Material(Shader.Find("Custom/DefaultPoint"));
            computeShader = (ComputeShader)Resources.Load("CountAndSort");
        }

        public void Reset() {
            dispatcher = new Dispatcher();
            computeShader = (ComputeShader)Resources.Load("CountAndSort");
            oldHandle = null;
        
            if (treeThread != null) {
                treeThread.Abort();
                treeThread = null;
            }
            if (readThread != null) {
                readThread.Abort();
                readThread = null;
            }
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
                tree = new Octree($"Resources/FastPointsOctrees/{handle.Name}");

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
                // if (tree != null) 
                //     ResetTree();

                if (!data.Init)
                    data.Initialize();
                if (!data.DecimatedGenerated)
                    _ = data.PopulateSparseCloud(decimatedCloudSize);

                if (generateTree && !data.TreeGenerated) {
                    treeThread = new Thread(new ParameterizedThreadStart(BuildTreeThread));
                    Debug.Log("Starting octree thread");
                    treeThread.Start(new BuildTreeParams(tree, data, dispatcher, () => {
                        data.TreePath = tree.dirPath;
                    }));
                }

                
            }

            if (!treeWasGenerated && data.TreeGenerated) {
                if (!tree.Loaded)
                    tree.LoadTree();

                int pointBudget = 10000000;
                pointBuffer = new ComputeBuffer(pointBudget, Point.size);
                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", pointBuffer);
                mat.SetFloat("_PointSize", pointSize);

                Camera cam = Camera.current == null ? Camera.main : Camera.current;
                readThread = new Thread(new ParameterizedThreadStart(ReadTreeThread));
                readThread.Start(new ReadTreeParams(tree, cam.projectionMatrix * cam.worldToCameraMatrix, pointBuffer, dispatcher, () => {}));
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

            if (data.TreeGenerated) {
                Debug.Log("Render");
                Camera cam = Camera.current == null ? Camera.main : Camera.current;
                rp.mat = cam.projectionMatrix * cam.worldToCameraMatrix;
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, (int)Interlocked.Read(ref rp.pointCount), 1);
            } else if (data.DecimatedGenerated) {
                // Debug.Log($"Point size: {System.Runtime.InteropServices.Marshal.SizeOf<Vector3>()} + {System.Runtime.InteropServices.Marshal.SizeOf<Color>()}");
                ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, Point.size);
                cb.SetData(data.decimatedCloud);

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
            _ = p.tree.BuildTree(p.data, 3, p.dispatcher, p.cb);
        }

        struct BuildTreeParams {
            public Octree tree;
            public PointCloudData data;
            public Dispatcher dispatcher;
            public Action cb;
            public BuildTreeParams(Octree tree, PointCloudData data, Dispatcher dispatcher, Action cb) {
                this.tree = tree;
                this.data = data;
                this.dispatcher = dispatcher;
                this.cb = cb;
            }
        }

        static void ReadTreeThread(object obj) {
            Debug.Log("Starting read thread");
            ReadTreeParams p = (ReadTreeParams)obj;
            Octree tree = p.tree;
            Matrix4x4 mat = p.mat;
            ComputeBuffer pointBuffer = p.pointBuffer;

            // TEST AABB CULLING
            while (!p.stopSignal) {
                Debug.Log(mat);
                AABB bbox = new AABB(new Vector3(0,0,0), new Vector3(5,5,5));
                Rect screenRect = bbox.ToScreenRect(p.mat);
                Debug.Log(screenRect.ToString());
            }
            // END TEST

            try {
                bool[] lastMask = tree.GetTreeMask(mat);
                while (!p.stopSignal) {
                    Debug.Log("Checking mask");
                    Thread.Sleep(30);  // Pick interval for updating mask 
                    bool[] mask = tree.GetTreeMask(mat);
                    int pointCount = Mathf.Min(tree.CountVisiblePoints(mask), pointBuffer.count);
                    if (!lastMask.Equals(mask)) {
                        Debug.Log("New mask");
                        if (pointCount > 0) {
                            Debug.Log("Nonempty mask");
                            pointBuffer.SetData(tree.ApplyTreeMask(mask), 0, 0, pointCount);
                        }
                        Interlocked.Exchange(ref p.pointCount, (long)pointCount);
                        
                        lastMask = mask;
                    }
                }
            } catch (Exception e) {
                Debug.Log($"ReadThread Exception. Message: {e.Message}, Backtrace: {e.StackTrace}, Inner: {e.InnerException}");
            }
        }

        struct ReadTreeParams {
            public Octree tree;
            public Action cb;
            public Matrix4x4 mat;
            public ComputeBuffer pointBuffer;
            public Dispatcher dispatcher;
            public long pointCount;
            public bool stopSignal;

            public ReadTreeParams(Octree tree, Matrix4x4 mat, ComputeBuffer pointBuffer, Dispatcher dispatcher, Action cb) {
                this.tree = tree;
                this.mat = mat;
                this.cb = cb;
                this.pointCount = 0;
                this.pointBuffer = pointBuffer;
                this.dispatcher = dispatcher;
                this.stopSignal = false;
            }
        }
    }
}