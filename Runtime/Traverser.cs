using UnityEngine;
using System;
using System.Threading;
using System.Collections.Generic;


namespace FastPoints
{
    class TraversalParams
    {
        public Plane[] frustum;
        public Vector3 camPosition;
        public float screenHeight;
        public float fov;
        public PointCloudRenderer loader;
        public NodeLoader nodeLoader;
        public OctreeGeometry geometry;
        public int pointBudget;
        public bool stopSignal = false;
        public int loadedNodeCount = 0;
    }

    public class Traverser
    {
        Thread thread;
        TraversalParams p;

        public Traverser(OctreeGeometry geometry, NodeLoader nodeLoader, PointCloudRenderer loader, int pointBudget)
        {
            p = new TraversalParams();
            p.geometry = geometry;
            p.nodeLoader = nodeLoader;
            p.loader = loader;
            p.pointBudget = pointBudget;

            thread = new Thread(new ParameterizedThreadStart(TraversalThread));
        }

        public void Start()
        {
            thread.Start(p);
        }

        public int GetLoadedNodeCount()
        {
            int count;
            lock (p)
            {
                count = p.loadedNodeCount;
            }
            return count;
        }

        public void Stop()
        {
            lock (p)
            {
                p.stopSignal = true;
            }
        }

        public void SetCamera(Camera cam, Matrix4x4 localToWorldMatrix)
        {
            lock (p)
            {
                p.frustum = GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix * localToWorldMatrix);
                p.camPosition = cam.transform.position;
                p.screenHeight = cam.pixelRect.height;
                p.fov = cam.fieldOfView;
            }
        }

        static void TraversalThread(object obj)
        {
            TraversalParams p = (TraversalParams)obj;

            int renderedCount = 0;
            uint renderedPoints = 0;
            int maxNodesToRender;

            string explanation = "";

            void TraverseNode(OctreeGeometryNode n, SimplePriorityQueue<OctreeGeometryNode> nodeQueue, Queue<OctreeGeometryNode> geomQueue, Queue<OctreeGeometryNode> deleteQueue)
            {
                string explanationLine = $"Traversing node {n.name}";
                if (n.loaded)
                {
                    if (n.numPoints > 0 && renderedPoints + n.numPoints < p.pointBudget && (n.Created || maxNodesToRender > 0))
                    {
                        lock (PointCloudRenderer.Cache)
                        {
                            PointCloudRenderer.Cache.TryRemove(n);
                        }
                        explanationLine += $", rendering. Created = {n.Created}. ";
                        renderedCount++;
                        geomQueue.Enqueue(n);
                        renderedPoints += n.numPoints;
                        if (!n.Created)
                            maxNodesToRender--;
                    }
                    else
                    {
                        explanationLine += $", not rendering. NumPoints is {n.numPoints}, pointBudget is {p.pointBudget}, created is {n.Created} and maxRender is {maxNodesToRender}. ";
                    }

                }
                else if (!n.loading)
                {
                    explanationLine += ", loading. ";
                    lock (p)
                    {
                        p.loadedNodeCount++;
                    }
                    n.Load();
                }
                else
                {
                    explanationLine += ", already loading. ";
                }

                string loadLine = "Queuing children ";
                string deleteLine = "Deleting children ";

                for (int i = 0; i < 8; i++)
                {
                    OctreeGeometryNode c = n.children[i];
                    if (c == null)
                        continue;

                    double size = RenderSize(c);
                    if (size > 0 || c.level <= 2)
                    {
                        loadLine += c.name + " ";
                        nodeQueue.Enqueue(c, -1 * (float)size);   // Dequeue returns min priority

                    }
                    else if (c.Created)
                    {
                        deleteLine += c.name + " ";
                        deleteQueue.Enqueue(c);
                    }
                }

                explanation += explanationLine + loadLine + ". " + deleteLine + ".\n";
            }

            double RenderSize(OctreeGeometryNode n)
            {
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

            while (!p.stopSignal)
            {
                maxNodesToRender = 30;
                renderedCount = 0;
                renderedPoints = 0;
                explanation = "";
                Queue<OctreeGeometryNode> nodesToRender = new Queue<OctreeGeometryNode>();
                Queue<OctreeGeometryNode> nodesToDelete = new Queue<OctreeGeometryNode>();

                nodeQueue.Enqueue(p.geometry.root, 0);

                int c = 0;
                while (nodeQueue.Count > 0)
                {
                    c++;
                    OctreeGeometryNode n = nodeQueue.Dequeue();
                    TraverseNode(n, nodeQueue, nodesToRender, nodesToDelete);
                }

                if (PointCloudRenderer.debug)
                    Debug.Log($"Rendering {renderedCount} nodes, {renderedPoints} points");
                if (renderedCount < 100)
                    Debug.Log(explanation);

                p.loader.SetQueues(nodesToRender, nodesToDelete);
            }
        }
    }
}