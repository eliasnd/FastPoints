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
        static int maxLength = 150;
        public bool IsRunning { get { return activeThreads > 0; } }

        (ReadThreadParams p, Thread t)[] readThreads;
        int activeThreads;

        Stopwatch watch;

        // Multi-threaded Reader
        public ThreadedReader(string path, int batchSize, ConcurrentQueue<byte[]> queue, int offset=0, int round=0, int threadCount=10) {
            this.path = path;
            this.queue = queue;

            watch = new Stopwatch();

            readThreads = new (ReadThreadParams, Thread)[threadCount];

            string outputStr = $"Created ThreadedReader with {threadCount} threads\n";

            uint fileLength = (uint)new FileInfo(path).Length;
            uint threadSize = fileLength / (uint)threadCount;
            if (round != 0)
                threadSize -= threadSize % (uint)round;

            Action closeCallback = () => { int remainingThreads = Interlocked.Add(ref activeThreads, -1); if (remainingThreads == 0) { watch.Stop(); Debug.Log($"Total reading time: {watch.ElapsedMilliseconds}"); } };

            uint currOffset = (uint)offset;

            for (int i = 0; i < threadCount-1; i++) {
                readThreads[i] = (
                    new ReadThreadParams(path, currOffset, threadSize, batchSize, queue, closeCallback),
                    new Thread(new ParameterizedThreadStart(ReadThread))
                );

                outputStr += $"\tCreated thread {i} at offset {currOffset} with size {threadSize}\n";
                currOffset += threadSize;
            }

            readThreads[threadCount-1] = (
                new ReadThreadParams(path, currOffset, fileLength - currOffset, batchSize, queue, closeCallback),
                new Thread(new ParameterizedThreadStart(ReadThread))
            );

            outputStr += $"\tCreated thread {threadCount-1} at offset {currOffset} with size {fileLength-currOffset}\n";
        }

        public bool Start() {
            if (activeThreads > 0)
                return false;

            watch.Start();

            for (int i = 0; i < readThreads.Length; i++) {
                readThreads[i].Item2.Start(readThreads[i].Item1);
                Interlocked.Add(ref activeThreads, 1);
            }

            return true;
        }

        static void ReadThread(object obj) {
            ReadThreadParams tp = (ReadThreadParams)obj;

            ConcurrentQueue<byte[]> queue = tp.readQueue;

            // Debug.Log($"Read thread at offset {tp.offset}");

            FileStream fs = File.Open(tp.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(tp.offset, SeekOrigin.Begin);

            //uint startPos = (uint)fs.Position;
            //uint endPos;

            for (int i = 0; i < tp.count / tp.batchSize; i++) {
                while (queue.Count > maxLength)
                    Thread.Sleep(5);

                byte[] bytes = new byte[tp.batchSize];
                fs.Read(bytes);
                //endPos = (uint)fs.Position;
                //Debug.Log($"Read batch from {startPos} to {endPos}");
                
                queue.Enqueue(bytes);

                //Debug.Log("Enqueued bytes");

                //Point[] points = Point.ToPoints(bytes);
                //int c = 0;
                //int c2 = 0;
                //foreach (Point p in points)
                //{
                //    if (p.pos.x > 11)
                //        c++;
                //    if (p.pos.x < -11)
                //        c2++;
                //}
                //if (c > 0 || c2 > 0)
                //    Debug.Log($"Range {startPos}, {endPos} contains {c} big points, {c2} small points");
                //startPos = endPos;
            }

            byte[] lastBytes = new byte[tp.count % tp.batchSize];
            fs.Read(lastBytes);
            //endPos = (uint)fs.Position;
            //Debug.Log($"Read batch from {startPos} to {endPos}");
            queue.Enqueue(lastBytes);

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
        public Action closeCallback;

        public ReadThreadParams(string filePath, uint offset, uint count, int batchSize, ConcurrentQueue<byte[]> readQueue, Action closeCallback) {
            this.filePath = filePath;
            this.offset = offset;
            this.count = count;
            this.batchSize = batchSize;
            this.readQueue = readQueue;
            this.closeCallback = closeCallback;
        }
    }
}