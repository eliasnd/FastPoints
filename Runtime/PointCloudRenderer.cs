using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace FastPoints
{
    // [ExecuteInEditMode]
    public class PointCloudRenderer : MonoBehaviour
    {
        #region public
        public PointCloudHandle handle;
        public int decimatedCloudSize = 1000000;
        public float pointSize = 1.5f;
        public Camera cam = null;
        public int pointBudget = 1000000;
        public bool drawGizmos = true;
        #endregion

        [SerializeField]
        int visiblePointCount;
        [SerializeField]
        int visibleNodeCount;
        [SerializeField]
        int loadedNodeCount;
        [SerializeField]
        int loadingNodeCount;
        [SerializeField]
        bool converted;
        [SerializeField]
        int queuedActionCount;
        [SerializeField]
        int cacheSize;
        [SerializeField]
        int cachePoints;
        [SerializeField]
        string converterStatus;

        bool decimatedGenerated = false;
        ComputeBuffer decimatedPosBuffer = null;
        ComputeBuffer decimatedColBuffer = null;

        OctreeGeometry geom;

        int actionsPerFrame = 2;

        bool octreeGenerated = true;

        ComputeShader computeShader;
        PointCloudHandle oldHandle = null;
        Traverser t;
        PointCloudConverter converter;

        public PathCamera pathCam;

        bool createdBuffers = false;

        List<AABB> boxesToDraw = null;

        #region rendering
        Material mat;
        #endregion

        Queue<OctreeGeometryNode> nodesToRender;
        Queue<OctreeGeometryNode> nodesToDelete;
        object queueLock;
        public static bool debug = true;
        public static LRU Cache = new LRU();

        public void Awake()
        {
            mat = new Material(Shader.Find("Custom/DefaultPoint"));
            queueLock = new object();
            converter = new PointCloudConverter();
        }

        public void Update()
        {
            cacheSize = PointCloudRenderer.Cache?.Size ?? 0;
            cachePoints = PointCloudRenderer.Cache?.NumPoints ?? 0;
            converterStatus = converter.Status switch
            {
                PointCloudConverter.ConversionStatus.CREATED => "Created",
                PointCloudConverter.ConversionStatus.STARTING => "Starting",
                PointCloudConverter.ConversionStatus.DECIMATING => "Decimating",
                PointCloudConverter.ConversionStatus.CONVERTING => "Converting",
                PointCloudConverter.ConversionStatus.DONE => "Done",
                PointCloudConverter.ConversionStatus.ABORTED => "Aborted",
            };

            if (handle == null)
            {
                oldHandle = handle;
                return;
            }

            if (handle != oldHandle)    // New data put in
            {
                if (!handle.Converted)
                    converter.Start(handle, decimatedCloudSize);
                else
                {
                    converted = true;
                    InitializeRendering();
                }
            }

            if (!handle.Converted && converter.Status == PointCloudConverter.ConversionStatus.DONE)
            {
                handle.Converted = true;
                converted = true;
                InitializeRendering();
            }

            if (handle.Converted)
                t.SetCamera(cam ?? Camera.current ?? Camera.main, transform.localToWorldMatrix);

            oldHandle = handle;
        }

        public void InitializeRendering()
        {
            string fullOutPath = Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(handle.path);
            geom = OctreeLoader.Load(fullOutPath + "/metadata.json");

            t = new Traverser(geom, geom.loader, this, pointBudget);
            t.Start();
        }

        public (Queue<OctreeGeometryNode>, Queue<OctreeGeometryNode>) SetQueues(Queue<OctreeGeometryNode> renderQueue, Queue<OctreeGeometryNode> deleteQueue)
        {
            lock (queueLock)
            {
                Queue<OctreeGeometryNode> oldRenderQueue = nodesToRender;
                nodesToRender = renderQueue;

                Queue<OctreeGeometryNode> oldDeleteQueue = nodesToDelete;
                nodesToDelete = deleteQueue;
                return (oldRenderQueue, oldDeleteQueue);
            }
        }

        public void RenderNode(OctreeGeometryNode node)
        {
            if (!node.loaded)
                return;

            if (!node.Created && !node.Create())
                Debug.LogError("Issue in node creation!");

            OctreeGeometry geom = node.octreeGeometry;
            Material m = new Material(Shader.Find("Custom/DefaultPoint")); ;
            m.hideFlags = HideFlags.DontSave;

            if (geom.posBuffer == null) // In case disposed during queue traversal
                return;

            boxesToDraw.Add(geom.boundingBox);

            m.SetVector("_Offset", geom.boundingBox.Min);
            m.SetBuffer("_Positions", geom.posBuffer);
            m.SetBuffer("_Colors", geom.colBuffer);
            m.SetFloat("_PointSize", pointSize);
            m.SetMatrix("_Transform", transform.localToWorldMatrix);
            m.SetPass(0);

            try
            {
                visiblePointCount += geom.posBuffer.count;
                visibleNodeCount++;

                Graphics.DrawProceduralNow(MeshTopology.Points, geom.posBuffer.count, 1);
            }
            catch (System.Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        public void OnRenderObject()
        {
            if (handle == null)
                return;

            if (handle.Converted)
                DrawConvertedCloud();  // Full draw call with traverser
            else if (converter.DecimationFinished)
            {
                if (decimatedPosBuffer == null)
                    (decimatedPosBuffer, decimatedColBuffer) = converter.GetDecimatedBuffers();
                DrawDecimatedCloud();   // Just decimated point cloud
            }
            else
                boxesToDraw = null; // Neither decimation nor conversion done, don't 
        }

        public void DrawDecimatedCloud()
        {
            boxesToDraw = null;

            mat.hideFlags = HideFlags.DontSave;
            mat.SetBuffer("_Positions", decimatedPosBuffer);
            mat.SetBuffer("_Colors", decimatedColBuffer);
            mat.SetFloat("_PointSize", pointSize);
            mat.SetMatrix("_Transform", transform.localToWorldMatrix);
            mat.SetPass(0);

            Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
        }

        public void DrawConvertedCloud()
        {
            if (nodesToRender == null)
                return;

            visiblePointCount = 0;
            visibleNodeCount = 0;
            loadedNodeCount = NodeLoader.numNodesLoaded;
            loadingNodeCount = NodeLoader.numNodesLoading;

            if (boxesToDraw != null)
                boxesToDraw.Clear();
            else
                boxesToDraw = new List<AABB>();

            Queue<OctreeGeometryNode> frameRenderQueue;
            Queue<OctreeGeometryNode> frameDeleteQueue;
            lock (queueLock)
            {
                frameRenderQueue = new Queue<OctreeGeometryNode>(nodesToRender);
                frameDeleteQueue = new Queue<OctreeGeometryNode>(nodesToDelete);
            }

            if (frameRenderQueue != null)
            {
                if (frameRenderQueue.Count < 100)
                    Debug.Log("smol");
                while (frameRenderQueue.Count > 0)
                    RenderNode(frameRenderQueue.Dequeue());
            }

            if (frameDeleteQueue != null)
                while (frameDeleteQueue.Count > 0)
                {
                    OctreeGeometryNode node = frameDeleteQueue.Dequeue();
                    if (node.loaded)
                    {
                        if (!node.Dispose())
                            Debug.LogError($"Dispose failed! Loaded: #{node.loaded}");
                        if (node.name == "r")
                            Debug.Log("Deleting r!");
                        lock (PointCloudRenderer.Cache)
                        {
                            PointCloudRenderer.Cache.Insert(node);
                        }
                    }
                }
        }


        public void OnDrawGizmos()
        {
#if UNITY_EDITOR
            // Ensure continuous Update calls.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
#endif
        }

        public void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            if (cam)
            {
                Gizmos.matrix = cam.transform.localToWorldMatrix;
                Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView, cam.farClipPlane, cam.nearClipPlane, cam.aspect);
            }

            if (boxesToDraw != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                foreach (AABB bbox in boxesToDraw)
                {
                    Gizmos.DrawWireCube(bbox.Center, bbox.Size);
                }
            }
        }
    }
}