using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

public class PluginTest : MonoBehaviour
{    
    public string source;
    public string outdir;
    Material mat;
    bool useDecimatedCloud = false;
    ComputeBuffer posBuffer;
    ComputeBuffer colBuffer;

    struct Color16 {
        public UInt16 r;
        public UInt16 g;
        public UInt16 b;
        public UInt16 a;

        public override string ToString() {
            return $"Color16({r}, {g}, {b}, {a})"
;        }
    }


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

    void Awake() {
        mat = new Material(Shader.Find("Custom/DefaultPoint"));
    }

    void Start() { 
        Debug.Log("Here!");
        // Debug.Log(Marshal.PtrToStringAnsi(TestCallback(LogMessage))); 
        // TestCallback(LogMessage);       
        // SeedRandomizer();        
        // Debug.Log(Add(3.2f, 4.5f).ToString());        
        // Debug.Log(DieRoll(20).ToString());        
        // Debug.Log(Random().ToString());        
        // Debug.Log(Marshal.PtrToStringAnsi(HelloThis()));
        int decimatedSize = 1000000;

        NativeArray<Vector3> decimatedPoints = new NativeArray<Vector3>(decimatedSize, Allocator.Persistent);
        NativeArray<Color32> decimatedColors = new NativeArray<Color32>(decimatedSize, Allocator.Persistent);

        // Task.Run(() => {
            Debug.Log("Start task");
            unsafe {
                Debug.Log("In unsafe");
                void* ptr1 = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(decimatedPoints);
                void* ptr2 = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(decimatedColors);
                Debug.Log("Got pointers");
                PopulateDecimatedCloud(
                    source, 
                    ptr1,
                    ptr2,
                    decimatedSize,
                    LogMessage
                );
                Debug.Log("Done unsafe");
            }
            posBuffer = new ComputeBuffer(decimatedSize, 12);
            posBuffer.SetData(decimatedPoints);
            colBuffer = new ComputeBuffer(decimatedSize, 4);
            colBuffer.SetData(decimatedColors);
            useDecimatedCloud = true;

            Debug.Log("Here");
            Debug.Log($"First point is {decimatedPoints[0].ToString()}");
            for (int i = 0; i < 100; i++)
                Debug.Log($"Got color {decimatedColors[i].ToString()}");

            // int maxVal = 0;
            // for (int i = 0; i < decimatedSize; i++) {
            //     maxVal = (int)(Mathf.Max(maxVal, decimatedColors[i].r));
            //     maxVal = (int)(Mathf.Max(maxVal, decimatedColors[i].g));
            //     maxVal = (int)(Mathf.Max(maxVal, decimatedColors[i].b));
            //     maxVal = (int)(Mathf.Max(maxVal, decimatedColors[i].a));
            // }
            // Debug.Log("Got max " + maxVal);
            decimatedPoints.Dispose();
            decimatedColors.Dispose();
            // RunConverter(source, outdir, LogMessage);
        // });
        // RunConverter(source, outdir, LogMessage);
        // Thread t = new Thread(RunConverter);
        // t.Start(source, outdir, LogMessage);    
    }

    public void OnRenderObject() {
        if (!useDecimatedCloud)
            return;

        mat.hideFlags = HideFlags.DontSave;
        mat.SetBuffer("_Positions", posBuffer);
        mat.SetBuffer("_Colors", colBuffer);
        mat.SetFloat("_PointSize", 1.5f);
        mat.SetMatrix("_Transform", transform.localToWorldMatrix);
        mat.SetPass(0);

        Graphics.DrawProceduralNow(MeshTopology.Points, 1000000, 1);
    }
}