using UnityEngine;
using System;
using System.Numerics;

using Vector3 = UnityEngine.Vector3;

namespace FastPoints {

    public class OctreeGeometry {
        public string path;
        public float[] scale;
		public float spacing;
        public AABB boundingBox;
        public TreeNode root;
        public PointAttributes pointAttributes;
        public NodeLoader loader;
        public string projection;
        public Vector3 offset;
    }
    
    public class OctreeGeometryNode : TreeNode {

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
        public TreeNode[] children;
        public BigInteger hierarchyByteOffset;
        public BigInteger hierarchyByteSize;
        public bool hasChildren;
        public float spacing;
        public Int64 byteOffset;
        public Int64 byteSize;

        public bool loading;
        public bool loaded;


        public OctreeGeometryNode(string name, OctreeGeometry octreeGeometry, AABB boundingBox){
            this.id = IDCount++;
            this.name = name;
            this.index = (int)Char.GetNumericValue(name[name.Length-1]);
            this.octreeGeometry = octreeGeometry;
            this.boundingBox = boundingBox;
            // this.boundingSphere = boundingBox.getBoundingSphere(new THREE.Sphere());
            this.children = new TreeNode[8];
            this.numPoints = 0;
            this.level = -1;
        }

        // public List<PointCloudTreeNode> GetChildren() {
        //     List<PointCloudTreeNode> children = new List<PointCloudTreeNode>();

        //     for (let i = 0; i < 8; i++) 
        //         if (this.children[i] != null)
        //             children.Add(this.children[i]);

        //     return children;
        // }

        // public void Load() {

        //     if (NodeLoader.numNodesLoading >= NodeLoader.maxNodesLoading) {
        //         return;
        //     }

        //     octreeGeometry.loader.Load(this);
        // }

        // public void Dispose() {
        //     if (this.geometry != null && this.parent != null) {
        //         this.geometry.Dispose();
        //         this.geometry = null;
        //         this.loaded = false;

        //         // // this.dispatchEvent( { type: 'dispose' } );
        //         // for (let i = 0; i < this.oneTimeDisposeHandlers.length; i++) {
        //         //     let handler = this.oneTimeDisposeHandlers[i];
        //         //     handler();
        //         // }
        //         // this.oneTimeDisposeHandlers = [];
        //     }
        // }

    }
}