using UnityEngine;
using System.IO;
using System.Collections.Generic;
using UnityEditor;

using Debug = UnityEngine.Debug;

namespace FastPoints
{
    [ExecuteInEditMode]
    public class PointCloudRenderer : MonoBehaviour
    {
        public static LRU Cache = new LRU();

        #region public
        public PointCloudHandle handle;
        public int decimatedCloudSize = 1000000;
        public float pointSize = 1.5f;
        public Camera cam = null;
        public int pointBudget = 1000000;
        public int cachePointBudget = 5000000;
        public bool drawGizmos = true;
        public int maxNodesToLoad = 10;
        public int maxNodesToRender = 30;
        public bool showDecimatedCloud = false;
        #endregion

        #region dirty_trackers
        PointCloudHandle oldHandle = null;
        int oldPointBudget;
        int oldCachePointBudget = 5000000;
        int oldMaxNodesToLoad = 10;
        int oldMaxNodesToRender = 30;
        float oldMinNodeSize = 1;
        #endregion

        # region converter_params
        [SerializeField]
        string source;
        [SerializeField]
        string outdir;
        [SerializeField]
        string method = "poisson";
        [SerializeField]
        string encoding = "default";
        [SerializeField]
        string chunkMethod = "LASZIP";
        [SerializeField]
        string converterStatus;
        [SerializeField]
        float converterProgress;
        [SerializeField]
        bool converting = false;
        [SerializeField]
        bool decimated;
        [SerializeField]
        bool converted;
        # endregion

        #region rendering_params
        [SerializeField]
        int visiblePointCount;
        [SerializeField]
        int visibleNodeCount;
        [SerializeField]
        int loadedNodeCount;
        [SerializeField]
        int loadingNodeCount;
        [SerializeField]
        float minNodeSize = 1;
        #endregion

        #region cache
        [SerializeField]
        int cacheSize;
        [SerializeField]
        int cachePoints;
        #endregion

        #region converter
        PointCloudConverter converter;
        ComputeBuffer decimatedPosBuffer = null;
        ComputeBuffer decimatedColBuffer = null;
        #endregion

        #region rendering
        OctreeGeometry geom;
        Traverser t;
        Material mat;
        object queueLock;
        Queue<OctreeGeometryNode> nodesToRender;
        Queue<OctreeGeometryNode> nodesToDelete;

        AppMonitor appMonitor;
        #endregion

        #region debug
        List<AABB> boxesToDraw = null;
        Camera cameraToDraw = null;

        public static bool debug = false;
        #endregion

        public Camera GetCurrentCamera()
        {
            if (cam != null)
                return cam;

            if (appMonitor.MostRecentRenderWindow == AppMonitor.RenderWindow.SCENE_VIEW)
                return SceneView.lastActiveSceneView.camera;
            else
                return Camera.main;
        }

        public void Awake()
        {
            mat = new Material(Shader.Find("Custom/DefaultPoint"));
            queueLock = new object();
            converter = new PointCloudConverter();
            appMonitor = FindObjectOfType<AppMonitor>();
        }

        public void Update()
        {
            // Read display params from objects

            cacheSize = PointCloudRenderer.Cache != null ? PointCloudRenderer.Cache.Size : 0;
            cachePoints = PointCloudRenderer.Cache != null ? PointCloudRenderer.Cache.NumPoints : 0;
            converterStatus = converter.Status switch
            {
                PointCloudConverter.ConversionStatus.CREATED => "Created",
                PointCloudConverter.ConversionStatus.STARTING => "Starting",
                PointCloudConverter.ConversionStatus.DECIMATING => "Decimating",
                PointCloudConverter.ConversionStatus.WAITING => "Waiting",
                PointCloudConverter.ConversionStatus.CONVERTING => "Converting",
                PointCloudConverter.ConversionStatus.DONE => "Done",
                PointCloudConverter.ConversionStatus.ABORTED => "Aborted",
            };
            converterProgress = converter.Progress;

            // Case on passed handle

            if (handle == null)     // No data
            {
                oldHandle = handle;
                return;
            }
            else if (handle != oldHandle)    // New data put in
            {
                if (!handle.Converted) {
                    converter.Start((source == null || source == "") ? handle.path : source, outdir, method, encoding, chunkMethod, decimatedCloudSize);
                    converting = true;
                }
                else
                {
                    converting = false;
                    converted = true;
                    InitializeRendering();
                }
                oldHandle = handle;
            }
            else if (!handle.Converted && converter.Status == PointCloudConverter.ConversionStatus.DONE)    // Not new data, conversion finished but handle not updated
            {
                handle.Converted = true;
                converted = true;
                AssetDatabase.SaveAssets();
                InitializeRendering();
            }
            else if (handle.Converted)  // Not new data, already converted
            {
                Camera c = GetCurrentCamera();
                if (c != null)
                {
                    if (debug)
                        Debug.Log("Drawing with camera " + c.GetInstanceID());
                    t.SetCamera(c, transform.localToWorldMatrix);
                    cameraToDraw = c;
                }

                if (maxNodesToLoad != oldMaxNodesToLoad || maxNodesToRender != oldMaxNodesToRender)
                    t.SetPerFrameNodeCounts(maxNodesToLoad, maxNodesToRender);
            }

            // Check dirty trackers

            if (pointBudget != oldPointBudget && t != null) {
                t.SetPointBudget(pointBudget);
                oldPointBudget = pointBudget;
            }

            if (cachePointBudget != oldCachePointBudget)
            {
                Cache.pointLoadLimit = cachePointBudget;
                oldCachePointBudget = cachePointBudget;
            }

            if (minNodeSize != oldMinNodeSize && minNodeSize > 0) {
                t.SetMinNodeSize(minNodeSize);
                oldMinNodeSize = minNodeSize;
            }
        }

