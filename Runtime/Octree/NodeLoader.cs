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
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Buffers;
using System.Threading;

namespace FastPoints
{
    class LoaderParams
    {
        public int numNodesLoading = 0;
        public Queue<OctreeGeometryNode> loadQueue;
        public int maxNodesLoading;
        public int numNodesLoaded = 0;
        public PointAttributes attributes;
        public string pathOctree;
        public string path;
        public bool stopSignal;
    }

    public class Node
    {
        public string name;
        public bool loaded;
        public bool loading;
        public int nodeType;
        public Int64 byteOffset;
        public Int64 byteSize;
        public UInt32 numPoints;
        public Int64 hierarchyByteOffset;
        public BigInteger hierarchyByteSize;
    }

    public static class NodeLoader
    {
        static Thread thread;
        static LoaderParams p;
        static int maxNodesLoading = 5;
        public static int nodesLoading = 0;
        public static int nodesLoaded = 0;
        public static object loaderLock = new object();

        public static void Start(string path, Metadata metadata, PointAttributes attributes, float[] scale, float[] offset)
        {
            p = new LoaderParams();
            p.path = path;
            p.attributes = attributes;
            p.maxNodesLoading = maxNodesLoading;
            p.loadQueue = new Queue<OctreeGeometryNode>(maxNodesLoading);

            thread = new Thread(new ParameterizedThreadStart(LoadingThread));
            thread.Start(p);
        }

        public static void Enqueue(OctreeGeometryNode node)
        {
            if (node.loaded || node.loading)
                return;

            lock (p)
            {
                if (p.loadQueue.Count >= maxNodesLoading)
                    return;

                p.loadQueue.Enqueue(node);
            }

            lock (loaderLock)
            {
                nodesLoading++;
            }
        }

        // -- ALL METHODS FOLLOWING THIS DO NOT HAPPEN ON MAIN THREAD --

