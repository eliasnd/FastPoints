using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    [ExecuteInEditMode]
    public class PointCloudCamera : MonoBehaviour {
        Camera c;
        ComputeShader computeShader;
        PointCloudLoader[] clouds;
        RenderTexture colorTex;
        RenderTexture depthTex;
        int oldScreenWidth = 0;
        int oldScreenHeight = 0;
        Material mat;
        Matrix vp;

        public void Start() {
            Debug.Log("Start");
            c = gameObject.GetComponent<Camera>();
            c.depthTextureMode = c.depthTextureMode | DepthTextureMode.Depth;

            computeShader = (ComputeShader)Resources.Load("FilterPoints");    
            mat = new Material(Shader.Find("Custom/FilteredPoints"));

            computeShader.SetInt("_ScreenWidth", Screen.width);
            computeShader.SetInt("_ScreenHeight", Screen.height);

            colorTex = new RenderTexture(Screen.width, Screen.height, 32);
            colorTex.enableRandomWrite = true;
            colorTex.Create();
            GameObject.FindGameObjectWithTag("col").GetComponent<Renderer>().material.SetTexture("_MainTex", colorTex);
            computeShader.SetTexture(computeShader.FindKernel("FilterPoints"), "_OutCol", colorTex);
            
            depthTex = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.R8);
            depthTex.enableRandomWrite = true;
            depthTex.Create();
            GameObject.FindGameObjectWithTag("depth").GetComponent<Renderer>().material.SetTexture("_MainTex", depthTex);
            computeShader.SetTexture(computeShader.FindKernel("FilterPoints"), "_OutDepth", depthTex);
        }

        void Update() {
            if (!computeShader)
                return;

            if (Screen.width != oldScreenHeight || Screen.height != oldScreenHeight) {
                computeShader.SetInt("_ScreenWidth", Screen.width);
                computeShader.SetInt("_ScreenHeight", Screen.height);

                colorTex = new RenderTexture(Screen.width, Screen.height, 32);
                colorTex.enableRandomWrite = true;
                computeShader.SetTexture(computeShader.FindKernel("FilterPoints"), "_OutCol", colorTex);
                
                depthTex = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.R8);
                depthTex.enableRandomWrite = true;
                computeShader.SetTexture(computeShader.FindKernel("FilterPoints"), "_OutDepth", depthTex);
            }

            vp = GL.GetGPUProjectionMatrix(c.projectionMatrix, false) * c.worldToCameraMatrix;
        }

        // Run compute shader to add cloud to frame
        public void AddCloud(Transform transform, List<Point> points) {
            if (!computeShader)
                return;

            // DEBUG: Try FillWithRed
            Debug.Log("AddCloud");
            // computeShader = (ComputeShader)Resources.Load("FillWithRed");    
            // computeShader.SetTexture(computeShader.FindKernel("FillWithRed"), "res", colorTex);
            // computeShader.Dispatch(computeShader.FindKernel("FillWithRed"), Screen.width, Screen.height, 1);

            // GameObject.FindGameObjectWithTag("col").GetComponent<Renderer>().material.SetTexture("_MainTex", colorTex);

            // computeShader = (ComputeShader)Resources.Load("FilterPoints");    

            // return;

            int threadBudget = Mathf.CeilToInt(points.Count / 65535f);    // Debug
            Matrix4x4 mvp = vp * transform.localToWorldMatrix;

            ComputeBuffer pointBuffer = new ComputeBuffer(points.Count, Point.size);
            pointBuffer.SetData(points);

            int[] lockArr = new int[Screen.width * Screen.height];
            ComputeBuffer lockBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(int));
            lockBuffer.SetData(lockArr);

            int debugCount = 6;
            int[] debugArr = new int[debugCount];
            ComputeBuffer debugBuffer = new ComputeBuffer(debugCount, sizeof(int));
            debugBuffer.SetData(debugArr);
                
            int kernel = computeShader.FindKernel("FilterPoints");
            computeShader.SetInt("_ThreadBudget", threadBudget);
            computeShader.SetInt("_PointCount", points.Count);
            computeShader.SetMatrix("_MVP", mvp);
            
            computeShader.SetBuffer(kernel, "_Points", pointBuffer);
            computeShader.SetBuffer(kernel, "_Locks", lockBuffer);
            computeShader.SetBuffer(kernel, "_DebugBuffer", debugBuffer);

            Stopwatch watch = new Stopwatch();
            watch.Start();
            computeShader.Dispatch(kernel, 65535, 1, 1);
            watch.Stop();
            Debug.Log($"Compute shader took {watch.ElapsedMilliseconds} ms");

            GameObject.FindGameObjectWithTag("col").GetComponent<Renderer>().material.SetTexture("_MainTex", colorTex);
            GameObject.FindGameObjectWithTag("depth").GetComponent<Renderer>().material.SetTexture("_MainTex", depthTex);

            lockBuffer.Dispose();
            pointBuffer.Dispose();
            debugBuffer.Dispose();
        }

        void OnRenderImage(RenderTexture source, RenderTexture target) {
            if (!computeShader)
                return;
            Debug.Log("OnRenderImage");

            mat.SetTexture("_CloudTex", colorTex);
            mat.SetTexture("_CloudDepthTexture", depthTex);

            Graphics.Blit(colorTex, target);
            // colorTex.Release();
            // depthTex.Release();
        }
    }
}