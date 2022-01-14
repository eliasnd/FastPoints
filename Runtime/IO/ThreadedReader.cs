using UnityEngine;

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    public class ThreadedReader {
        string path;
        ConcurrentQueue<byte[]> queue;
        int maxLength = 150;
        int batchSize;
        int offset;
        int round;
        bool reading;
        bool sampling;
        public bool IsRunning { get { return activeThreads > 0; } }

        (ReadThreadParams p, Thread t)[] readThreads;
        (SampleThreadParams p, Thread t)[] sampleThreads;
        int threadCount = 10;
        int activeThreads;

        public ThreadedReader(string path, int batchSize, ConcurrentQueue<byte[]> queue, int offset=0, int round=0) {
            this.path = path;
            this.batchSize = batchSize;
            this.queue = queue;
            this.offset = offset;
            this.round = round;
        }

        public bool StartReading() {
            if (reading || sampling)
                return false;

            reading = true;

            readThreads = new (ReadThreadParams, Thread)[threadCount];

            uint fileLength = (uint)(new FileInfo(path).Length);
            uint threadSize = fileLength / (uint)threadCount;
            if (round != 0)
                threadSize -= threadSize % (uint)round;

            Action closeCallback = () => { Interlocked.Add(ref activeThreads, -1); };

            uint currOffset = (uint)offset;

            for (int i = 0; i < threadCount-1; i++) {
                ReadThreadParams p = new ReadThreadParams(path, currOffset, threadSize, batchSize, queue, closeCallback);
                Thread t = new Thread(new ParameterizedThreadStart(ThreadedReader.ReadThread));
                t.Start(p);
                Interlocked.Add(ref activeThreads, 1);

                readThreads[i] = (p, t);
                currOffset += threadSize;
            }

            ReadThreadParams lastP = new ReadThreadParams(path, currOffset, fileLength - currOffset, batchSize, queue, closeCallback);
            Thread lastT = new Thread(new ParameterizedThreadStart(ThreadedReader.ReadThread));
            lastT.Start(lastP);
            Interlocked.Add(ref activeThreads, 1);

            readThreads[threadCount-1] = (lastP, lastT);            

            return true;
        }

        public bool StartSampling(int sampleCount) {
            if (sampling || reading)
                return false;

            sampling = true;

            sampleThreads = new (SampleThreadParams, Thread)[threadCount];

            uint fileLength = (uint)(new FileInfo(path).Length);
            uint threadSize = fileLength / (uint)threadCount;
            if (round != 0)
                threadSize -= threadSize % (uint)round;

            Action closeCallback = () => { Interlocked.Add(ref activeThreads, -1); Debug.Log($"Closed thread. {activeThreads} remaining"); };

            uint currOffset = (uint)offset;

            for (int i = 0; i < threadCount-1; i++) {
                SampleThreadParams p = new SampleThreadParams(path, currOffset, currOffset + threadSize, (uint)(sampleCount / threadCount), round, queue, closeCallback);
                Thread t = new Thread(new ParameterizedThreadStart(ThreadedReader.SampleThread));
                t.Start(p);
                Interlocked.Add(ref activeThreads, 1);

                sampleThreads[i] = (p, t);
                currOffset += threadSize;
            }

            SampleThreadParams lastP = new SampleThreadParams(path, currOffset, fileLength, (uint)(sampleCount / threadCount + sampleCount % threadCount), round, queue, closeCallback);
            Thread lastT = new Thread(new ParameterizedThreadStart(ThreadedReader.SampleThread));
            lastT.Start(lastP);
            Interlocked.Add(ref activeThreads, 1);
            sampleThreads[threadCount-1] = (lastP, lastT);

            return true;
        }

        public async Task Stop() {
            await Task.Run(() => {
                if (!reading)
                    return;

                foreach ((ReadThreadParams p, Thread t) tup in readThreads)
                    tup.p.reading = false;
                    
                reading = false;

                bool threadsFinished = false;   // Wait for all threads to finish
                while (!threadsFinished) {
                    Thread.Sleep(300);
                    threadsFinished = true;
                    foreach ((ReadThreadParams p, Thread t) tup in readThreads)
                        threadsFinished &= !tup.t.IsAlive;
                }
            });
        }

        static void ReadThread(object obj) {
            ReadThreadParams tp = (ReadThreadParams)obj;

            ConcurrentQueue<byte[]> queue = tp.readQueue;

            FileStream fs = File.Open(tp.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(tp.offset, SeekOrigin.Begin);

            byte[] bytes = new byte[tp.batchSize];

            for (int i = 0; i < tp.count / tp.batchSize; i++) {
                fs.Read(bytes);
                queue.Enqueue(bytes);
            }

            bytes = new byte[tp.count % tp.batchSize];
            fs.Read(bytes);
            queue.Enqueue(bytes);

            fs.Close();
            tp.closeCallback();
        }

        static void SampleThread(object obj) {
            SampleThreadParams tp = (SampleThreadParams)obj;
            Debug.Log($"Sample thread started with count of {tp.count} at offset {tp.start}");

            ConcurrentQueue<byte[]> queue = tp.sampleQueue;

            FileStream fs = File.Open(tp.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(tp.start, SeekOrigin.Begin);

            int interval = (int)(((tp.end - tp.start) / (tp.count * tp.sampleSize)));
            Debug.Log($"Interval is {interval}");

            for (int i = 0; i < tp.count; i++) {
                byte[] bytes = new byte[tp.sampleSize];
                fs.Read(bytes);
                queue.Enqueue(bytes);
                // Debug.Log("Enqueued bytes");
                fs.Seek(tp.sampleSize * (interval-1), SeekOrigin.Current);
            }

            fs.Close();
            tp.closeCallback();
        }
    }

    class ReadThreadParams {

        public string filePath;
        public ConcurrentQueue<byte[]> readQueue;
        public uint offset;
        public uint count;
        public int batchSize;
        public bool reading;
        public Action closeCallback;

        public ReadThreadParams(string filePath, uint offset, uint count, int batchSize, ConcurrentQueue<byte[]> readQueue, Action closeCallback) {
            this.filePath = filePath;
            this.offset = offset;
            this.count = count;
            this.batchSize = batchSize;
            this.readQueue = readQueue;
            this.closeCallback = closeCallback;
            reading = true;
        }
    }

    class SampleThreadParams {
        public string filePath;
        public ConcurrentQueue<byte[]> sampleQueue;

        public uint start;
        public uint end;
        public uint count;
        public int sampleSize;
        public bool reading;
        public Action closeCallback;

        public SampleThreadParams(string filePath, uint start, uint end, uint count, int sampleSize, ConcurrentQueue<byte[]> sampleQueue, Action closeCallback) {
            this.filePath = filePath;
            this.start = start;
            this.end = end;
            this.count = count;
            this.sampleSize = sampleSize;
            this.sampleQueue = sampleQueue;
            this.closeCallback = closeCallback;
            reading = true;
        }
    }
}