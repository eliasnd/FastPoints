using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastPoints {
    public class ThreadedReader {
        string path;
        ConcurrentQueue<byte[]> queue;
        int maxLength = 150;
        int batchSize;
        int reading;
        (ThreadParams p, Thread t)[] threads;
        int threadCount = 100;

        public ThreadedReader(string path, int batchSize) {
            this.path = path;
            this.batchSize = batchSize;
            queue = new ConcurrentQueue<byte[]>();
            threads = new (ThreadParams, Thread)[threadCount];
            uint fileLength = new FileInfo(path).Length;
            uint threadSize = fileLength / threadCount;

            for (int i = 0; i < threadCount-1; i++)
                threads[i] = (new ThreadParams(path, threadSize * i, threadSize, batchSize, queue), new Thread(new ParameterizedThreadStart(ThreadedWriter.WriteThread)));

            threads[threadCount-1] = (new ThreadParams(path, threadSize * (threadCount-2), threadSize + fileLength % threadSize, batchSize, queue), new Thread(new ParameterizedThreadStart(ThreadedWriter.WriteThread)));
        }

        public bool Start() {
            if (reading)
                return false;

            foreach ((ThreadParams p, Thread t) tup in threads)
                tup.t.Start(tup.p);

            reading = true;
            return true;
        }

        public async Task Stop() {
            await Task.Run(() => {
                if (!reading)
                    return;

                foreach ((ThreadParams p, Thread t) tup in threads)
                    tup.p.reading = false;
                    
                reading = false;

                bool threadsFinished = false;   // Wait for all threads to finish
                while (!threadsFinished) {
                    Thread.Sleep(300);
                    threadsFinished = true;
                    foreach ((ThreadParams p, Thread t) tup in threads)
                        threadsFinished &= !tup.t.IsAlive;
                }
            });
        }

        static void ReadThread(object obj) {
            ThreadParams tp = (ThreadParams)obj;

            ConcurrentQueue<byte[]> queue = tp.readQueue;

            FileStream fs = File.Open(tp.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(tp.offset);

            byte[] bytes = new byte[tp.batchSize];

            for (int i = 0; i < tp.count / tp.batchSize; i++) {
                fs.Read(bytes);
                queue.Enqueue(bytes);
            }

            bytes = new byte[tp.count % tp.batchSize];
            fs.Read(tp.count % tp.batchSize);
            queue.Enqueue(bytes);

            fs.Close();
        }
    }

    public class ThreadParams {

        public string filePath;
        public ConcurrentQueue<byte[]> readQueue;
        public int offset;
        public int count;
        public int batchSize;
        public bool reading;

        public ThreadParams(string filePath, int offset, int count, int batchSize, ConcurrentQueue<byte> readQueue) {
            this.filePath = filePath;
            this.offset = offset;
            this.count = count;
            this.batchSize = batchSize;
            this.readQueue = readQueue;
            reading = true;
        }
    }
}