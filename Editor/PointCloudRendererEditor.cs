//C# Example (LookAtPointEditor.cs)
using UnityEngine;
using UnityEditor;

namespace FastPoints
{
    [CustomEditor(typeof(PointCloudRenderer))]
    [CanEditMultipleObjects]
    public class PointCloudRendererEditor : Editor
    {
        bool showConverterOptions = false;
        bool showRenderingDetails = false;

        SerializedProperty handle;
        SerializedProperty decimatedCloudSize;
        SerializedProperty pointSize;
        SerializedProperty cam;
        SerializedProperty pointBudget;
        SerializedProperty drawGizmos;
        SerializedProperty cachePointBudget;

        SerializedProperty visiblePointCount;
        SerializedProperty visibleNodeCount;
        SerializedProperty loadedNodeCount;
        SerializedProperty loadingNodeCount;
        SerializedProperty queuedActionCount;
        SerializedProperty cacheSize;
        SerializedProperty converting;
        SerializedProperty decimated;
        SerializedProperty converted;
        SerializedProperty converterStatus;
        SerializedProperty converterProgress;
        SerializedProperty cachePoints;
        SerializedProperty maxNodesToLoad;
        SerializedProperty maxNodesToRender;
        SerializedProperty showDecimatedCloud;
        SerializedProperty converterParams;
        SerializedProperty minNodeSize;

        // Converter params
        SerializedProperty source;
        SerializedProperty outdir;
        SerializedProperty method;
        SerializedProperty encoding;
        SerializedProperty chunkMethod;

        void OnEnable()
        {
            handle = serializedObject.FindProperty("handle");
            decimatedCloudSize = serializedObject.FindProperty("decimatedCloudSize");
            pointSize = serializedObject.FindProperty("pointSize");
            cam = serializedObject.FindProperty("cam");
            pointBudget = serializedObject.FindProperty("pointBudget");
            drawGizmos = serializedObject.FindProperty("drawGizmos");
            cachePointBudget = serializedObject.FindProperty("cachePointBudget");
            maxNodesToLoad = serializedObject.FindProperty("maxNodesToLoad");
            maxNodesToRender = serializedObject.FindProperty("maxNodesToRender");
            minNodeSize = serializedObject.FindProperty("minNodeSize");


            visiblePointCount = serializedObject.FindProperty("visiblePointCount");
            visibleNodeCount = serializedObject.FindProperty("visibleNodeCount");
            loadedNodeCount = serializedObject.FindProperty("loadedNodeCount");
            loadingNodeCount = serializedObject.FindProperty("loadingNodeCount");
            queuedActionCount = serializedObject.FindProperty("queuedActionCount");
            cacheSize = serializedObject.FindProperty("cacheSize");
            cachePoints = serializedObject.FindProperty("cachePoints");
            converting = serializedObject.FindProperty("converting");
            decimated = serializedObject.FindProperty("decimated");
            converted = serializedObject.FindProperty("converted");
            converterStatus = serializedObject.FindProperty("converterStatus");
            converterProgress = serializedObject.FindProperty("converterProgress");
            showDecimatedCloud = serializedObject.FindProperty("showDecimatedCloud");

            source = serializedObject.FindProperty("source");
            outdir = serializedObject.FindProperty("outdir");
            method = serializedObject.FindProperty("method");
            encoding = serializedObject.FindProperty("encoding");
            chunkMethod = serializedObject.FindProperty("chunkMethod");

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(handle);
            if (decimated.boolValue)
            {
                EditorGUILayout.PropertyField(pointSize);
            }

            if (converted.boolValue)
            {
                EditorGUILayout.PropertyField(pointBudget);
                EditorGUILayout.PropertyField(pointSize);
                EditorGUILayout.LabelField($"Points rendering: {visiblePointCount.intValue}");
                showRenderingDetails = EditorGUILayout.Foldout(showRenderingDetails, "Advanced rendering options");
                if (showRenderingDetails)
                {
                    EditorGUILayout.PropertyField(cam, new GUIContent("Camera to render to"));
                    EditorGUILayout.PropertyField(maxNodesToRender, new GUIContent("Max nodes to create / frame"));
                    EditorGUILayout.PropertyField(maxNodesToLoad, new GUIContent("Max nodes to load / frame"));
                    EditorGUILayout.PropertyField(minNodeSize, new GUIContent("Smallest node size to render"));
                    EditorGUILayout.PropertyField(cachePointBudget);
                    EditorGUILayout.PropertyField(drawGizmos);

                    EditorGUILayout.LabelField($"Nodes rendering: {visibleNodeCount.intValue}");
                    EditorGUILayout.LabelField($"Nodes loading: {loadingNodeCount.intValue}");
                    EditorGUILayout.LabelField($"Points in cache: {cachePoints.intValue}");
                    EditorGUILayout.LabelField($"Cached nodes: {cacheSize.intValue}");
                }
            }
            else
            {
                showConverterOptions = EditorGUILayout.Foldout(showConverterOptions, "Advanced conversion options");
                if (showConverterOptions)
                {
                    GUI.enabled = !converting.boolValue;
                    EditorGUILayout.PropertyField(decimatedCloudSize);
                    EditorGUILayout.PropertyField(source);
                    EditorGUILayout.PropertyField(outdir, new GUIContent("Target"));
                    EditorGUILayout.PropertyField(method);
                    EditorGUILayout.PropertyField(encoding);
                    EditorGUILayout.PropertyField(chunkMethod);
                    GUI.enabled = true;
                }
                EditorGUILayout.LabelField($"Converter status: {converterStatus.stringValue}");
                EditorGUILayout.LabelField($"Converter progress: {(converterProgress.floatValue == -1f ? "N/A" : $"{(int)(converterProgress.floatValue * 100)} %")}");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}