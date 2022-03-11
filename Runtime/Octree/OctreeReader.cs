using UnityEngine;

using System;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;

namespace FastPoints {

    public class OctreeReader {
        Thread maskerThread;
        OctreeMaskerParams maskerParams;
        Thread readerThread;
        OctreeReaderParams readerParams;

        PointCloudLoader loader;

        public OctreeReader(Octree tree, PointCloudLoader loader) {
            this.loader = loader;

            maskerThread = new Thread(new ParameterizedThreadStart(MaskOctree));
            maskerParams = new OctreeMaskerParams();
            readerThread.Start(maskerParams);

            readerThread = new Thread(new ParameterizedThreadStart(ReadOctree));
            readerParams = new OctreeReaderParams();
            readerThread.Start(readerParams);
        }

        // Call from main thread!
        public void SetCamera(Camera cam) {
            lock (maskerParams.paramsLock) {
                maskerParams.frustum = GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix * loader.transform.localToWorldMatrix);
                maskerParams.camPosition = cam.transform.position;
                maskerParams.screenHeight = cam.pixelRect.height;
                maskerParams.fov = cam.fieldOfView;
            }
        }

        public void StopReader() {
            lock (maskerParams.paramsLock) {
                maskerParams.stopSignal = true;
            }
            lock (readerParams.paramsLock) {
                readerParams.stopSignal = true;
            }
        }

        static void MaskOctree(object obj) {
            OctreeMaskerParams p = (OctreeMaskerParams)obj;

            while (!p.stopSignal)
            {
                bool[] mask;

                lock (p.paramsLock)
                {
                    mask = GetTreeMask(p.tree, p.frustum, p.camPosition, p.fov, p.screenHeight);
                }

                byte[] pBytes = new byte[CountVisiblePoints(p.tree, mask) * 15];
                int idx = 0;

                FileStream fs = File.OpenRead($"{p.tree.dirPath}/octree.dat");

                for (int i = 0; i < mask.Length; i++)
                {
                    if (!mask[i])
                        continue;

                    NodeEntry n = p.tree.nodes[i];

                    if (fs.Position != n.offset)
                        fs.Seek(n.offset, SeekOrigin.Begin);

                    fs.Read(pBytes, idx, (int)n.pointCount);
                }
                    
            }
        }

        // Get masks separately from points to avoid needless IO
        public static bool[] GetTreeMask(Octree tree, Plane[] frustum, Vector3 camPosition, float fov, float screenHeight) {
            bool[] mask = new bool[tree.nodes.Length];
            // Populate bool array with true if node is visible, false otherwise
            int i = 0;
            while (i < tree.nodes.Length) {
                NodeEntry n = tree.nodes[i];
                double distance = (n.bbox.Center - camPosition).magnitude;
                double slope = Math.Tan(fov / 2 * Mathf.Deg2Rad);
                double projectedSize = (screenHeight / 2.0) * (n.bbox.Size.magnitude / 2) / (slope * distance);

                float minNodeSize = 10;

                if (!Utils.TestPlanesAABB(frustum, n.bbox) || projectedSize < minNodeSize) { // If bbox outside camera or too small
                    mask[i] = false;
                    i += (int)tree.nodes[i].descendentCount;
                } else {    // Set to render and step into children
                    mask[i] = true;
                    i++;
                }
            }

            return mask;
        }


        public static int CountVisiblePoints(Octree tree, bool[] mask) {
            int sum = 0;
            for (int i = 0; i < tree.nodes.Length; i++)
                if (mask[i])
                    sum += (int)tree.nodes[i].pointCount;
            return sum;
        }

        

        static void ReadOctree(object obj) {
            OctreeReaderParams p = (OctreeReaderParams)obj;
            FileStream fs = File.OpenRead(p.tree.dirPath);

            while (!p.stopSignal) {
                // Construct new point array and pass to compute shader

                NodeEntry node;
                while (!p.nodesToRead.TryDequeue(out node)) {
                    if (p.stopSignal)
                        return;
                    Thread.Sleep(50);
                }

                // At this point, have node

            }
        }
    }

    public struct OctreeMaskerParams {
        public object paramsLock;
        public Octree tree;
        public Plane[] frustum;
        public Vector3 camPosition;
        public float screenHeight;
        public float fov;
        public bool[] currMask;
        public bool stopSignal;
    }

    public struct OctreeReaderParams {
        public object paramsLock;
        public Octree tree;
        public ConcurrentQueue<NodeEntry> toLoad;
        public ConcurrentQueue<NodeEntry> toUnload;
        public bool stopSignal;
    }

}