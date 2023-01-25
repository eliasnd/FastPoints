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
        SerializedProperty converted;
        SerializedProperty converterStatus;
        SerializedProperty converterProgress;
        SerializedProperty cachePoints;
        SerializedProperty maxNodesToLoad;
        SerializedProperty maxNodesToRender;
        SerializedProperty showDecimatedCloud;
        SerializedProperty converterParams;

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


            visiblePointCount = serializedObject.FindProperty("visiblePointCount");
            visibleNodeCount = serializedObject.FindProperty("visibleNodeCount");
            loadedNodeCount = serializedObject.FindProperty("loadedNodeCount");
            loadingNodeCount = serializedObject.FindProperty("loadingNodeCount");
            queuedActionCount = serializedObject.FindProperty("queuedActionCount");
            cacheSize = serializedObject.FindProperty("cacheSize");
            cachePoints = serializedObject.FindProperty("cachePoints");
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
            EditorGUILayout.PropertyField(decimatedCloudSize);
            EditorGUILayout.PropertyField(pointSize);
            EditorGUILayout.PropertyField(cam);
            EditorGUILayout.PropertyField(pointBudget);
            EditorGUILayout.PropertyField(drawGizmos);
            EditorGUILayout.PropertyField(cachePointBudget);
            EditorGUILayout.PropertyField(maxNodesToLoad);
            EditorGUILayout.PropertyField(maxNodesToRender);
            EditorGUILayout.PropertyField(showDecimatedCloud);

            if (converted.boolValue)
            {
                EditorGUILayout.LabelField($"Points rendering: {visiblePointCount.intValue}");
                EditorGUILayout.LabelField($"Nodes rendering: {visibleNodeCount.intValue}");
                // EditorGUILayout.LabelField($"Nodes loaded: {loadedNodeCount.intValue}");
                EditorGUILayout.LabelField($"Nodes loading: {loadingNodeCount.intValue}");
                EditorGUILayout.LabelField($"Queued actions: {queuedActionCount.intValue}");
                EditorGUILayout.LabelField($"Cached nodes: {cacheSize.intValue}");
                EditorGUILayout.LabelField($"Points in cache: {cachePoints.intValue}");
            }
            else
            {

                showConverterOptions = EditorGUILayout.Foldout(showConverterOptions, "Advanced conversion options");
                if (showConverterOptions)
                {
                    EditorGUILayout.PropertyField(source);
                    EditorGUILayout.PropertyField(outdir, new GUIContent("Target"));
                    EditorGUILayout.PropertyField(method);
                    EditorGUILayout.PropertyField(encoding);
                    EditorGUILayout.PropertyField(chunkMethod);
                }
                EditorGUILayout.LabelField($"Converter status: {converterStatus.stringValue}");
                EditorGUILayout.LabelField($"Converter progress: {(converterProgress.floatValue == -1f ? "N/A" : $"{(int)(converterProgress.floatValue * 100)} %")}");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}