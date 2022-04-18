using UnityEngine;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;

namespace FastPoints {

    public class OctreeReader {
        static int[] nodesToShow = new int[] {7, 8, 9, 10, 11, 12, 13};
        Thread loaderThread;
        OctreeLoaderParams loaderParams;

        Thread traversalThread;
        OctreeTraversalParams traversalParams;

        PointCloudLoader loader;

        public OctreeReader(Octree tree, PointCloudLoader loader, Camera initCam) {
            this.loader = loader;

            object paramsLock = new();

            loaderThread = new Thread(new ParameterizedThreadStart(LoadThread));
            loaderParams = new OctreeLoaderParams {
                paramsLock = paramsLock,
                tree = tree,
                stopSignal = false
            };
            loaderThread.Start(loaderParams);

            traversalThread = new Thread(new ParameterizedThreadStart(TraverseThread));
            traversalParams = new OctreeTraversalParams {
                paramsLock = paramsLock,
                tree = tree,
                stopSignal = false,
                visiblePoints = new()
            };
            traversalThread.Start(traversalParams);

            SetCamera(initCam);
        }

        // Call from main thread!
        public void SetCamera(Camera cam) {
            lock (traversalParams.paramsLock) {
                traversalParams.frustum = GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix * loader.transform.localToWorldMatrix);
                traversalParams.camPosition = cam.transform.position;
                traversalParams.screenHeight = cam.pixelRect.height;
                traversalParams.fov = cam.fieldOfView;
            }
        }

        public List<Point> GetLoadedPoints() {
            lock (traversalParams.paramsLock) {
                return traversalParams.visiblePoints;
            }
        }

        public List<AABB> GetVisibleBBs() {
            lock (traversalParams.paramsLock) {
                return traversalParams.visibleBBox;
            }
        }

        public void StopReader() {
            lock (loaderParams.paramsLock) {
                loaderParams.stopSignal = true;
            }
            lock (traversalParams.paramsLock) {
                traversalParams.stopSignal = true;
            }
        }

        static void LoadThread(object obj) {
            OctreeLoaderParams p = (OctreeLoaderParams)obj;
            FileStream fs = File.Open($"{p.tree.dirPath}/octree.dat", FileMode.Open, FileAccess.Read, FileShare.Read);

            bool firstTime = true;

            // Maybe use tasks?
            void Traverse(Node n, bool onlyUnload = false) {
                if (onlyUnload) {
                    lock (n) {
                        n.IsVisible = false;
                        n.points = null;
                    }
                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            Traverse(n.children[i], true);
                    return;
                }

                bool isVisible;
                bool pointsNull;
                lock (n) {
                    isVisible = n.IsVisible;  
                    pointsNull = n.points == null;
                }
                if (isVisible) {
                    if (pointsNull) { // Load points
                        byte[] pBytes = new byte[n.pointCount * 15];

                        if (fs.Position != n.offset)
                            fs.Seek(n.offset, SeekOrigin.Begin);
                        fs.ReadAsync(pBytes, 0, (int)n.pointCount * 15).ContinueWith((Task t) => {
                            Point[] points = Point.ToPoints(pBytes);
                            lock (n) {
                                n.points = points;
                            }
                        });
                    } 

                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            Traverse(n.children[i]);
                } else {
                    n.points = null;
                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            Traverse(n.children[i], true);
                }

            }

            void TraverseBFS(Node n, Queue<Node> toTraverse) {
                if (firstTime)
                    Debug.Log($"Traversing {n.name}");
                bool isVisible;
                bool pointsNull;
                lock (n) {
                    isVisible = n.IsVisible;  
                    pointsNull = n.points == null;
                }
                if (isVisible) {
                    if (pointsNull) { // Load points
                        byte[] pBytes = new byte[n.pointCount * 15];

                        if (fs.Position != n.offset)
                            fs.Seek(n.offset, SeekOrigin.Begin);
                        fs.ReadAsync(pBytes, 0, (int)n.pointCount * 15).ContinueWith((Task t) => {
                            Point[] points = Point.ToPoints(pBytes);
                            lock (n) {
                                n.points = points;
                            }
                        });
                    } 

                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            toTraverse.Enqueue(n.children[i]);
                } else {
                    n.points = null;
                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            Traverse(n.children[i], true);
                }
            }

            while (!p.stopSignal) {
                Queue<Node> toTraverse = new();
                toTraverse.Enqueue(p.tree.root);
                int c = 0;
                while (toTraverse.Count > 0) {
                    c++;
                    TraverseBFS(toTraverse.Dequeue(), toTraverse);
                }
                if (c > 1)
                    firstTime = false;
                Thread.Sleep(50);
            }
        }

