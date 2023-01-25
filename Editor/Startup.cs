using UnityEngine;
using UnityEditor;

namespace FastPoints
{
    [InitializeOnLoad]
    public class Startup
    {
        static Startup()
        {
            GameObject go = new GameObject();
            go.name = "AppMonitor";
            go.AddComponent<AppMonitor>();

            FastPointsNativeApi.InitializeConverter();
        }
    }

    public class AppMonitor : MonoBehaviour
    {
        public void Awake()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.LockReloadAssemblies();
#endif
        }

        void OnApplicationQuit()
        {
            //Prevent editor from reloading dlls files during the gameplay
#if UNITY_EDITOR
            UnityEditor.EditorApplication.UnlockReloadAssemblies();
#endif
        }
    }
}