using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FastPoints {
    public class ThreadedWriter {
        ConcurrentDictionary<string, ConcurrentBag<byte[]>> buffers;
        Dictionary<string, int> locks;
        (Thread, ThreadParams)[] threads;
        readonly object writingLock = new object();
        bool stopSignal = false;


        public ThreadedWriter(int threadCount=1) {
            buffers = new ConcurrentDictionary<string, ConcurrentBag<byte[]>>();
            locks = new Dictionary<string, int>();

            threads = new (Thread, ThreadParams)[threadCount];
            for (int i = 0; i < threadCount; i++) {
                Thread t = new Thread(new ParameterizedThreadStart(WriteThread));
                ThreadParams p = new ThreadParams(buffers, locks, false, writingLock);
                t.Start(p);
                threads[i] = (t, p);
            }
        }

        ~ThreadedWriter() {
            stopSignal = true;

            for (int i = 0; i < threads.Length; i++)
                threads[i].Item2.stopSignal = true;

            for (int i = 0; i < threads.Length; i++)    // Maybe not needed?
                threads[i].Item1.Join();
        }

        public void Write(string path, byte[] bytes) {
            if (stopSignal)
                throw new Exception("ThreadedWriter not running!");

            buffers.GetOrAdd(path, new ConcurrentBag<byte[]>()).Add(bytes);
        }

        static void WriteThread(object obj) {
            ThreadParams tp = (ThreadParams)obj;

            while (true) {
                string path = null;

                
                if (tp.stopSignal && tp.buffers.Count == 0)
                    break;

                lock (tp.writingLock) {
                    foreach (string s in tp.buffers.Keys)
                        if (!tp.locks.ContainsKey(s)) {
                            path = s;
                            tp.locks.Add(path, 1);
                            break;
                        }
                }

                if (path == null)  { // No unlocked buffer
                    Thread.Sleep(100);
                    continue;
                }

                FileStream fs = File.OpenWrite(path);
                ConcurrentBag<byte[]> buffer = tp.buffers[path];

                // Flush buffer
                while (buffer.Count > 0) {
                    byte[] bytes;
                    while (!buffer.TryTake(out bytes)) {}
                    fs.Write(bytes);
                }

                // Clean up
                lock (tp.writingLock) {
                    tp.buffers.TryRemove(path, out buffer);
                    int i;
                    tp.locks.Remove(path, out i);
                }

                fs.Close();

                Thread.Sleep(100);
            }
        }
    }

    class ThreadParams {

        public object writingLock;
        public ConcurrentDictionary<string, ConcurrentBag<byte[]>> buffers;
        public Dictionary<string, int> locks;
        public bool stopSignal;

        public ThreadParams(ConcurrentDictionary<string, ConcurrentBag<byte[]>> buffers, Dictionary<string, int> locks, bool stopSignal, object writingLock) {
            this.buffers = buffers;
            this.locks = locks;
            this.stopSignal = stopSignal;
            this.writingLock = writingLock;
        }
    }
}