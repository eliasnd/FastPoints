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

            readThreads = new (ReadThreadParams, Thread)[threadCount];

            uint fileLength = (uint)(new FileInfo(path).Length);
            uint threadSize = fileLength / (uint)threadCount;
            if (round != 0)
                threadSize -= threadSize % (uint)round;

            Action closeCallback = () => { Interlocked.Add(ref activeThreads, -1); };

            uint currOffset = (uint)offset;

            for (int i = 0; i < threadCount-1; i++) {
                readThreads[i] = (
                    new ReadThreadParams(path, currOffset, threadSize, batchSize, queue, closeCallback),
                    new Thread(new ParameterizedThreadStart(ThreadedReader.ReadThread))
                );
                currOffset += threadSize;
            }

            readThreads[threadCount-1] = (
                new ReadThreadParams(path, currOffset, fileLength - currOffset, batchSize, queue, closeCallback),
                new Thread(new ParameterizedThreadStart(ThreadedReader.ReadThread))
            );
        }

        public bool Start() {
            if (activeThreads > 0)
                return false;

            for (int i = 0; i < threadCount; i++) {
                readThreads[i].Item2.Start(readThreads[i].Item1);
                Interlocked.Add(ref activeThreads, 1);
            }
        }

        static void ReadThread(object obj) {
            ReadThreadParams tp = (ReadThreadParams)obj;

            ConcurrentQueue<byte[]> queue = tp.readQueue;

            Debug.Log($"Read thread at offset {tp.offset}");

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