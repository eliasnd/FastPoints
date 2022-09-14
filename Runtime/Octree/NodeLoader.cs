using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace FastPoints {

    public class Node {
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

    public class NodeLoader {
        public static int numNodesLoading = 0;
        public static int maxNodesLoading = 20;
        public Metadata metadata;
        public PointAttributes attributes;
        public float[] offset;
		public float[] scale;
        public string pathOctree;

        public string path = "";

        public NodeLoader(string path){
            this.path = path;
        }

        public async void Load(OctreeGeometryNode node) {

            if (node.loaded || node.loading) {
                return;
            }

            Int64 byteSize = node.byteSize;
            Int64 byteOffset = node.byteOffset;

            node.loading = true;
            numNodesLoading++;

            if (node.nodeType == 2){
                await LoadHierarchy(node);
            }


            pathOctree = $"{path}/../octree.bin";

            byte[] buffer;

            if (byteSize == 0) {
                buffer = new byte[0];
                Debug.LogWarning($"loaded node with 0 bytes: {node.name}");
            } else {
                buffer = new byte[byteSize];
                FileStream fs = File.Open(pathOctree, FileMode.Open);
                fs.Seek(byteOffset, SeekOrigin.Begin);
                fs.Read(buffer, 0, (int)byteSize);
            }

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

        void ParseHierarchy(OctreeGeometryNode node, byte[] buffer) {
            int bytesPerNode = 22;
            int numNodes = buffer.Length / bytesPerNode;

            OctreeGeometry octree = node.octreeGeometry;
            OctreeGeometryNode[] nodes = new OctreeGeometryNode[numNodes];
            nodes[0] = node;
            int nodePos = 1;

            for(int i = 0; i < numNodes; i++) {
                OctreeGeometryNode current = nodes[i];

                byte type = (byte)BitConverter.ToChar(buffer, i * bytesPerNode + 0);
                byte childMask = (byte)BitConverter.ToChar(buffer, i * bytesPerNode + 1);
                UInt32 numPoints = BitConverter.ToUInt32(buffer, i * bytesPerNode + 2);
                Int64 byteOffset = BitConverter.ToInt64(buffer, i * bytesPerNode + 6);
                Int64 byteSize = BitConverter.ToInt64(buffer, i * bytesPerNode + 14);

                if (current.nodeType == 2) {
                    // replace proxy with real node
                    current.byteOffset = byteOffset;
                    current.byteSize = byteSize;
                    current.numPoints = numPoints;
                } else if (type == 2) {
                    // load proxy
                    current.hierarchyByteOffset = byteOffset;
                    current.hierarchyByteSize = byteSize;
                    current.numPoints = numPoints;
                } else {
                    // load real node 
                    current.byteOffset = byteOffset;
                    current.byteSize = byteSize;
                    current.numPoints = numPoints;
                }

                if (current.byteSize == 0) {
                    // workaround for issue #1125
                    // some inner nodes erroneously report >0 points even though have 0 points
                    // however, they still report a byteSize of 0, so based on that we now set node.numPoints to 0
                    current.numPoints = 0;
                }
                
                current.nodeType = type;

                if (current.nodeType == 2) {
                    continue;
                }

                for (int childIndex = 0; childIndex < 8; childIndex++) {
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

                    // nodes.push(child);
                    nodes[nodePos] = child;
                    nodePos++;
                }
            }
        }

        async Task LoadHierarchy(OctreeGeometryNode node){
            int hierarchyByteOffset = (int)node.hierarchyByteOffset;
            int hierarchyByteSize = (int)node.hierarchyByteSize;

            string hierarchyPath = $"{this.path}/../hierarchy.bin";

            byte[] buffer = new byte[hierarchyByteSize];
            File.Open(pathOctree, FileMode.Open).Read(buffer, hierarchyByteOffset, hierarchyByteSize);

            ParseHierarchy(node, buffer);
        }

    }
}