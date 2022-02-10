using UnityEngine;

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    public class QueuedWriter {
        ConcurrentQueue<(byte[], uint)> queue;
        public Stream stream;
        WriterThreadParams p;
        Thread writeThread;

        object enqueueLock = new object();
        uint currOffset;

        public uint QueueSize
        {
            get
            {
                lock (p.queueSizeLock)
                {
                    return p.queueSize;
                }
            }
        }

        public QueuedWriter(string path) {
            stream = File.OpenWrite(path);
        }

        public QueuedWriter(Stream stream) {
            this.stream = stream;
        }

        public void Start(uint offset=0)
        {
            p = new WriterThreadParams();
            p.offset = offset;
            p.stream = stream;
            p.stopSignal = false;
            p.queue = new ConcurrentQueue<(byte[], uint)>();
            p.queueSizeLock = new object();
            p.queueSize = 0;

            writeThread = new Thread(new ParameterizedThreadStart(RunWriter));
            writeThread.Start(p);
        }

        static void RunWriter(object obj) {
            WriterThreadParams p = (WriterThreadParams)obj;

            p.stream.Seek(p.offset, SeekOrigin.Begin);

            while (!p.stopSignal || p.queue.Count > 0) {
                (byte[] b, uint l) tup;
                while (!p.queue.TryDequeue(out tup)) { }

                lock (p.queueSizeLock)
                {
                    p.queueSize -= (uint)tup.b.Length;
                }

                if (tup.l == uint.MaxValue)
                    p.stream.Write(tup.b);
                else
                {
                    uint oldPos = (uint)p.stream.Position;
                    p.stream.Seek(tup.l, SeekOrigin.Begin);
                    p.stream.Write(tup.b);
                    p.stream.Seek(oldPos, SeekOrigin.Begin);
                }
            }
        }

        public void Stop() {
            if (!writeThread.IsAlive)
                Debug.LogError("QueuedWriter must be running to stop!");

            p.stopSignal = true;
        }

        public uint Enqueue(byte[] bytes, uint pos = uint.MaxValue)
        {
            if (!writeThread.IsAlive)
                Debug.LogError("QueuedWriter must be running to enqueue!");

            while (true)
            {
                lock (p.queueSizeLock)
                {
                    if (p.queueSize < 1024 * 1024 * 100)
                    {
                        p.queueSize += (uint)bytes.Length;
                        break;
                    }
                }
                Thread.Sleep(20);
            }
            

            lock (enqueueLock) {
                queue.Enqueue((bytes, pos));

                if (pos != uint.MaxValue) {
                    uint ptr = currOffset;
                    currOffset += (uint)bytes.Length;
                    return ptr;
                } else
                    return pos;
            }
        }
    }

    struct WriterThreadParams
    {
        public ConcurrentQueue<(byte[], uint)> queue;
        public object queueSizeLock;
        public uint queueSize;
        public Stream stream;
        public uint offset;
        public bool stopSignal;
    }
}