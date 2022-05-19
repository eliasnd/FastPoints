using UnityEngine;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;

namespace FastPoints {

    public class OctreeReader {
        Thread loaderThread;
        OctreeLoaderParams loaderParams;

        Thread traversalThread;
        OctreeTraversalParams traversalParams;

        PointCloudLoader loader;

        public int traversalDepth { get; private set; }

        public int pointBudget { get; private set; }


        public OctreeReader(Octree tree, PointCloudLoader loader, Camera initCam) {
            this.loader = loader;

            object paramsLock = new();

            ConcurrentQueue<Node> toLoad = new();

            loaderThread = new Thread(new ParameterizedThreadStart(LoadThread));
            loaderParams = new OctreeLoaderParams {
                paramsLock = paramsLock,
                tree = tree,
                stopSignal = false,
                toLoad = toLoad
            };
            loaderThread.Start(loaderParams);

            traversalThread = new Thread(new ParameterizedThreadStart(TraverseThread));
            traversalParams = new OctreeTraversalParams {
                paramsLock = paramsLock,
                tree = tree,
                stopSignal = false,
                visiblePoints = new(),
                toLoad = toLoad
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

        // Pass null to use default behavior
        public void SetNodesToShow(string[] nodesToShow) {
            lock (traversalParams.paramsLock) {
                traversalParams.nodesToShow = nodesToShow;
            }
        }

        public List<Point> GetLoadedPoints() {
            lock (traversalParams.paramsLock) {
                return traversalParams.visiblePoints;
            }
        }

        public List<(string, AABB)> GetVisibleBBs() {
            lock (traversalParams.paramsLock) {
                return traversalParams.visibleBBox;
            }
        }

        public void SetDebug() {
            lock (traversalParams.paramsLock) {
                traversalParams.debug = true;
            }
        }

        public void SetTraversalDepth(int depth) {
            lock (traversalParams.paramsLock) {
                traversalParams.traversalDepth = depth;
            }

            traversalDepth = depth;
        }

        public void SetPointBudget(int pointBudget) {
            lock (traversalParams.paramsLock) {
                traversalParams.pointBudget = pointBudget;
            }

            this.pointBudget = pointBudget;
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

            while (!p.stopSignal) {
                while (p.toLoad.Count > 0) {
                    Node n;
                    while (!p.toLoad.TryDequeue(out n)) Thread.Sleep(5);
                    if (n.points != null)   // Already loaded
                        continue;

                    byte[] pBytes = new byte[n.pointCount * 15];

                    if (fs.Position != n.offset)
                        fs.Seek(n.offset, SeekOrigin.Begin);
                    fs.Read(pBytes, 0, (int)n.pointCount * 15);
                    Point[] points = Point.ToPoints(pBytes);
                    lock (n) {
                        n.points = points;
                        n.AwaitingLoad = false;
                    }
                }
                Thread.Sleep(50);
            }
        }

        // Traverses tree, setting nodes to be loaded and removed
        static void TraverseThread(object obj) {
            OctreeTraversalParams p = (OctreeTraversalParams)obj;
            int traversalCount = 0;

            double RenderSize(Node n) {
                double distance = (n.bbox.Center - p.camPosition).magnitude;
                double slope = Math.Tan(p.fov / 2 * Mathf.Deg2Rad);
                double projectedSize = (p.screenHeight / 2.0) * (n.bbox.Size.magnitude / 2) / (slope * distance);

                float minNodeSize = 10;

                if (!Utils.TestPlanesAABB(p.frustum, n.bbox) || projectedSize < minNodeSize) // If bbox outside camera or too small
                    return 0;
                else 
                    return projectedSize;

            }

            void TraverseUnload(Node n) {
                lock (n) {
                    if (n.points != null)
                        // p.toUnload.Enqueue(n);
                        n.points = null;
                }
                for (int i = 0; i < 8; i++)
                    if (n.children[i] != null)
                        TraverseUnload(n.children[i]);
            }

            // Only called if node visible
            void TraverseNode(Node n, SimplePriorityQueue<Node> bfsQueue, List<Point> target, List<(string, AABB)> bboxTarget, bool debug = false) {

                string debugStr = $"{traversalCount}: Traversing node {n.name} with size {RenderSize(n)}";
                lock (n) {
                    if (p.pointBudget <= 0 || target.Count + n.pointCount < p.pointBudget) {
                        bboxTarget.Add((n.name, n.bbox));
                        if (n.points != null) {
                            target.AddRange(n.points);
                            debugStr += $", rendering {n.pointCount} points";
                        } else if (!n.AwaitingLoad) {
                            n.AwaitingLoad = true;
                            p.toLoad.Enqueue(n);
                        }
                    } else {
                        debugStr += $", rendering {n.pointCount} points would lead to point count of {target.Count + n.pointCount}";
                    }
                }

                if (debug)
                    Debug.Log(debugStr);

                traversalCount++;

                if (p.traversalDepth > 0 && n.name.Length >= p.traversalDepth)
                    return;

                for (int i = 0; i < 8; i++) {
                    if (n.children[i] == null)
                        continue;
                    double size = RenderSize(n.children[i]);
                    if (size == 0)
                        TraverseUnload(n.children[i]);
                    else {
                        if (debug)
                            Debug.Log($"Enqueuing node {n.children[i].name} with size {size}");
                        bfsQueue.Enqueue(n.children[i], -1 * (float)size);   // Dequeue returns min priority
                    }
                }
            }

            Node GetNode(string name) {
                if (name[0] != 'n')
                    throw new Exception($"Name {name} not properly formatted for GetNode!");

                Node curr = p.tree.root;
                for (int i = 1; i < name.Length; i++) {
                    int c = name[i] - '0';
                    if (c < 0 || c > 7)
                        throw new Exception($"Name {name} not properly formatted for GetNode!");
                    curr = curr.children[c];
                }

                return curr;
            }

            while (!p.stopSignal) {
                List<Point> visibleTmp = new();
                List<(string, AABB)> bboxTmp = new();


                if (p.nodesToShow != null) {
                    try {
                        foreach (string name in p.nodesToShow) {
                            Node n = GetNode(name);
                            bboxTmp.Add((name, n.bbox));
                            if (n.points != null) {
                                visibleTmp.AddRange(n.points);
                            } else if (!n.AwaitingLoad) {
                                n.AwaitingLoad = true;
                                p.toLoad.Enqueue(n);
                            }
                            // Quick check here for debugging
                            // foreach (Point pt in n.points) {
                            //     if (!n.bbox.InAABB(pt.pos))
                            //         Debug.LogError($"Point outside node {n.name}!");
                            // }
                        }
                    } catch (Exception e) {
                        Debug.LogError("Problem!");
                    }
                } else {
                    visibleTmp = new(p.pointBudget);

                    SimplePriorityQueue<Node> bfsQueue = new();
                    bfsQueue.Enqueue(p.tree.root, 0);
                    traversalCount = 0;
                    int c = 0;
                    bool debugAtStart = p.debug;
                    while (bfsQueue.Count > 0) {
                        c++;
                        Node n = bfsQueue.Dequeue();                   
                        TraverseNode(n, bfsQueue, visibleTmp, bboxTmp, debugAtStart);
                    }  

                    if (debugAtStart) {
                        lock (p.paramsLock) {
                            p.debug = false;
                        }
                    }
                }

                lock (p.paramsLock) {
                    p.visiblePoints = visibleTmp;
                    p.visibleBBox = bboxTmp;
                }

                // while (p.toLoad.Count > 0)
                    Thread.Sleep(10);

                
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
        public int pointBudget = 1000000;
        public List<Point> visiblePoints;
        public List<(string, AABB)> visibleBBox;
        public ConcurrentQueue<Node> toLoad;
        public ConcurrentQueue<Node> toUnload;
        public bool debug;
        public bool stopSignal;
        public int traversalDepth;
        public string[] nodesToShow;
    }

    public class OctreeLoaderParams {
        public object paramsLock;
        public Octree tree;
        public ConcurrentQueue<Node> toLoad;
        public ConcurrentQueue<Node> toUnload;
        public bool stopSignal;
    }

}