using UnityEngine;
using UnityEditor;
using Unity.Collections;

using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    // [ExecuteInEditMode]
    public class PointCloudLoader : MonoBehaviour {

        #region public
        public PointCloudHandle handle;
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

        bool decimatedGenerated = false;
        NativeArray<Vector3> decimatedPoints;
        NativeArray<Color32> decimatedColors;
        ComputeBuffer decimatedPosBuffer;
        ComputeBuffer decimatedColBuffer;

        int actionsPerFrame = 2;

        Dispatcher dispatcher;
        ComputeShader computeShader;
        PointCloudHandle oldHandle = null;

        #region rendering
        Thread readThread;
        ComputeBuffer pointBuffer;
        Material mat;
        #endregion

        bool treeGeneratedOld = false;
        bool awaitingDecimated = false;
        string fullInPath;
        string fullOutPath;

        bool firstFrame;

        public void Awake() {
            dispatcher = new Dispatcher();
            mat = new Material(Shader.Find("Custom/DefaultPoint"));
        }

        public void Reset() {
            dispatcher = new Dispatcher();
            oldHandle = null;
        }

        public void Update() {
            if (handle != oldHandle) {  // If new data put in
                Debug.Log("Starting...");
                fullInPath = System.IO.Directory.GetCurrentDirectory() + "/" + handle.path;
                fullOutPath = System.IO.Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(handle.path);
                ProcessPointCloud();
            }

            for (int i = 0; i < actionsPerFrame; i++) {
                Action a;
                if (dispatcher.TryDequeue(out a))
                    a();
            }

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

        public async Task ProcessPointCloud() {
            string ext = Path.GetExtension(handle.path);

            decimatedPoints = new NativeArray<Vector3>(decimatedCloudSize, Allocator.Persistent);
            decimatedColors = new NativeArray<Color32>(decimatedCloudSize, Allocator.Persistent);

            await Task.Run(() => {
                Debug.Log("Starting task " + fullInPath + ", " + fullOutPath);
                FastPointsNativeApi.PopulateDecimatedCloud(
                    fullInPath, 
                    decimatedPoints, 
                    decimatedColors, 
                    decimatedCloudSize
                );

                dispatcher.Enqueue(() => {
                    decimatedPosBuffer = new ComputeBuffer(decimatedCloudSize, 12);
                    decimatedPosBuffer.SetData(decimatedPoints);
                    decimatedColBuffer = new ComputeBuffer(decimatedCloudSize, 4);
                    decimatedColBuffer.SetData(decimatedColors);
                    decimatedGenerated = true;

                    decimatedPoints.Dispose();
                    decimatedColors.Dispose();
                    Debug.Log("Decimated point cloud generated");
                });
            }).ContinueWith(_ => {
                FastPointsNativeApi.RunConverter(fullInPath, fullOutPath);

                Debug.Log("Finished conversion");
            });        
        }

        public void OnRenderObject() {
            if (handle == null)
                return;

            if (decimatedGenerated) {
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_Positions", decimatedPosBuffer);
                mat.SetBuffer("_Colors", decimatedColBuffer);
                mat.SetFloat("_PointSize", pointSize);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
            }

            // if (data.TreeGenerated) {
            //     List<Point> loadedPoints = new();
            //     Camera c = (!cam || useComputeShader) ? Camera.current ?? Camera.main : cam;
            //     Debug.Log($"Camera {c.name}, gameObject {c.gameObject}");
            //     if (useComputeShader) {
            //         if (!c.gameObject.GetComponent<PointCloudCamera>()) {
            //             c.gameObject.AddComponent<PointCloudCamera>();
            //             // c.gameObject.GetComponent<PointCloudCamera>().Init();
            //         }
            //     } else if (c.gameObject.GetComponent<PointCloudCamera>()) {
            //         UnityEngine.Object.DestroyImmediate(c.gameObject.GetComponent<PointCloudCamera>());
            //     }
                
            //     if (useNodesToShow) {   // If nodes to show enabled, render all and only nodes in list
            //         reader.SetNodesToShow(nodesToShow);
            //         loadedPoints = reader.GetLoadedPoints();
            //     } else {
            //         reader.SetNodesToShow(null);
            //         reader.SetCamera(c);
                    
            //         // Create new point buffer
            //         loadedPoints = reader.GetLoadedPoints();   
            //         if (debugReader) {
            //             reader.SetDebug();
            //             debugReader = false;
            //         }      

            //         if (maxOctreeRenderDepth != reader.traversalDepth)
            //             reader.SetTraversalDepth(maxOctreeRenderDepth);

            //         if (pointBudget != reader.pointBudget)
            //             reader.SetPointBudget(pointBudget);
                    
            //         if (loadedPoints.Count == 0)
            //             return;
            //     }

            //     Debug.Log($"Rendering {loadedPoints.Count} points");
            //     if (useComputeShader) {
            //         c.GetComponent<PointCloudCamera>().AddCloud(transform, loadedPoints);
            //         if (runOnce) {
            //             c.GetComponent<PointCloudCamera>().runOnce = true;
            //             runOnce = false;
            //         }
            //     } else {
            //         ComputeBuffer cb = new ComputeBuffer(loadedPoints.Count, Point.size);
            //         cb.SetData(loadedPoints);

            //         mat.hideFlags = HideFlags.DontSave;
            //         mat.SetBuffer("_PointBuffer", cb);
            //         mat.SetFloat("_PointSize", pointSize);
            //         mat.SetMatrix("_Transform", transform.localToWorldMatrix);
            //         mat.SetPass(0);

            //         Graphics.DrawProceduralNow(MeshTopology.Points, loadedPoints.Count, 1);

            //         cb.Dispose();
            //     }

            // } else if (data.DecimatedGenerated) {
            //     // Debug.Log($"Point size: {System.Runtime.InteropServices.Marshal.SizeOf<Vector3>()} + {System.Runtime.InteropServices.Marshal.SizeOf<Color>()}");
            //     ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, Point.size);
            //     cb.SetData(data.decimatedCloud);

            //     mat.hideFlags = HideFlags.DontSave;
            //     mat.SetBuffer("_PointBuffer", cb);
            //     mat.SetFloat("_PointSize", pointSize);
            //     mat.SetMatrix("_Transform", transform.localToWorldMatrix);
            //     mat.SetPass(0);

            //     Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);

            //     cb.Dispose();
            // }
        }
    }
}