        // Traverses tree, setting nodes to be loaded and removed
        static void TraverseThread(object obj) {
            OctreeTraversalParams p = (OctreeTraversalParams)obj;

            bool usePointBudget = true;

            bool ShouldRender(Node n) {
                double distance = (n.bbox.Center - p.camPosition).magnitude;
                double slope = Math.Tan(p.fov / 2 * Mathf.Deg2Rad);
                double projectedSize = (p.screenHeight / 2.0) * (n.bbox.Size.magnitude / 2) / (slope * distance);

                float minNodeSize = 10;

                if (!Utils.TestPlanesAABB(p.frustum, n.bbox) || projectedSize < minNodeSize) // If bbox outside camera or too small
                    return false;
                else 
                    return true;

            }

            void Traverse(Node n, List<Point> target, List<AABB> bboxTarget) {
                if (ShouldRender(n)) {
                    lock (n) {
                        n.IsVisible = true;
                        if (n.points != null && (!usePointBudget || target.Count + n.pointCount < p.pointBudget)) {
                            bboxTarget.Add(n.bbox);
                            target.AddRange(n.points);
                        }
                    }
                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            Traverse(n.children[i], target, bboxTarget);
                } else {
                    lock (n) {
                        n.IsVisible = false;
                    }
                }
            }

            void TraverseBFS(Node n, Queue<Node> toTraverse, List<Point> target, List<AABB> bboxTarget) {
                if (ShouldRender(n)) {
                    lock (n) {
                        n.IsVisible = true;
                        if (n.points != null && (!usePointBudget || target.Count + n.pointCount < p.pointBudget)) {
                            bboxTarget.Add(n.bbox);
                            target.AddRange(n.points);
                        }
                    }
                    for (int i = 0; i < 8; i++)
                        if (n.children[i] != null)
                            toTraverse.Enqueue(n.children[i]);
                } else {
                    lock (n) {
                        n.IsVisible = false;
                    }
                }
            }

            while (!p.stopSignal) {
                List<Point> visibleTmp = new(p.pointBudget);
                List<AABB> bboxTmp = new();
                // Traverse(p.tree.root, visibleTmp, bboxTmp);    // Traverse tree
                Queue<Node> toTraverse = new();
                toTraverse.Enqueue(p.tree.root);
                while (toTraverse.Count > 0) {
                    TraverseBFS(toTraverse.Dequeue(), toTraverse, visibleTmp, bboxTmp);
                }
                lock (p.paramsLock) {
                    p.visiblePoints = visibleTmp;
                    p.visibleBBox = bboxTmp;
                }
                Thread.Sleep(50);
            }
        }
    }

    public class OctreeTraversalParams {
        public object paramsLock;
        public Octree tree;
        public Plane[] frustum;
        public Vector3 camPosition;
        public float screenHeight;
        public float fov;
        public int pointBudget = 2500000;
        public List<Point> visiblePoints;
        public List<AABB> visibleBBox;
        public bool stopSignal;
    }

    public class OctreeLoaderParams {
        public object paramsLock;
        public Octree tree;
        public bool stopSignal;
    }

}