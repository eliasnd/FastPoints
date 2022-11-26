using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

using Vector3 = UnityEngine.Vector3;

namespace FastPoints
{

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

    public class NodeLoader
    {
        public static int numNodesLoading = 0;
        public static int maxNodesLoading = 20;
        public static int numNodesLoaded = 0;
        public Metadata metadata;
        public PointAttributes attributes;
        public float[] offset;
        public float[] scale;
        public string pathOctree;

        public string path = "";
        Dispatcher dispatcher;

        public NodeLoader(string path, Dispatcher dispatcher)
        {
            this.path = path;
            this.dispatcher = dispatcher;
        }

        public async void Load(OctreeGeometryNode node)
        {

            if (node.loaded || node.loading)
            {
                return;
            }

            node.loading = true;
            numNodesLoading++;

            if (node.nodeType == 2)
            {
                try
                {
                    await LoadHierarchy(node);
                }
                catch (Exception e) { Debug.LogError(e.ToString()); }
            }

            Int64 byteSize = node.byteSize;
            Int64 byteOffset = node.byteOffset;

            pathOctree = $"{Directory.GetParent(path).ToString()}/octree.bin";

            byte[] buffer;

            if (byteSize == 0)
            {
                buffer = new byte[0];
                Debug.LogWarning($"loaded node with 0 bytes: {node.name}");
            }
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
                numNodesLoaded++;
                node.loaded = true;
                node.loading = false;
                numNodesLoading--;

                return;
            }

            await Task.Run(() =>
            {
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

                    dispatcher.Enqueue(() =>
                    {
                        node.octreeGeometry.posBuffer = new ComputeBuffer((int)node.numPoints, 12);
                        node.octreeGeometry.posBuffer.SetData(attributeBuffers["position"].Item1);
                        node.octreeGeometry.colBuffer = new ComputeBuffer((int)node.numPoints, 4);
                        node.octreeGeometry.colBuffer.SetData(attributeBuffers["rgba"].Item1);

                        numNodesLoaded++;

                        node.loaded = true;
                        node.loading = false;
                        numNodesLoading--;
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError(e.Message);
                }
            });

            //             let workerPath;
            //             if(this.metadata.encoding === "BROTLI"){
            //                 workerPath = Potree.scriptPath + '/workers/2.0/DecoderWorker_brotli.js';
            //             }else{
            //                 workerPath = Potree.scriptPath + '/workers/2.0/DecoderWorker.js';
            //             }

            // let worker = Potree.workerPool.getWorker(workerPath);

            //             worker.onmessage = function (e) {

            //                 let data = e.data;
            //                 let buffers = data.attributeBuffers;

            //                 Potree.workerPool.returnWorker(workerPath, worker);

            //                 let geometry = new THREE.BufferGeometry();

            //                 for(let property in buffers){

            //                     let buffer = buffers[property].buffer;

            //                     if(property === "position"){
            //                         geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(buffer), 3));
            //                     }else if(property === "rgba"){
            //                         geometry.setAttribute('rgba', new THREE.BufferAttribute(new Uint8Array(buffer), 4, true));
            //                     }else if(property === "NORMAL"){
            //                         //geometry.setAttribute('rgba', new THREE.BufferAttribute(new Uint8Array(buffer), 4, true));
            //                         geometry.setAttribute('normal', new THREE.BufferAttribute(new Float32Array(buffer), 3));
            //                     }else if (property === "INDICES") {
            //                         let bufferAttribute = new THREE.BufferAttribute(new Uint8Array(buffer), 4);
            //                         bufferAttribute.normalized = true;
            //                         geometry.setAttribute('indices', bufferAttribute);
            //                     }else{
            //                         const bufferAttribute = new THREE.BufferAttribute(new Float32Array(buffer), 1);

            //                         let batchAttribute = buffers[property].attribute;
            //                         bufferAttribute.potree = {
            //                             offset: buffers[property].offset,
            //                             scale: buffers[property].scale,
            //                             preciseBuffer: buffers[property].preciseBuffer,
            //                             range: batchAttribute.range,
            //                         };

            //                         geometry.setAttribute(property, bufferAttribute);
            //                     }

            //                 }
            //                 // indices ??

            //                 node.density = data.density;
            //                 node.geometry = geometry;
            //                 node.loaded = true;
            //                 node.loading = false;
            //                 Potree.numNodesLoading--;
            //             };

            //             let pointAttributes = node.octreeGeometry.pointAttributes;
            //             let scale = node.octreeGeometry.scale;

            //             let box = node.boundingBox;
            //             let min = node.octreeGeometry.offset.clone().add(box.min);
            //             let size = box.max.clone().sub(box.min);
            //             let max = min.clone().add(size);
            //             let numPoints = node.numPoints;

            //             let offset = node.octreeGeometry.loader.offset;

            //             let message = {
            //                 name: node.name,
            //                 buffer: buffer,
            //                 pointAttributes: pointAttributes,
            //                 scale: scale,
            //                 min: min,
            //                 max: max,
            //                 size: size,
            //                 offset: offset,
            //                 numPoints: numPoints
            //             };

            //             worker.postMessage(message, [message.buffer]);
            // }catch(e){
            //     node.loaded = false;
            //     node.loading = false;
            //     Potree.numNodesLoading--;

            //     console.log(`failed to load ${node.name}`);
            //     console.log(e);
            //     console.log(`trying again!`);
        }

        void ParseHierarchy(OctreeGeometryNode node, byte[] buffer)
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
                    geometry.loader = this;
                    geometry.scale = current.octreeGeometry.scale;  // bad here, just mvp
                    geometry.offset = current.octreeGeometry.offset;
                    child.octreeGeometry = geometry;

                    // nodes.push(child);
                    nodes[nodePos] = child;
                    nodePos++;
                }
            }

            if (PointCloudLoader.debug)
                Debug.Log($"Parsing hierarchy. Starting with node {node.name} and buffer with {numNodes} nodes. Hit proxies: {String.Join(' ', proxies.ToArray())}");
        }

        async Task LoadHierarchy(OctreeGeometryNode node)
        {
            int hierarchyByteOffset = (int)node.hierarchyByteOffset;
            int hierarchyByteSize = (int)node.hierarchyByteSize;

            if (PointCloudLoader.debug)
                Debug.Log($"Load hierarchy with byte offset {hierarchyByteOffset} and size {hierarchyByteSize}");

            string hierarchyPath = $"{Directory.GetParent(this.path).ToString()}/hierarchy.bin";

            byte[] buffer = new byte[hierarchyByteSize];
            FileStream fs = File.Open(hierarchyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(hierarchyByteOffset, SeekOrigin.Begin);
            fs.Read(buffer, 0, hierarchyByteSize);

            ParseHierarchy(node, buffer);
        }

    }
}