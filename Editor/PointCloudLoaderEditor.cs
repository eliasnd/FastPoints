//C# Example (LookAtPointEditor.cs)
using UnityEngine;
using UnityEditor;

namespace FastPoints
{
    [CustomEditor(typeof(PointCloudLoader))]
    [CanEditMultipleObjects]
    public class PointCloudLoaderEditor : Editor
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
        SerializedProperty converted;

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
            converted = serializedObject.FindProperty("converted");
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
                EditorGUILayout.LabelField($"Nodes loaded: {loadedNodeCount.intValue}");
                EditorGUILayout.LabelField($"Nodes loading: {loadingNodeCount.intValue}");
            }
        }
    }
}