        public void InitializeRendering()
        {
            string fullOutPath = Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(handle.path);
            geom = OctreeLoader.Load(fullOutPath + "/metadata.json");

            t = new Traverser(geom, this, pointBudget);
            t.Start();
            t.SetCamera(Camera.main, transform.localToWorldMatrix);
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

            if (geom.posBuffer == null) // In case disposed during queue traversal 
                return;

            boxesToDraw.Add(geom.boundingBox);

            mat.SetVector("_Scale", new Vector3(1f / geom.scale[0], 1f / geom.scale[1], 1f / geom.scale[2]));
            mat.SetVector("_Offset", geom.boundingBox.Min + this.geom.offset);
            mat.SetBuffer("_Positions", geom.posBuffer);
            mat.SetBuffer("_Colors", geom.colBuffer);
            mat.SetMatrix("_Transform", transform.localToWorldMatrix);
            mat.SetPass(0);

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
            {
                if (showDecimatedCloud)
                    DrawDecimatedCloud();
                else
                    DrawConvertedCloud();  // Full draw call with traverser
            }
            else if (converter.DecimationFinished)
            {
                if (decimatedPosBuffer == null)
                    (decimatedPosBuffer, decimatedColBuffer) = converter.GetDecimatedBuffers();
                decimated = true;   
                DrawDecimatedCloud();   // Just decimated point cloud
            }
            else
                boxesToDraw = null; // Neither decimation nor conversion done, don't 
        }

        public void DrawDecimatedCloud()
        {
            boxesToDraw = null;

            mat.hideFlags = HideFlags.DontSave;
            mat.SetVector("_Scale", new Vector3(1f, 1f, 1f));
            mat.SetVector("_Offset", new Vector3(0f, 0f, 0f));
            mat.SetBuffer("_Positions", decimatedPosBuffer);
            mat.SetBuffer("_Colors", decimatedColBuffer);
            mat.SetFloat("_PointSize", pointSize);
            mat.SetMatrix("_Transform", transform.localToWorldMatrix);
            mat.SetPass(0);

            Graphics.DrawProceduralNow(MeshTopology.Points, decimatedCloudSize, 1);
        }

        public void DrawConvertedCloud()
        {
            mat.SetFloat("_PointSize", pointSize);

            if (nodesToRender == null)
                return;

            visiblePointCount = 0;
            visibleNodeCount = 0;
            lock (NodeLoader.loaderLock)
            {
                loadedNodeCount = NodeLoader.nodesLoaded;
                loadingNodeCount = NodeLoader.nodesLoading;
            }

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

            if (frameDeleteQueue != null)
                while (frameDeleteQueue.Count > 0)
                {
                    OctreeGeometryNode node = frameDeleteQueue.Dequeue();
                    if (node.loaded)
                    {
                        node.Dispose();
                        lock (PointCloudRenderer.Cache)
                        {
                            PointCloudRenderer.Cache.Insert(node);
                        }
                    }
                }

            int renderQueueSize = frameRenderQueue?.Count ?? 0;
            int newlyCreatedNodes = 0;

            if (frameRenderQueue != null)
            {
                while (frameRenderQueue.Count > 0)
                {
                    OctreeGeometryNode n = frameRenderQueue.Dequeue();
                    if (!n.Created)
                        newlyCreatedNodes++;

                    RenderNode(n);
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

            if (cameraToDraw != null)
            {
                Gizmos.matrix = cameraToDraw.transform.localToWorldMatrix;
                Gizmos.DrawFrustum(Vector3.zero, cameraToDraw.fieldOfView, cameraToDraw.farClipPlane, cameraToDraw.nearClipPlane, cameraToDraw.aspect);
            }

            if (boxesToDraw != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                foreach (AABB bbox in boxesToDraw)
                {
                    Vector3 bboxCenter = (bbox.Center + geom.offset);
                    Gizmos.DrawWireCube(new Vector3(bboxCenter.x / geom.scale[0], bboxCenter.y / geom.scale[1], bboxCenter.z / geom.scale[2]), new Vector3(bbox.Size.x / geom.scale[0], bbox.Size.y / geom.scale[1], bbox.Size.z / geom.scale[2]));
                }

            }
        }

        public void OnDisable()
        {
            if (decimatedPosBuffer != null)
                decimatedPosBuffer.Release();

            if (decimatedColBuffer != null)
                decimatedColBuffer.Release();

            if (!handle.Converted && converter.Status != PointCloudConverter.ConversionStatus.DONE)
                converter.RemovePointCloud();
        }
    }
}