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

                // readThread = new Thread(new ParameterizedThreadStart(ReadTreeThread));
                // readThread.Start(new ReadTreeParams(tree, cam.projectionMatrix * cam.worldToCameraMatrix, pointBuffer, dispatcher, () => {}));
                Camera c = (!cam) ? Camera.current ?? Camera.main : cam;
                reader = new(tree, this, c);
                readerRunning = true;
            }

            for (int i = 0; i < actionsPerFrame; i++) {
                // if (data.TreeGenerated && readerRunning)
                //     reader.SetNodesToShow(toShow);
                // if (dispatcher.Count > 0) {
                //     Action action;
                //     dispatcher.TryDequeue(out action);
                //     action();
                // }
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
                Camera c = (!cam) ? Camera.current ?? Camera.main : cam;
                
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

                Matrix4x4 mvp = GL.GetGPUProjectionMatrix(c.projectionMatrix, true) * c.worldToCameraMatrix * transform.localToWorldMatrix;

                // if (runOnce) {
                //     try {
                //         List<Point> clipPoints = loadedPoints.FindAll(p => {
                //             Vector4 clipPos = mvp * new Vector4(p.pos.x, p.pos.y, p.pos.z, 1);
                //             Vector3 ndc = new Vector3(clipPos.x / clipPos.w, clipPos.y / clipPos.w, clipPos.z / clipPos.w);
                //             return !(ndc.x <= -1 || ndc.x >= 1 || ndc.y <= -1 || ndc.y >= 1 || ndc.z <= 0 || ndc.z > 1);
                //         }).ToList();
                //         runOnce = false;
                //     } catch (Exception e) {
                //         Debug.LogError($"Error {e.Message}");
                //     }
                // }

                ComputeBuffer pointBuffer = new ComputeBuffer(loadedPoints.Count, Point.size);
                pointBuffer.SetData(loadedPoints);

                ComputeBuffer lockBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(int));
                int[] lockArr = new int[Screen.width * Screen.height];
                lockBuffer.SetData(lockArr);

                ComputeBuffer colorBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(Int16) * 4);
                ComputeBuffer posBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(float) * 4);

                // int threadBudget = Mathf.CeilToInt(loadedPoints.Count / 65536f);    // 256 * 256 threads
                int threadBudget = Mathf.CeilToInt(loadedPoints.Count / 65535f);    // Debug

                int debugCount = 6;
                int[] debugArr = new int[debugCount];
                ComputeBuffer debugBuffer = new ComputeBuffer(debugCount, sizeof(int));
                debugBuffer.SetData(debugArr);

                int kernel = computeShader.FindKernel("FilterPoints");
                computeShader.SetInt("_ThreadBudget", threadBudget);
                computeShader.SetInt("_PointCount", loadedPoints.Count);
                computeShader.SetMatrix("_Transform", transform.localToWorldMatrix);
                computeShader.SetMatrix("_MVP", mvp);
                computeShader.SetInt("_ScreenWidth", Screen.width);
                computeShader.SetInt("_ScreenHeight", Screen.height);
                computeShader.SetBuffer(kernel, "_Points", pointBuffer);
                computeShader.SetBuffer(kernel, "_Locks", lockBuffer);
                computeShader.SetBuffer(kernel, "_OutCol", colorBuffer);
                computeShader.SetBuffer(kernel, "_OutPos", posBuffer);
                computeShader.SetBuffer(kernel, "_DebugBuffer", debugBuffer);
                
                // if (runOnce) {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();
                    // computeShader.Dispatch(kernel, 256, 1, 1);
                    computeShader.Dispatch(kernel, 65535, 1, 1);
                    watch.Stop();
                    Debug.Log($"Compute shader took {watch.ElapsedMilliseconds} ms");
                    runOnce = false;
                    debugBuffer.GetData(debugArr);
                    Vector4[] debugPosArr = new Vector4[Screen.width * Screen.height];
                    posBuffer.GetData(debugPosArr);
                // }
                
                mat = new Material(Shader.Find("Custom/FilteredPoints"));
                mat.hideFlags = HideFlags.DontSave;
                // mat.SetBuffer("_PointBuffer", pointBuffer);
                mat.SetInt("_ScreenWidth", Screen.width);
                mat.SetBuffer("_Colors", colorBuffer);
                mat.SetBuffer("_Positions", posBuffer);
                mat.SetFloat("_PointSize", pointSize);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, Screen.width * Screen.height, 1);

                pointBuffer.Dispose();
                lockBuffer.Dispose();
                colorBuffer.Dispose();
                posBuffer.Dispose();

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