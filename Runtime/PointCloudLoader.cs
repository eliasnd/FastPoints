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

        public static bool debug = false;

        #region public
        public PointCloudHandle handle;
        public int decimatedCloudSize = 1000000;
        public float pointSize = 1.5f;
        public Camera cam = null;
        public int pointBudget = 1000000;
        public bool drawGizmos = true;
        #endregion

        bool decimatedGenerated = false;
        NativeArray<Vector3> decimatedPoints;
        NativeArray<Color32> decimatedColors;
        ComputeBuffer decimatedPosBuffer;
        ComputeBuffer decimatedColBuffer;

        ComputeBuffer testPos;
        ComputeBuffer testCol;

        OctreeGeometry geom;

        int actionsPerFrame = 2;

        bool octreeGenerated = true;

        Dispatcher dispatcher;
        ComputeShader computeShader;
        PointCloudHandle oldHandle = null;
        Traverser t;

        public PathCamera pathCam;

        bool createdBuffers = false;

        List<AABB> boxesToDraw = null;

        #region rendering
        Material mat;
        #endregion

        Queue<OctreeGeometry> nodesToRender;
        object queueLock;

        string fullInPath;
        string fullOutPath;

        bool firstFrame;

        public void Awake() {
            dispatcher = new Dispatcher();
            mat = new Material(Shader.Find("Custom/DefaultPoint"));
            queueLock = new object();
        }

        public void Reset() {
            dispatcher = new Dispatcher();
            oldHandle = null;
        }

        public void Update() {
            if (handle != oldHandle) {  // If new data put in
                Debug.Log("Starting...");
                // pathCam.ResetFPS();
                if (!handle.Converted)
                {
                    fullInPath = System.IO.Directory.GetCurrentDirectory() + "/" + handle.path;
                    fullOutPath = System.IO.Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(handle.path);
                    ProcessPointCloud();
                } else
                {
                    fullOutPath = System.IO.Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(handle.path);
                    geom = OctreeLoader.Load(fullOutPath + "/metadata.json", dispatcher);

                    t = new Traverser(geom, geom.loader, this, pointBudget, dispatcher);
                    t.Start();
                    t.SetCamera(cam ?? Camera.current ?? Camera.main, transform.localToWorldMatrix);
                }
                //ProcessPointCloud();
            }

            //if (geom != null && geom.root.loaded ) {
            //    if (createdBuffers)
            //    {
            //        mat.hideFlags = HideFlags.DontSave;
            //        mat.SetBuffer("_Positions", testPos);
            //        mat.SetBuffer("_Colors", testCol);
            //        mat.SetFloat("_PointSize", pointSize);
            //        mat.SetMatrix("_Transform", transform.localToWorldMatrix);
            //        mat.SetPass(0);

            //        Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
            //    }
            //    else
            //    {
            //        testPos = new ComputeBuffer(geom.positions.Length, 12);
            //        testPos.SetData(geom.positions);

            //        testCol = new ComputeBuffer(geom.colors.Length, 4);
            //        testCol.SetData(geom.colors);

            //        createdBuffers = true;
            //    }
            //}

            for (int i = 0; i < actionsPerFrame; i++) {
                Action a;
                if (dispatcher.TryDequeue(out a))
                    a();
            }

            if (handle.Converted) {
                t.SetCamera(cam ?? Camera.current ?? Camera.main, transform.localToWorldMatrix);
                // Main rendering loop
            }

            oldHandle = handle;
        }

        public Queue<OctreeGeometry> SetQueue(Queue<OctreeGeometry> queue) {
            lock (queueLock) {
                Queue<OctreeGeometry> oldQueue = nodesToRender;
                nodesToRender = new Queue<OctreeGeometry>(queue);
                return oldQueue;
            }
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
            if (!drawGizmos)
                return;

            if (cam) {
                Gizmos.matrix = cam.transform.localToWorldMatrix;
                Gizmos.DrawFrustum( Vector3.zero, cam.fieldOfView, cam.farClipPlane, cam.nearClipPlane, cam.aspect );
            }

            if (boxesToDraw != null) {
                Gizmos.matrix = transform.localToWorldMatrix;
                foreach (AABB bbox in boxesToDraw) {
                    Gizmos.DrawWireCube( bbox.Center, bbox.Size );
                }
            }
        }


        public async Task ProcessPointCloud() {
            string ext = Path.GetExtension(handle.path);

            decimatedPoints = new NativeArray<Vector3>(decimatedCloudSize, Allocator.Persistent);
            decimatedColors = new NativeArray<Color32>(decimatedCloudSize, Allocator.Persistent);

            await Task.Run(() => {
                Debug.Log("Starting task " + fullInPath + ", " + fullOutPath);
                DateTime startTime = DateTime.Now;
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

                    DateTime endTime = DateTime.Now;
                    Double elapsedMillisecs = ((TimeSpan)(endTime - startTime)).TotalMilliseconds;
                    Debug.Log("Generated decimated cloud in " + elapsedMillisecs + " ms");
                });
            }).ContinueWith(_ => {
                FastPointsNativeApi.RunConverter(fullInPath, fullOutPath);

                dispatcher.Enqueue(() =>
                {
                    nodesToRender = new Queue<OctreeGeometry>();
                    handle.Converted = true;

                    // pathCam.LogFPS();
                    geom = OctreeLoader.Load(fullOutPath + "/metadata.json", dispatcher);

                    t = new Traverser(geom, geom.loader, this, pointBudget, dispatcher);
                    t.Start();
                    t.SetCamera(Camera.current ?? Camera.main, transform.localToWorldMatrix);
                });

                Debug.Log("Finished conversion");
            });        
        }

        public void RenderNode(OctreeGeometry geom) {
            Material m = new Material(Shader.Find("Custom/DefaultPoint"));;
            m.hideFlags = HideFlags.DontSave;

            if (geom.posBuffer == null) // In case disposed during queue traversal
                return;

            boxesToDraw.Add(geom.boundingBox);

            m.SetVector("_Offset", geom.boundingBox.Min);
            // m.SetVector("_Offset", new Vector3( 2f, 0f, 0f ));
            m.SetBuffer("_Positions", geom.posBuffer);
            m.SetBuffer("_Colors", geom.colBuffer);
            m.SetFloat("_PointSize", pointSize);
            m.SetMatrix("_Transform", transform.localToWorldMatrix);
            m.SetPass(0);

            Graphics.DrawProceduralNow(MeshTopology.Points, geom.positions.Length, 1);
        }

        public void OnRenderObject() {
            if (handle == null || nodesToRender == null)
                return;

            if (handle.Converted)
            {
                if (boxesToDraw != null)
                    boxesToDraw.Clear();
                else
                    boxesToDraw = new List<AABB>();

                Queue<OctreeGeometry> frameQueue;
                lock (queueLock)
                {
                    frameQueue = new Queue<OctreeGeometry>(nodesToRender);
                }

                if (frameQueue != null)
                    while (frameQueue.Count > 0)
                    //if (frameQueue.Count > 0)
                        RenderNode(frameQueue.Dequeue());
            } else if (decimatedGenerated) {
                boxesToDraw = null;

                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_Positions", decimatedPosBuffer);
                mat.SetBuffer("_Colors", decimatedColBuffer);
                mat.SetFloat("_PointSize", pointSize);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                mat.SetPass(0);

                Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
            } else
                boxesToDraw = null;

            //if (octreeGenerated && geom.root.loaded)
            //     RenderNode(geom);

                //if (octreeGenerated && geom.root.loaded)
                //{
                //    mat.hideFlags = HideFlags.DontSave;

                //    mat.SetBuffer("_Positions", testPos);
                //    mat.SetBuffer("_Colors", testCol);
                //    mat.SetFloat("_PointSize", pointSize);
                //    mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                //    mat.SetPass(0);

                //    Graphics.DrawProceduralNow(MeshTopology.Points, geom.positions.Length, 1);
                //}

                // if (createdBuffers) {
                //     mat.hideFlags = HideFlags.DontSave;
                //     mat.SetBuffer("_Positions", testPos);
                //     mat.SetBuffer("_Colors", testCol);
                //     mat.SetFloat("_PointSize", pointSize);
                //     mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                //     mat.SetPass(0);

                //     Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
                // }


                // if (!octreeGenerated && decimatedGenerated) {
                //     mat.hideFlags = HideFlags.DontSave;
                //     mat.SetBuffer("_Positions", decimatedPosBuffer);
                //     mat.SetBuffer("_Colors", decimatedColBuffer);
                //     mat.SetFloat("_PointSize", pointSize);
                //     mat.SetMatrix("_Transform", transform.localToWorldMatrix);
                //     mat.SetPass(0);

                //     Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
                // }
        }
    }
}