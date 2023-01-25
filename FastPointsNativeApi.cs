using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace FastPoints
{
    public class FastPointsNativeApi
    {
        static bool converterRunning = false;
        static object mtx = new object();
        static Dictionary<string, IntPtr> nativeHandles = null;    // Maps source paths to native pointers

        //The string dll should match the name of your plugin
#if UNITY_IOS
            const string dll = "__Internal";
#else
        const string dll = "fastpoints-native";
#endif

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LoggingCallback(string message);


        [DllImport(dll)]
        private static extern void InitializeConverter(LoggingCallback cb);

        [DllImport(dll)]
        private static extern void StopConverter();

        [DllImport(dll)]
        private unsafe static extern IntPtr AddPointCloud(string source, void* points, void* colors, int decimated_size, string outdir, string method, string encoding, string chunk_method, StatusCallback scb, ProgressCallback pcb);

        [DllImport(dll)]
        private static extern bool RemovePointCloud(IntPtr handle);

        private static void LogMessage(string message)
        {
            Debug.Log(message);
        }

        public static void InitializeConverter()
        {
            lock (mtx)
            {
                if (converterRunning)
                    throw new Exception("Error initializing converter: converter already running!");

                nativeHandles = new Dictionary<string, IntPtr>();
                InitializeConverter(LogMessage);
                converterRunning = true;
            }
        }

        public static void AbortConverter()
        {
            lock (mtx)
            {
                if (!converterRunning)
                    throw new Exception("Error stopping converter: converter not running!");
                nativeHandles = null;
                StopConverter();
                converterRunning = false;
            }

        }

        public static void AddPointCloud(string source, NativeArray<Vector3> points, NativeArray<Color32> colors, int decimatedSize, string outdir, string method, string encoding, string chunk_method, StatusCallback statusCallback, ProgressCallback progressCallback)
        {
            lock (mtx)
            {
                unsafe
                {
                    void* ptr1 = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(points);
                    void* ptr2 = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(colors);

                    IntPtr ptr = AddPointCloud(
                        source,
                        ptr1,
                        ptr2,
                        decimatedSize,
                        outdir,
                        method,
                        encoding,
                        chunk_method,
                        statusCallback,
                        progressCallback
                    );

                    nativeHandles.Add(source, ptr);
                }
            }
        }

        public static void RemovePointCloud(string source)
        {
            lock (mtx)
            {
                IntPtr nativePtr;
                if (nativeHandles.TryGetValue(source, out nativePtr))
                {
                    nativeHandles.Remove(source);
                    RemovePointCloud(nativePtr);
                }
                else
                    throw new Exception("Point cloud not added!");
            }
        }
    }
}