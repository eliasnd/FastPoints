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
        public Camera cam = null;
        public bool debugReader = false;
        public bool runOnce = false;
        public string highlightedNode="";
        public int maxOctreeRenderDepth = -1;
        public int pointBudget = 1000000;
        public bool showBBoxs = true;
        public bool useNodesToShow = false;
        public string[] nodesToShow;
        public bool useComputeShader = false;
        #endregion

        int actionsPerFrame = 2;

        Dispatcher dispatcher;
        ComputeShader computeShader;
        Octree tree;
        Thread treeThread;
        PointCloudHandle oldHandle = null;

        #region rendering
        Thread readThread;
        ComputeBuffer pointBuffer;
        Material mat;
        #endregion

        bool treeGeneratedOld = false;
        OctreeReader reader;
        bool readerRunning = false;
        bool awaitingDecimated = false;

        bool firstFrame;

        public void Awake() {
            dispatcher = new Dispatcher();
            mat = new Material(Shader.Find("Custom/DefaultPoint"));
            computeShader = (ComputeShader)Resources.Load("FilterPoints");
        }

        public void Reset() {
            dispatcher = new Dispatcher();
            computeShader = (ComputeShader)Resources.Load("FilterPoints");
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
                if (!data.DecimatedGenerated && !awaitingDecimated) {
                    data.PopulateSparseCloud(decimatedCloudSize).ContinueWith((Task t) => { awaitingDecimated = false; });
                    awaitingDecimated = true;
                }

                if (awaitingDecimated)
                    return;

                if (generateTree && !data.TreeGenerated) {
                    treeThread = new Thread(new ParameterizedThreadStart(BuildTreeThread));
                    Debug.Log("Starting octree thread");
                    treeThread.Start(new BuildTreeParams(tree, data, dispatcher, () => {
                        data.TreePath = tree.dirPath;
                    }));
                }

                
            }

            if (data.TreeGenerated && !readerRunning) {
                Debug.Log("init reader");
                if (!tree.Loaded) {
                    tree.LoadTree();
                    firstFrame = true;
                    // tree.VerifyTree();
                }

                // int pointBudget = 10000000;
                pointBuffer = new ComputeBuffer(pointBudget, Point.size);
                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", pointBuffer);
                mat.SetFloat("_PointSize", pointSize);

                Camera c = (!cam) ? Camera.current ?? Camera.main : cam;
                reader = new(tree, this, c);
                readerRunning = true;
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

        public void OnDrawGizmosSelected() {
            if (data && data.TreeGenerated && showBBoxs) {
                Gizmos.matrix = transform.localToWorldMatrix;
                List<(string, AABB)> visibleBBs = reader.GetVisibleBBs();
                if (visibleBBs == null)
                    return;

                foreach ((string, AABB) pair in visibleBBs) {
                    if (highlightedNode != "") {
                        if (highlightedNode.Contains(pair.Item1) && pair.Item1.Length <= highlightedNode.Length) {
                            if (pair.Item1 == highlightedNode)
                                Handles.Label(transform.TransformPoint(pair.Item2.Center), pair.Item1);
                            Gizmos.DrawWireCube( pair.Item2.Center, pair.Item2.Size );
                        }
                    } else {
                        Handles.Label(transform.TransformPoint(pair.Item2.Center), pair.Item1);
                        Gizmos.DrawWireCube( pair.Item2.Center, pair.Item2.Size );
                    }
                }
            }
        }

        public void OnRenderObject() {
            if (data == null)
                return;

            if (data.TreeGenerated) {
                List<Point> loadedPoints = new();
                Camera c = (!cam || useComputeShader) ? Camera.current ?? Camera.main : cam;
                Debug.Log($"Camera {c.name}, gameObject {c.gameObject}");
                if (useComputeShader) {
                    if (!c.gameObject.GetComponent<PointCloudCamera>()) {
                        c.gameObject.AddComponent<PointCloudCamera>();
                        // c.gameObject.GetComponent<PointCloudCamera>().Init();
                    }
                } else if (c.gameObject.GetComponent<PointCloudCamera>()) {
                    UnityEngine.Object.DestroyImmediate(c.gameObject.GetComponent<PointCloudCamera>());
                }
                
                if (useNodesToShow) {   // If nodes to show enabled, render all and only nodes in list
                    reader.SetNodesToShow(nodesToShow);
                    loadedPoints = reader.GetLoadedPoints();
                } else {
                    reader.SetNodesToShow(null);
                    reader.SetCamera(c);
                    
                    // Create new point buffer
                    loadedPoints = reader.GetLoadedPoints();   
                    if (debugReader) {
                        reader.SetDebug();
                        debugReader = false;
                    }      

                    if (maxOctreeRenderDepth != reader.traversalDepth)
                        reader.SetTraversalDepth(maxOctreeRenderDepth);

                    if (pointBudget != reader.pointBudget)
                        reader.SetPointBudget(pointBudget);
                    
                    if (loadedPoints.Count == 0)
                        return;
                }

                Debug.Log($"Rendering {loadedPoints.Count} points");
                if (useComputeShader)
                    c.GetComponent<PointCloudCamera>().AddCloud(transform, loadedPoints);
                else {
                    ComputeBuffer cb = new ComputeBuffer(loadedPoints.Count, Point.size);
                    cb.SetData(loadedPoints);

                    mat.hideFlags = HideFlags.DontSave;
                    mat.SetBuffer("_PointBuffer", cb);
                    mat.SetFloat("_PointSize", pointSize);
                    mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                    mat.SetPass(0);

                    Graphics.DrawProceduralNow(MeshTopology.Points, loadedPoints.Count, 1);

                    cb.Dispose();
                }

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
    }
}