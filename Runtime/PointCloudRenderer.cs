using UnityEngine;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FastPoints {
    
    [ExecuteInEditMode]
    public sealed class PointCloudRenderer : MonoBehaviour {

        public PointCloudData data;

        public void OnRenderObject() {
            if (data == null)
                return;

            if (data.TreeGenerated) {
                throw new NotImplementedException();
            } else if (data.DecimatedGenerated) {

                ComputeBuffer cb = new ComputeBuffer(data.decimatedCloud.Length, Marshal.SizeOf(new Point(0, 0, 0, 0)));
                cb.SetData(data.decimatedCloud);

                Material mat = new Material(Shader.Find("Custom/DefaultPoint"));
                mat.hideFlags = HideFlags.DontSave;
                mat.SetBuffer("_PointBuffer", cb);
                mat.SetPass(0);
                mat.SetMatrix("_Transform", transform.localToWorldMatrix);

                Graphics.DrawProceduralNow(MeshTopology.Points, data.PointCount, 1);

                cb.Dispose();
            }
        }
    }

}