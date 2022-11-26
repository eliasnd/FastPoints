using UnityEngine;
using System;
using System.Numerics;

using Vector3 = UnityEngine.Vector3;

namespace FastPoints
{

    public class OctreeGeometry
    {
        public string path;
        public float[] scale;
        public float spacing;
        public AABB boundingBox;
        public OctreeGeometryNode root;
        public PointAttributes pointAttributes;
        public NodeLoader loader;
        public string projection;
        public Vector3 offset;
        public ComputeBuffer posBuffer;
        public ComputeBuffer colBuffer;

        public void Dispose()
        {
            if (posBuffer != null)
            {
                posBuffer.Release();
                colBuffer.Release();
            }
        }
    }

    public class OctreeGeometryNode
    {

        static int IDCount = 0;

        public OctreeGeometryNode parent;

        public int id;
        public string name;
        public int index;
        public OctreeGeometry octreeGeometry;
        public AABB boundingBox;
        public int level;
        public UInt32 numPoints;
        public int nodeType;
        public OctreeGeometryNode[] children;
        public BigInteger hierarchyByteOffset;
        public BigInteger hierarchyByteSize;
        public bool hasChildren;
        public float spacing;
        public Int64 byteOffset;
        public Int64 byteSize;

        public bool loading;
        public bool loaded;


        public OctreeGeometryNode(string name, OctreeGeometry octreeGeometry, AABB boundingBox)
        {
            this.id = IDCount++;
            this.name = name;
            this.index = (int)Char.GetNumericValue(name[name.Length - 1]);
            this.octreeGeometry = octreeGeometry;
            this.boundingBox = boundingBox;
            // this.boundingSphere = boundingBox.getBoundingSphere(new THREE.Sphere());
            this.children = new OctreeGeometryNode[8];
            this.numPoints = 0;
            this.level = -1;
        }

        public void Load()
        {
            try
            {
                //if (NodeLoader.numNodesLoading >= NodeLoader.maxNodesLoading) {
                //    return;
                //}

                octreeGeometry.loader.Load(this);
            }
            catch (Exception e)
            {
                Debug.LogError($"Got error message: {e.Message}, backtrace: {e.ToString()}");
            }
        }

        public bool Dispose()
        {
            if (this.loaded && this.octreeGeometry != null && this.parent != null)
            {
                NodeLoader.numNodesLoaded--;
                this.octreeGeometry.Dispose();
                this.loaded = false;
                if (PointCloudLoader.debug)
                    Debug.Log("Unloading node " + name);
                return true;

                // // this.dispatchEvent( { type: 'dispose' } );
                // for (let i = 0; i < this.oneTimeDisposeHandlers.length; i++) {
                //     let handler = this.oneTimeDisposeHandlers[i];
                //     handler();
                // }
                // this.oneTimeDisposeHandlers = [];
            }
            return false;
        }

    }
}