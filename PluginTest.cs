using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    public class PluginTest : MonoBehaviour
    {    
        public string source;

        //The string dll should match the name of your plugin
        #if UNITY_IOS    
            const string dll = "__Internal";
        #else    
            const string dll = "fastpoints-native";
        #endif    
        // [DllImport(dll)]    
        // private static extern void SeedRandomizer();    
        // [DllImport(dll)]    
        // private static extern int DieRoll(int sides);    
        // [DllImport(dll)]    
        // private static extern float Add(float a, float b);    
        // [DllImport(dll)]    
        // private static extern float Random();    
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LoggingCallback(string message);   

        [DllImport(dll)]    
        private static extern IntPtr HelloWorld();   

        [DllImport(dll)]  
        private static extern IntPtr TestCallback(LoggingCallback cb); 

        [DllImport(dll)]    
        private static extern void RunConverter(string source, string outdir, LoggingCallback cb); 

        [DllImport(dll)]
        unsafe private static extern void PopulateDecimatedCloud(string source, void* points, void* colors, int decimatedSize, LoggingCallback cb);

        private static void LogMessage(string message) {
            Debug.Log(message);
        }

        void Start() { 
            Debug.Log("Start plugin");
            OctreeLoader.Load(source);
        }
    }
}