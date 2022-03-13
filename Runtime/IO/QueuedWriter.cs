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
        long currOffset;
        int numEnqueued = 0;

        public long QueueSize
        {
            get
            {
                // lock (p.queueSizeLock)
                // {
                    return Interlocked.Read(ref p.queueSize);
                // }
            }
        }

        public QueuedWriter(string path) {
            stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            Start();
        }

        public QueuedWriter(Stream stream) {
            this.stream = stream;
        }

        public void Start(uint offset=0)
        {
            p = new WriterThreadParams();
            p.offset = (long)offset;
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

            int numDequeued = 0;

            while (!p.stopSignal || p.queue.Count > 0) {
                (byte[] b, uint l) tup;
                while (!p.queue.TryDequeue(out tup)) { }

                Interlocked.Add(ref p.queueSize, -(long)tup.b.Length);
                numDequeued++;

                // Debug.Log($"Dequeued {numDequeued}, writing at position {p.stream.Position}");

                if (tup.l == uint.MaxValue) {
                    uint oldPos = (uint)p.stream.Position;
                    p.stream.Write(tup.b);
                    p.stream.Seek(oldPos, SeekOrigin.Begin);    // Read back bytes and check
                    byte[] test = new byte[tup.b.Length];
                    p.stream.Read(test);
                    if (test.Length % 15 != 0)
                        Debug.Log("Wrong length!");
                    for (int i = 0; i < test.Length; i++)
                        if (test[i] != tup.b[i])
                            Debug.Log("Problem!");
                }
                else
                {
                    uint oldPos = (uint)p.stream.Position;
                    p.stream.Seek(tup.l, SeekOrigin.Begin);
                    p.stream.Write(tup.b);
                    p.stream.Seek(oldPos, SeekOrigin.Begin);
                }

                Thread.Sleep(100);
            }

            p.stream.Close();
        }

        public void Stop() {
            if (!writeThread.IsAlive)
                Debug.LogError("QueuedWriter must be running to stop!");

            p.stopSignal = true;
        }

        public long Enqueue(byte[] bytes, uint pos = uint.MaxValue)
        {
            if (!writeThread.IsAlive)
                Debug.LogError("QueuedWriter must be running to enqueue!");
            
            Interlocked.Add(ref p.queueSize, (long)bytes.Length);

            lock (enqueueLock) {
                p.queue.Enqueue((bytes, pos));

                if (pos == uint.MaxValue) {
                    long ptr =currOffset;
                    currOffset += (long)bytes.Length;
                    numEnqueued++;
                    // Debug.Log($"Enqueued {numEnqueued}, got position {ptr}");
                    return ptr;
                } else
                    return (long)pos;
            }
        }
    }

    class WriterThreadParams
    {
        public ConcurrentQueue<(byte[], uint)> queue;
        public object queueSizeLock;
        public long queueSize;
        public Stream stream;
        public long offset;
        public bool stopSignal;
    }
}