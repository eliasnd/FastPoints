// FastPoints
// Copyright (C) 2023  Elias Neuman-Donihue

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using UnityEngine;
using Unity.Collections;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

namespace FastPoints
{

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProgressCallback(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void StatusCallback(int status);

    public class PointCloudConverter
    {
        private ProgressCallback pcb;
        private StatusCallback scb;

        object statusLock = new object();
        public enum ConversionStatus { CREATED, STARTING, DECIMATING, WAITING, CONVERTING, DONE, ABORTED };
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

        object progressLock = new object();
        float progress;     // Progress in current step - ranges from 0.0 to 1.0
        public float Progress
        {
            get
            {
                lock (progressLock)
                {
                    return progress;
                }
            }
        }

        public bool DecimationFinished
        {
            get { return (status == ConversionStatus.WAITING || status == ConversionStatus.CONVERTING || status == ConversionStatus.DONE); }
        }

        object arrayLock = new object();
        NativeArray<Vector3> decimatedPoints;
        NativeArray<Color32> decimatedColors;
        int decimatedCloudSize = 1000000;
        bool debug = false;

        string source;
        string dest;

        void UpdateStatus(int status)
        {
            lock (statusLock)
            {
                this.status = (ConversionStatus)status;
            }
        }

        void UpdateProgress(float progress)
        {
            lock (progressLock)
            {
                this.progress = progress;
            }
        }

        // Write decimated data to passed buffers
        // Must be called from main thread
        // Returns true if buffer population successful, false otherwise
        public (ComputeBuffer, ComputeBuffer) GetDecimatedBuffers(bool disposeArrays = true)
        {
            ConversionStatus currStatus = Status;
            if (currStatus != ConversionStatus.WAITING && currStatus != ConversionStatus.CONVERTING && currStatus != ConversionStatus.DONE)
                throw new System.Exception("Decimation not finished");

            ComputeBuffer decimatedPosBuffer = new ComputeBuffer(decimatedCloudSize, 12);
            ComputeBuffer decimatedColBuffer = new ComputeBuffer(decimatedCloudSize, 4);

            decimatedPosBuffer.SetData(decimatedPoints);
            decimatedColBuffer.SetData(decimatedColors);

            if (disposeArrays)
            {
                decimatedPoints.Dispose();
                decimatedColors.Dispose();
            }

            return (decimatedPosBuffer, decimatedColBuffer);
        }

        public void Start(string source, string outdir, string method = "poisson", string encoding = "default", string chunkMethod = "LASZIP", int decimatedCloudSize = 1000000, bool debug = false)
        {
            this.decimatedCloudSize = decimatedCloudSize;
            this.debug = debug;
            pcb = UpdateProgress;
            scb = UpdateStatus;

            UpdateStatus(0);
            UpdateProgress(-1f);

            this.source = System.IO.Directory.GetCurrentDirectory() + "/" + source;
            string dest = System.IO.Directory.GetCurrentDirectory() + "/" + (outdir == null || outdir == "" ? "ConvertedClouds/" : outdir) + Path.GetFileNameWithoutExtension(source);

            decimatedPoints = new NativeArray<Vector3>(decimatedCloudSize, Allocator.Persistent);
            decimatedColors = new NativeArray<Color32>(decimatedCloudSize, Allocator.Persistent);

            FastPointsNativeApi.AddPointCloud(
                this.source,
                decimatedPoints,
                decimatedColors,
                decimatedCloudSize,
                dest,
                method,
                encoding,
                chunkMethod,
                scb,
                pcb
            );
        }

        public void RemovePointCloud()
        {
            if (source != null)
                FastPointsNativeApi.RemovePointCloud(source);
        }
    }
}