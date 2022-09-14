using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace FastPoints {
    public class FastPointsNativeApi
    {    
        //The string dll should match the name of your plugin
        #if UNITY_IOS    
            const string dll = "__Internal";
        #else    
            const string dll = "fastpoints-native";
        #endif    
        
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

        public static void PopulateDecimatedCloud(string source, NativeArray<Vector3> points, NativeArray<Color32> colors, int decimatedSize) {
            unsafe {
                void* ptr1 = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(points);
                void* ptr2 = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(colors);
                
                PopulateDecimatedCloud(
                    source, 
                    ptr1,
                    ptr2,
                    decimatedSize,
                    LogMessage
                );
            }
        }

        public static void RunConverter(string source, string outdir) {
            RunConverter(source, outdir, LogMessage);
        }
    }
}