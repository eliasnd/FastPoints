using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastPoints {
    public class ThreadedWriter {
        string path;
        ConcurrentQueue<(uint, byte[])> queue;
        int maxLength = 150;
        (ThreadParams p, Thread t)[] threads;
        int threadCount = 100;
        readonly object writingLock = new object();
        bool writing;
        public bool IsWriting { get { return writing; } }


        public ThreadedWriter(string path) {
            this.path = path;
            queue = new ConcurrentQueue<(uint, byte[])>();
            threads = new (ThreadParams, Thread)[threadCount];
            for (int i = 0; i < threadCount; i++)
                threads[i] = (new ThreadParams(path, queue), new Thread(new ParameterizedThreadStart(ThreadedWriter.WriteThread)));
        }

        public bool Start() {
            if (writing)
                return false;

            foreach ((ThreadParams p, Thread t) tup in threads)
                tup.t.Start(tup.p);

            writing = true;
            return true;
        }

        public async Task Stop() {
            await Task.Run(() => {
                if (!writing)
                    return;

                foreach ((ThreadParams p, Thread t) tup in threads)
                    tup.p.writing = false;
                    
                writing = false;

                bool threadsFinished = false;   // Wait for all threads to finish
                while (!threadsFinished) {
                    Thread.Sleep(300);
                    threadsFinished = true;
                    foreach ((ThreadParams p, Thread t) tup in threads)
                        threadsFinished &= !tup.t.IsAlive;
                }
            });
        }

        public bool Write(uint idx, byte[] bytes) {
            if (queue.Count >= maxLength)
                return false;

            queue.Enqueue((idx, bytes));
            return true;
        }

        static void WriteThread(object obj) {
            ThreadParams tp = (ThreadParams)obj;

            FileStream fs = File.Open(tp.filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            ConcurrentQueue<(uint, byte[])> queue = tp.writeQueue;

            while (tp.writing || queue.Count > 0) {
                (uint idx, byte[] bytes) tuple;
                if (!queue.TryDequeue(out tuple)) {  // If queue empty, sleep
                    Thread.Sleep(300);
                    continue;
                }


                fs.Seek(tuple.idx, SeekOrigin.Begin);
                fs.Write(tuple.bytes);
            }

            fs.Close();
        }
    }

    public class ThreadParams {

        public string filePath;
        public ConcurrentQueue<(uint, byte[])> writeQueue;
        public bool writing;

        public ThreadParams(string filePath, ConcurrentQueue<(uint, byte[])> writeQueue) {
            this.filePath = filePath;
            this.writeQueue = writeQueue;
            writing = true;
        }
    }
}