        static void LoadingThread(object obj)
        {
            LoaderParams p = (LoaderParams)obj;

            bool stopSignal;
            lock (p)
            {
                stopSignal = p.stopSignal;
            }

            while (!stopSignal)
            {
                OctreeGeometryNode node;
                string path;
                PointAttributes attributes;
                lock (p)
                {
                    p.loadQueue.TryDequeue(out node);
                    path = p.path;
                    attributes = p.attributes;
                }

                if (node == null)   // Maybe sleep?
                    continue;

                Load(node, path, attributes);

                lock (p)
                {
                    stopSignal = p.stopSignal;
                }
            }
        }
        static void Load(OctreeGeometryNode node, string path, PointAttributes attributes)
        {
            node.loading = true;

            if (node.nodeType == 2)
            {
                LoadHierarchy(node, path);
            }

            Int64 byteSize = node.byteSize;
            Int64 byteOffset = node.byteOffset;

            string pathOctree = $"{Directory.GetParent(path).ToString()}/octree.bin";

            byte[] buffer;

            if (byteSize == 0)
                buffer = new byte[0];
            else
            {
                buffer = new byte[byteSize];
                FileStream fs = File.Open(pathOctree, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(byteOffset, SeekOrigin.Begin);
                fs.Read(buffer, 0, (int)byteSize);
                // fs.Close();
            }

            if (node.numPoints == 0)
            {
                lock (NodeLoader.loaderLock)
                {
                    NodeLoader.nodesLoaded++;
                }
                node.loaded = true;
                node.loading = false;

                return;
            }

            try
            {
                Dictionary<string, Tuple<byte[], PointAttribute>> attributeBuffers = Decoder.Decode(new DecoderInput(
                    buffer,
                    attributes,
                    node.octreeGeometry.scale,
                    node.name,
                    node.boundingBox.Min + node.octreeGeometry.offset,
                    node.boundingBox.Max + node.octreeGeometry.offset,
                    node.boundingBox.Max - node.boundingBox.Min,
                    node.octreeGeometry.offset,
                    (int)node.numPoints
                ));

                node.octreeGeometry.posBytes = ArrayPool<Byte>.Shared.Rent((int)node.numPoints * 12);
                attributeBuffers["position"].Item1.CopyTo(node.octreeGeometry.posBytes, 0);
                node.octreeGeometry.colBytes = ArrayPool<Byte>.Shared.Rent((int)node.numPoints * 4);

                // Only add colors if attribute provided
                if (attributeBuffers.ContainsKey("rgba"))
                    attributeBuffers["rgba"].Item1.CopyTo(node.octreeGeometry.colBytes, 0);
                else
                    for (int i = 0; i < (int)node.numPoints * 4; i++)
                        node.octreeGeometry.colBytes[i] = 0x00;


                node.loaded = true;
                node.loading = false;

                lock (NodeLoader.loaderLock)
                {
                    NodeLoader.nodesLoading--;
                }

                lock (PointCloudRenderer.Cache)
                {
                    PointCloudRenderer.Cache.Insert(node);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        static void ParseHierarchy(OctreeGeometryNode node, byte[] buffer)
        {
            int bytesPerNode = 22;
            int numNodes = buffer.Length / bytesPerNode;
            List<string> proxies = new List<string>();

            OctreeGeometry octree = node.octreeGeometry;
            OctreeGeometryNode[] nodes = new OctreeGeometryNode[numNodes];
            nodes[0] = node;
            int nodePos = 1;

            for (int i = 0; i < numNodes; i++)
            {
                OctreeGeometryNode current = nodes[i];

                if (current.name == "r0256")
                    node = node;

                byte type = (byte)BitConverter.ToChar(buffer, i * bytesPerNode + 0);
                byte childMask = (byte)BitConverter.ToChar(buffer, i * bytesPerNode + 1);
                UInt32 numPoints = BitConverter.ToUInt32(buffer, i * bytesPerNode + 2);
                Int64 byteOffset = BitConverter.ToInt64(buffer, i * bytesPerNode + 6);
                Int64 byteSize = BitConverter.ToInt64(buffer, i * bytesPerNode + 14);

                if (current.nodeType == 2)
                {
                    // replace proxy with real node
                    current.byteOffset = byteOffset;
                    current.byteSize = byteSize;
                    current.numPoints = numPoints;
                }
                else if (type == 2)
                {
                    // load proxy
                    proxies.Add(current.name);
                    current.hierarchyByteOffset = byteOffset;
                    current.hierarchyByteSize = byteSize;
                    current.numPoints = numPoints;
                }
                else
                {
                    // load real node 
                    current.byteOffset = byteOffset;
                    current.byteSize = byteSize;
                    current.numPoints = numPoints;
                }

                if (current.byteSize == 0)
                {
                    // workaround for issue #1125
                    // some inner nodes erroneously report >0 points even though have 0 points
                    // however, they still report a byteSize of 0, so based on that we now set node.numPoints to 0
                    current.numPoints = 0;
                }

                current.nodeType = type;

                if (current.nodeType == 2)
                {
                    continue;
                }

                for (int childIndex = 0; childIndex < 8; childIndex++)
                {
                    bool childExists = ((1 << childIndex) & childMask) != 0;

                    if (!childExists)
                        continue;

                    string childName = current.name + childIndex;

                    AABB childAABB = current.boundingBox.ChildAABB(childIndex);
                    OctreeGeometryNode child = new OctreeGeometryNode(childName, octree, childAABB);
                    child.name = childName;
                    child.spacing = current.spacing / 2;
                    child.level = current.level + 1;

                    current.children[childIndex] = child;
                    child.parent = current;

                    OctreeGeometry geometry = new OctreeGeometry();
                    geometry.root = child;
                    geometry.boundingBox = childAABB;
                    geometry.scale = current.octreeGeometry.scale;  // bad here, just mvp
                    geometry.offset = current.octreeGeometry.offset;
                    child.octreeGeometry = geometry;

                    // nodes.push(child);
                    nodes[nodePos] = child;
                    nodePos++;
                }
            }

            if (PointCloudRenderer.debug)
                Debug.Log($"Parsing hierarchy. Starting with node {node.name} and buffer with {numNodes} nodes. Hit proxies: {String.Join(' ', proxies.ToArray())}");
        }

        static void LoadHierarchy(OctreeGeometryNode node, string path)
        {
            int hierarchyByteOffset = (int)node.hierarchyByteOffset;
            int hierarchyByteSize = (int)node.hierarchyByteSize;

            string hierarchyPath = $"{Directory.GetParent(path).ToString()}/hierarchy.bin";

            byte[] buffer = new byte[hierarchyByteSize];
            FileStream fs = File.Open(hierarchyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(hierarchyByteOffset, SeekOrigin.Begin);
            fs.Read(buffer, 0, hierarchyByteSize);

            ParseHierarchy(node, buffer);
        }
    }
}