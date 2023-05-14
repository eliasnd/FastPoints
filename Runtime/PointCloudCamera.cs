// FastPoints
// Copyright (C) 2023  Elias Neuman-Donihue

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints
{
    // [ExecuteInEditMode]
    public class PointCloudCamera : MonoBehaviour
    {
        Camera c;
        ComputeShader computeShader;
        List<(RenderTexture colTex, RenderTexture depthTex)> clouds;
        int oldScreenWidth = 0;
        int oldScreenHeight = 0;
        Material mat;
        Matrix4x4 vp;

        public Matrix4x4 MVP { get { return vp * transform.localToWorldMatrix; } }

        public void Start()
        {
            c = gameObject.GetComponent<Camera>();
            c.depthTextureMode = c.depthTextureMode | DepthTextureMode.Depth;

            vp = GL.GetGPUProjectionMatrix(c.projectionMatrix, false) * c.worldToCameraMatrix;

            computeShader = (ComputeShader)Resources.Load("FilterPoints");
            mat = new Material(Shader.Find("Custom/FilteredPoints"));

            computeShader.SetInt("_ScreenWidth", Screen.width);
            computeShader.SetInt("_ScreenHeight", Screen.height);

            clouds = new List<(RenderTexture, RenderTexture)>();
        }

        void Update()
        {
            if (!computeShader)
                return;

            if (Screen.width != oldScreenHeight || Screen.height != oldScreenHeight)
            {
                computeShader.SetInt("_ScreenWidth", Screen.width);
                computeShader.SetInt("_ScreenHeight", Screen.height);
            }

            vp = GL.GetGPUProjectionMatrix(c.projectionMatrix, false) * c.worldToCameraMatrix;
        }

        public void AddCloud(RenderTexture colTex, RenderTexture depthTex)
        {
            clouds.Add((colTex, depthTex));
        }

        // void OnRenderImage(RenderTexture source, RenderTexture target)
        // {
        //     if (!computeShader)
        //         return;

        //     RenderTexture tex1 = new RenderTexture(source);
        //     tex1.Create();
        //     RenderTexture tex2 = new RenderTexture(source.width, source.height, 0);
        //     tex2.Create();

        //     RenderTexture curr = tex1;
        //     RenderTexture next = tex2;
        //     foreach ((RenderTexture colorTex, RenderTexture depthTex) cloud in clouds)
        //     {
        //         mat.SetTexture("_CloudTex", cloud.colorTex);
        //         mat.SetTexture("_CloudDepthTexture", cloud.depthTex);

        //         Graphics.Blit(source, target);
        //         return;

        //         Graphics.Blit(curr, next);

        //         RenderTexture tmp = curr;
        //         curr = next;
        //         next = tmp;

        //         cloud.colorTex.Release();
        //         cloud.depthTex.Release();
        //     }

        //     Graphics.CopyTexture(curr, target);
        // }
    }
}