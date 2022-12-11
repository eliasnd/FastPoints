using UnityEngine;
using Unity.Collections;
using System.Threading.Tasks;
using System.IO;

namespace FastPoints
{
    // Runs conversion on a separate thread, updating status to allow main thread to initialize rendering primitives as needed
    public class PointCloudConverter
    {
        object statusLock = new object();
        public enum ConversionStatus { CREATED, STARTING, DECIMATING, CONVERTING, ABORTED, DONE };
        ConversionStatus status;
        public ConversionStatus Status
        {
            get
            {
                lock (statusLock)
                {
                    return status;
                }
            }
        }

        public float progress { get; private set; } // Progress in current conversion phase - ranges from 0.0 to 1.0

        public bool IsRunning
        {
            get
            {
                return (status == ConversionStatus.STARTING || status == ConversionStatus.DECIMATING || status == ConversionStatus.CONVERTING);
            }
        }

        public bool DecimationFinished
        {
            get { return (status == ConversionStatus.CONVERTING || status == ConversionStatus.DONE); }
        }

        object arrayLock = new object();
        NativeArray<Vector3> decimatedPoints;
        NativeArray<Color32> decimatedColors;
        int decimatedCloudSize = 1000000;
        bool debug = false;

        // Write decimated data to passed buffers
        // Must be called from main thread
        // Returns true if buffer population successful, false otherwise
        public (ComputeBuffer, ComputeBuffer) GetDecimatedBuffers(bool disposeArrays = true)
        {
            if (status != ConversionStatus.CONVERTING && status != ConversionStatus.DONE)
                throw new System.Exception("Decimation not finished");

            ComputeBuffer decimatedPosBuffer = new ComputeBuffer(decimatedCloudSize, 12);
            ComputeBuffer decimatedColBuffer = new ComputeBuffer(decimatedCloudSize, 4);

            lock (arrayLock)
            {
                decimatedPosBuffer.SetData(decimatedPoints);
                decimatedColBuffer.SetData(decimatedColors);

                if (disposeArrays)
                {
                    decimatedPoints.Dispose();
                    decimatedColors.Dispose();
                }
            }

            return (decimatedPosBuffer, decimatedColBuffer);
        }

        public async void Start(PointCloudHandle handle, int decimatedCloudSize = 1000000, bool debug = false)
        {
            this.decimatedCloudSize = decimatedCloudSize;
            this.debug = debug;

            lock (statusLock)
            {
                status = ConversionStatus.STARTING;
            }

            string fullInPath = System.IO.Directory.GetCurrentDirectory() + "/" + handle.path;
            string fullOutPath = System.IO.Directory.GetCurrentDirectory() + "/ConvertedClouds/" + Path.GetFileNameWithoutExtension(handle.path);

            decimatedPoints = new NativeArray<Vector3>(decimatedCloudSize, Allocator.Persistent);
            decimatedColors = new NativeArray<Color32>(decimatedCloudSize, Allocator.Persistent);

            lock (statusLock)
            {
                status = ConversionStatus.DECIMATING;
            }

            await Task.Run(() =>
            {
                FastPointsNativeApi.PopulateDecimatedCloud(
                    fullInPath,
                    decimatedPoints,
                    decimatedColors,
                    decimatedCloudSize
                );

                lock (statusLock)
                {
                    status = ConversionStatus.CONVERTING;
                }

                FastPointsNativeApi.RunConverter(fullInPath, fullOutPath);

                lock (statusLock)
                {
                    status = ConversionStatus.DONE;
                }
            });
        }

        public void Abort()
        {
            lock (statusLock)
            {
                status = ConversionStatus.ABORTED;
            }
        }
    }
}