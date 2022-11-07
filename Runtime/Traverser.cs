using UnityEngine;
using System;
using System.Threading;
using System.Collections.Generic;


namespace FastPoints {
    class TraversalParams {
        public Plane[] frustum;
        public Vector3 camPosition;
        public float screenHeight;
        public float fov;
        public PointCloudLoader loader;
        public Dispatcher dispatcher;
        public NodeLoader nodeLoader;
        public OctreeGeometry geometry;
        public int pointBudget;
        public bool stopSignal = false;
    }

    public class Traverser {
        Thread thread;
        TraversalParams p;

        public Traverser(OctreeGeometry geometry, NodeLoader nodeLoader, PointCloudLoader loader, int pointBudget, Dispatcher dispatcher) {
            p = new TraversalParams();
            p.geometry = geometry;
            p.nodeLoader = nodeLoader;
            p.loader = loader;
            p.pointBudget = pointBudget;
            p.dispatcher = dispatcher;

            thread = new Thread(new ParameterizedThreadStart(TraversalThread));
        }

        public void Start() {
            thread.Start(p);
        }

        public void Stop() {
            lock (p) {
                p.stopSignal = true;
            }
        }

        public void SetCamera(Camera cam, Matrix4x4 localToWorldMatrix) {
            lock (p) {
                p.frustum = GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix * localToWorldMatrix);
                p.camPosition = cam.transform.position;
                p.screenHeight = cam.pixelRect.height;
                p.fov = cam.fieldOfView;
            }
        }

        static void TraversalThread(object obj) {
            TraversalParams p = (TraversalParams)obj;

            int renderedCount = 0;
            uint renderedPoints = 0;

            void TraverseNode(OctreeGeometryNode n, SimplePriorityQueue<OctreeGeometryNode> nodeQueue, Queue<OctreeGeometry> geomQueue) {
                if (n.loaded) {
                    if (n.numPoints > 0 && renderedPoints + n.numPoints < p.pointBudget) {
                        renderedCount++;
                        geomQueue.Enqueue(n.octreeGeometry);
                        renderedPoints += n.numPoints;
                    }

                } else if (!n.loading)
                    n.Load();

                for (int i = 0; i < 8; i++) {
                    OctreeGeometryNode c = n.children[i];
                    if (c == null)
                        continue;

                    double size = RenderSize(c);
                    if (size == 0 && c.level > 2)
                        p.dispatcher.Enqueue(() => {
                            try { 
                            c.Dispose();
                            }    catch (Exception e)
                            {
                                Debug.LogError("Here");
                            }
                        });
                    else
                        nodeQueue.Enqueue(n.children[i], -1 * (float)size);   // Dequeue returns min priority
                }
            }

            double RenderSize(OctreeGeometryNode n) {
                double distance = (n.boundingBox.Center - p.camPosition).magnitude;
                double slope = Math.Tan(p.fov / 2 * Mathf.Deg2Rad);
                double projectedSize = (p.screenHeight / 2.0) * (n.boundingBox.Size.magnitude / 2) / (slope * distance);

                float minNodeSize = 10;

                if (!Utils.TestPlanesAABB(p.frustum, n.boundingBox) || projectedSize < minNodeSize) // If bbox outside camera or too small
                    return 0;
                else 
                    return projectedSize;
            }

            SimplePriorityQueue<OctreeGeometryNode> nodeQueue = new SimplePriorityQueue<OctreeGeometryNode>();

            // Swap queues to save memory
            Queue<OctreeGeometry> queue1 = new Queue<OctreeGeometry>();
            Queue<OctreeGeometry> queue2 = new Queue<OctreeGeometry>();

            Queue<OctreeGeometry> nodesToRender = queue1;
            p.loader.SetQueue(queue2);

            while (!p.stopSignal) {
                renderedCount = 0;
                renderedPoints = 0;
                nodesToRender.Clear();

                nodeQueue.Enqueue(p.geometry.root, 0);

                // int traversalCount = 0;
                int c = 0;
                while (nodeQueue.Count > 0) {
                    c++;
                    OctreeGeometryNode n = nodeQueue.Dequeue();                
                    TraverseNode(n, nodeQueue, nodesToRender);
                }

                if (PointCloudLoader.debug)
                    Debug.Log($"Rendering {renderedCount} nodes, {renderedPoints} points");

                nodesToRender = p.loader.SetQueue(nodesToRender);
            }
        }

        

        
    }
}