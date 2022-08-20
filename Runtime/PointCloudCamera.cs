using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
        Matrix4x4 vp;
        public bool runOnce;

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
            // GameObject.FindGameObjectWithTag("col").GetComponent<Renderer>().material.SetTexture("_MainTex", colorTex);
            computeShader.SetTexture(computeShader.FindKernel("FilterPoints"), "_OutCol", colorTex);
            
            depthTex = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.R8);
            depthTex.enableRandomWrite = true;
            depthTex.Create();
            // GameObject.FindGameObjectWithTag("depth").GetComponent<Renderer>().material.SetTexture("_MainTex", depthTex);
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

            int threadBudget = Mathf.CeilToInt(points.Count / 65535f);    // Debug
            Matrix4x4 mvp = vp * transform.localToWorldMatrix;

            Debug.Log($"AddCloud called, matrix is {mvp.ToString()}");

            ComputeBuffer pointBuffer = new ComputeBuffer(points.Count, Point.size);
            pointBuffer.SetData(points);

            int[] lockArr = new int[Screen.width * Screen.height];
            ComputeBuffer lockBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(int));
            lockBuffer.SetData(lockArr);

            int debugCount = 6;
            int[] debugArr = new int[debugCount];
            ComputeBuffer debugBuffer = new ComputeBuffer(debugCount, sizeof(int));
            debugBuffer.SetData(debugArr);

            float[] floatArr = new float[Screen.width * Screen.height];
            ComputeBuffer floatBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(float));
            floatBuffer.SetData(floatArr);

            int[] intArr = new int[Screen.width * Screen.height];
            ComputeBuffer intBuffer = new ComputeBuffer(Screen.width * Screen.height, sizeof(int));
            intBuffer.SetData(intArr);
                
            int kernel = computeShader.FindKernel("FilterPoints");
            computeShader.SetInt("_ThreadBudget", threadBudget);
            computeShader.SetInt("_PointCount", points.Count);
            computeShader.SetMatrix("_MVP", mvp);
            
            computeShader.SetBuffer(kernel, "_Points", pointBuffer);
            computeShader.SetBuffer(kernel, "_Locks", lockBuffer);
            computeShader.SetBuffer(kernel, "_DebugBuffer", debugBuffer);
            computeShader.SetBuffer(kernel, "_PixelInts", intBuffer);
            computeShader.SetBuffer(kernel, "_PixelFloats", floatBuffer);

            // DEBUG - check for texture stability
            if (runOnce) {
                Texture2D oldTex = new Texture2D(colorTex.width, colorTex.height);
                RenderTexture.active = colorTex;
                oldTex.ReadPixels(new Rect(0, 0, colorTex.width, colorTex.height), 0, 0);
                oldTex.Apply();

                RenderTexture.active = null;

                Stopwatch watch = new Stopwatch();
                watch.Start();
                computeShader.Dispatch(kernel, 65535, 1, 1);
                watch.Stop();
                Debug.Log($"Compute shader took {watch.ElapsedMilliseconds} ms");

                Texture2D newTex = new Texture2D(depthTex.width, depthTex.height);
                RenderTexture.active = depthTex;
                newTex.ReadPixels(new Rect(0, 0, depthTex.width, depthTex.height), 0, 0);
                newTex.Apply();

                float[] depths = newTex.GetPixels().Select(x => x.r).ToArray();
                Debug.Log($"Max depth is {depths.Max()}, min depth is {depths.Min()}");

                RenderTexture.active = null;

                bool diff = false;
                for (int x = 0; x < colorTex.width && !diff; x++)
                    for (int y = 0; y < colorTex.width && !diff; y++) {
                        diff |= oldTex.GetPixel(x, y) != newTex.GetPixel(x, y);
                    }

                Debug.Log(diff ? "Textures different!" : "Textures same");
                runOnce = false;
            } else {
                Stopwatch watch = new Stopwatch();
                watch.Start();
                computeShader.Dispatch(kernel, 65535, 1, 1);
                watch.Stop();
                Debug.Log($"Compute shader took {watch.ElapsedMilliseconds} ms");

                // debugBuffer.GetData(debugArr);
                // intBuffer.GetData(intArr);
                // floatBuffer.GetData(floatArr);

                // int maxInt = intArr.Max();

            }   

            // GameObject.FindGameObjectWithTag("col").GetComponent<Renderer>().material.SetTexture("_MainTex", colorTex);
            // GameObject.FindGameObjectWithTag("depth").GetComponent<Renderer>().material.SetTexture("_MainTex", depthTex);

            lockBuffer.Dispose();
            pointBuffer.Dispose();
            intBuffer.Dispose();
            floatBuffer.Dispose();
            debugBuffer.Dispose();
        }

        void OnRenderImage(RenderTexture source, RenderTexture target) {
            if (!computeShader)
                return;
            Debug.Log("OnRenderImage");

            mat.SetTexture("_CloudTex", colorTex);
            mat.SetTexture("_CloudDepthTexture", depthTex);

            Graphics.Blit(source, target, mat);
            // colorTex.Release();
            // depthTex.Release();
        }
    }
}