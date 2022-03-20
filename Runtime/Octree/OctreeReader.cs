using UnityEngine;

using System;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace FastPoints {

    public class OctreeReader {
        static int[] nodesToShow = new int[] {7, 8, 9, 10, 11, 12, 13};
        Thread maskerThread;
        OctreeMaskerParams maskerParams;
        Thread readerThread;
        OctreeReaderParams readerParams;

        PointCloudLoader loader;

        public OctreeReader(Octree tree, PointCloudLoader loader, Camera initCam) {
            Debug.Log("Octree reader constructor");
            this.loader = loader;

            ConcurrentQueue<int> toLoad = new();
            ConcurrentQueue<int> toUnload = new();
            object paramsLock = new();

            maskerThread = new Thread(new ParameterizedThreadStart(MaskOctree));
            maskerParams = new OctreeMaskerParams {
                paramsLock = paramsLock,
                tree = tree,
                stopSignal = false,
                toLoad = toLoad,
                toUnload = toUnload,
                nodesToShow = new int[0]
            };
            SetCamera(initCam);
            maskerThread.Start(maskerParams);

            readerThread = new Thread(new ParameterizedThreadStart(ReadOctree));
            readerParams = new OctreeReaderParams {
                paramsLock = paramsLock,
                tree = tree,
                stopSignal = false,
                toLoad = toLoad,
                toUnload = toUnload
            };
            readerThread.Start(readerParams);

            Debug.Log("Done octree reader");
        }

        // Call from main thread!
        public void SetCamera(Camera cam) {
            lock (maskerParams.paramsLock) {
                maskerParams.frustum = GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix * loader.transform.localToWorldMatrix);
                maskerParams.camPosition = cam.transform.position;
                maskerParams.screenHeight = cam.pixelRect.height;
                maskerParams.fov = cam.fieldOfView;
            }
            Debug.Log($"Set camera position to {maskerParams.camPosition.ToString()}");
        }

        public void StopReader() {
            lock (maskerParams.paramsLock) {
                maskerParams.stopSignal = true;
            }
            lock (readerParams.paramsLock) {
                readerParams.stopSignal = true;
            }
        }

        public void SetNodesToShow(int[] nodesToShow) {
            lock (maskerParams.nodesToShow) {
                maskerParams.nodesToShow = nodesToShow;
                foreach (int i in nodesToShow)
                    maskerParams.toLoad.Enqueue(i);
            }
        }

        static void MaskOctree(object obj) {
            Debug.Log("Octree mask thread");
            OctreeMaskerParams p = (OctreeMaskerParams)obj;
            

            while (!p.stopSignal) {
                bool[] mask;

                if (p.nodesToShow.Length != 0) {
                    continue;
                }

                lock (p.paramsLock)
                {
                    mask = GetTreeMask(p.tree, p.frustum, p.camPosition, p.fov, p.screenHeight);
                }

                for (int i = 0; i < mask.Length; i++)
                {
                    if (!mask[i] && p.tree.nodes[i].points != null) {
                        p.tree.nodes[i].points = null;
                        // p.toUnload.Enqueue(i);
                    } else if (mask[i] && p.tree.nodes[i].points == null)
                        p.toLoad.Enqueue(i);
                }

                Thread.Sleep(50);
                    
            }
        }

        // Get masks separately from points to avoid needless IO
        public static bool[] GetTreeMask(Octree tree, Plane[] frustum, Vector3 camPosition, float fov, float screenHeight) {
            Debug.Log($"Get tree mask. camPosition is {camPosition.ToString()}");
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
                    i += (int)tree.nodes[i].descendentCount + 1;
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
            Debug.Log("Octree read thread");
            OctreeReaderParams p = (OctreeReaderParams)obj;
            FileStream fs = File.Open($"{p.tree.dirPath}/octree.dat", FileMode.Open, FileAccess.Read, FileShare.Read);

            int[] unloadCounts = new int[p.tree.nodes.Length];  // Unused

            while (!p.stopSignal) {
                Debug.Log("Read octree loop");
                // Construct new point array and pass to compute shader

                if (p.toLoad.Count > 0) {
                    int n;
                    while (!p.toLoad.TryDequeue(out n))
                        Thread.Sleep(50);

                    NodeEntry node = p.tree.nodes[n];

                    if (node.points != null)
                        continue;

                    lock (node) {
                        byte[] pBytes = new byte[node.pointCount * 15];

                        if (fs.Position != node.offset)
                            fs.Seek(node.offset, SeekOrigin.Begin);
                        fs.Read(pBytes, 0, (int)node.pointCount * 15);
                        node.points = Point.ToPoints(pBytes);
                    }

                    if (n == 0) {
                        int[] xRangeCounts = new int[24];
                        foreach (Point pt in node.points)
                            xRangeCounts[12+Mathf.FloorToInt(pt.pos.x)]++;
                        xRangeCounts = xRangeCounts;
                    }
                }

                // if (p.toUnload.Count > 0) {
                //     // Probably do some caching here, check number with unloadCounts
                //     int n;
                //     while (!p.toUnload.TryDequeue(out n))
                //         Thread.Sleep(50);

                //     NodeEntry node = p.tree.nodes[n];
                //     node.points = null;
                //     Debug.Log($"Unloaded {n}");
                // }
                int loadedNodes = 0;
                for (int i = 0; i < p.tree.nodes.Length; i++)
                    if (p.tree.nodes[i].points != null)
                        loadedNodes++;
                Debug.Log($"Cycle, {loadedNodes} loaded nodes");
                Thread.Sleep(50);
            }
        }
    }

    public class OctreeMaskerParams {
        public object paramsLock;
        public Octree tree;
        public Plane[] frustum;
        public Vector3 camPosition;
        public float screenHeight;
        public float fov;
        public bool[] currMask;
        public bool stopSignal;
        public ConcurrentQueue<int> toLoad;
        public ConcurrentQueue<int> toUnload;
        public int[] nodesToShow;
    }

    public class OctreeReaderParams {
        public object paramsLock;
        public Octree tree;
        public ConcurrentQueue<int> toLoad;
        public ConcurrentQueue<int> toUnload;
        public bool stopSignal;
    }

}