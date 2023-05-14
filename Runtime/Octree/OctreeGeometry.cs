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
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Buffers;

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
        public string projection;
        public Vector3 offset;
        public byte[] posBytes;
        public byte[] colBytes;
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

        public void Unload()
        {
            if (posBytes != null)
            {
                ArrayPool<byte>.Shared.Return(posBytes);
                posBytes = null;
                ArrayPool<byte>.Shared.Return(colBytes);
                colBytes = null;
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
        public bool created;
        public bool Created
        {
            get { return created; }
        }


        public OctreeGeometryNode(string name, OctreeGeometry octreeGeometry, AABB boundingBox)
        {
            this.id = IDCount++;
            this.name = name;
            this.index = (int)Char.GetNumericValue(name[name.Length - 1]);
            this.octreeGeometry = octreeGeometry;
            this.boundingBox = boundingBox;
            this.children = new OctreeGeometryNode[8];
            this.numPoints = 0;
            this.level = -1;
        }

        public void Load()
        {
            if (name == "r")
                Debug.Log("Loading r!");
            try
            {
                NodeLoader.Enqueue(this);
            }
            catch (Exception e)
            {
                Debug.LogError($"Got error message: {e.Message}, backtrace: {e.ToString()}");
            }
        }

        // Must be called from main thread!
        public bool Create()
        {
            try
            {
                if (numPoints == 0)
                    return true;

                if (!loaded || octreeGeometry.posBuffer != null && octreeGeometry.posBuffer.IsValid() || octreeGeometry.colBuffer != null && octreeGeometry.colBuffer.IsValid())
                    return false;

                if (octreeGeometry.posBytes == null || octreeGeometry.colBytes == null)
                    throw new Exception("Bytes not loaded!");

                octreeGeometry.posBuffer = new ComputeBuffer((int)numPoints, 12);
                octreeGeometry.posBuffer.SetData(octreeGeometry.posBytes, 0, 0, (int)numPoints * 12);
                octreeGeometry.colBuffer = new ComputeBuffer((int)numPoints, 4);
                octreeGeometry.colBuffer.SetData(octreeGeometry.colBytes, 0, 0, (int)numPoints * 4);

                created = true;
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }

            return true;
        }

        public bool Dispose()
        {
            if (this.loaded && this.octreeGeometry != null && this.parent != null)
            {
                lock (NodeLoader.loaderLock)
                {
                    NodeLoader.nodesLoaded--;
                }
                this.octreeGeometry.Dispose();
                created = false;
                return true;
            }
            return false;
        }

        public bool Unload()
        {
            if (this.name == "r")
                Debug.Log("Unloading r!");
            if (this.loaded && this.octreeGeometry != null)
            {
                octreeGeometry.Unload();
                loaded = false;
                return true;
            }
            return false;
        }
    }
}