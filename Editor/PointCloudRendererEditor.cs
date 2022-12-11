//C# Example (LookAtPointEditor.cs)
using UnityEngine;
using UnityEditor;

namespace FastPoints
{
    [CustomEditor(typeof(PointCloudRenderer))]
    [CanEditMultipleObjects]
    public class PointCloudRendererEditor : Editor
    {
        SerializedProperty handle;
        SerializedProperty decimatedCloudSize;
        SerializedProperty pointSize;
        SerializedProperty cam;
        SerializedProperty pointBudget;
        SerializedProperty drawGizmos;

        SerializedProperty visiblePointCount;
        SerializedProperty visibleNodeCount;
        SerializedProperty loadedNodeCount;
        SerializedProperty loadingNodeCount;
        SerializedProperty queuedActionCount;
        SerializedProperty cacheSize;
        SerializedProperty converted;
        SerializedProperty converterStatus;
        SerializedProperty cachePoints;

        void OnEnable()
        {
            handle = serializedObject.FindProperty("handle");
            decimatedCloudSize = serializedObject.FindProperty("decimatedCloudSize");
            pointSize = serializedObject.FindProperty("pointSize");
            cam = serializedObject.FindProperty("cam");
            pointBudget = serializedObject.FindProperty("pointBudget");
            drawGizmos = serializedObject.FindProperty("drawGizmos");

            visiblePointCount = serializedObject.FindProperty("visiblePointCount");
            visibleNodeCount = serializedObject.FindProperty("visibleNodeCount");
            loadedNodeCount = serializedObject.FindProperty("loadedNodeCount");
            loadingNodeCount = serializedObject.FindProperty("loadingNodeCount");
            queuedActionCount = serializedObject.FindProperty("queuedActionCount");
            cacheSize = serializedObject.FindProperty("cacheSize");
            cachePoints = serializedObject.FindProperty("cachePoints");
            converted = serializedObject.FindProperty("converted");
            converterStatus = serializedObject.FindProperty("converterStatus");
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

            serializedObject.ApplyModifiedProperties();

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
                EditorGUILayout.LabelField($"Converter status: {converterStatus.stringValue}");
            }
        }
    }
}