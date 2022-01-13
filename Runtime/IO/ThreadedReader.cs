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
        bool reading;
        bool sampling;

        (ReadThreadParams p, Thread t)[] readThreads;
        (SampleThreadParams p, Thread t)[] sampleThreads;
        int threadCount = 100;

        public ThreadedReader(string path, int batchSize, ConcurrentQueue<byte[]> queue, int offset=0, int round=0) {
            this.path = path;
            this.batchSize = batchSize;

            readThreads = new (ReadThreadParams, Thread)[threadCount];
            sampleThreads = new (SampleThreadParams, Thread)[threadCount];

            uint fileLength = new FileInfo(path).Length;
            uint threadSize = fileLength / threadCount;
            if (round != 0)
                threadSize -= threadSize % round;

            for (int i = 0; i < threadCount-1; i++) {
                readThreads[i] = (
                    new ReadThreadParams(path, threadSize * i, threadSize, batchSize, readQueue), 
                    new Thread(new ParameterizedThreadStart(ThreadedWriter.ReadThread))
                );
                sampleThreads[i] = (
                    new SampleThreadParams(path, threadSize * i, threadSize * (i+1), -1, round, sampleQueue), 
                    new Thread(new ParameterizedThreadStart(ThreadedWriter.SampleThread))
                );
            }

            readThreads[threadCount-1] = (
                new ReadThreadParams(path, threadSize * (threadCount-2), threadSize + fileLength % threadSize, batchSize, readQueue), 
                new Thread(new ParameterizedThreadStart(ThreadedWriter.WriteThread))
            );
            sampleThreads[threadCount-1] = (
                new SampleThreadParams(path, threadSize * (threadCount-2), fileLength-1, -1, round, sampleQueue), 
                new Thread(new ParameterizedThreadStart(ThreadedWriter.WriteThread)));
        }

        public bool StartReading() {
            if (reading)
                return false;

            foreach ((ThreadParams p, Thread t) tup in threads)
                tup.t.Start(tup.p);

            reading = true;
            return true;
        }

        public bool StartSampling(int sampleCount) {
            if (sampling)
                return false;

            for (int i = 0; i < threadCount-1; i++) {
                (ThreadParams p, Thread t) tup = sampleThreads[i];
                tup.p.count = sampleCount / threadCount;
                tup.t.Start(tup.p);
            }

            (ThreadParams p, Thread t) tup = sampleThreads[threadCount-1];
            tup.p.count = sampleCount / threadCount + sampleCount % threadCount;
            tup.t.Start(tup.p);

            sampling = true;
            return true;
        }

        public bool IsRunning() {
            bool threadsFinished = false;   // Wait for all threads to finish
                while (!threadsFinished) {
                    Thread.Sleep(300);
                    threadsFinished = true;
                    foreach ((ThreadParams p, Thread t) tup in threads)
                        threadsFinished &= !tup.t.IsAlive;
                }

            return !threadsFinished;
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
            fs.Read(bytes);
            queue.Enqueue(bytes);

            fs.Close();
        }

        static void SampleThread(object obj) {
            ThreadParams tp = (ThreadParams)obj;

            ConcurrentQueue<byte[]> queue = tp.readQueue;

            FileStream fs = File.Open(tp.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(tp.offset);

            int interval = count / ((end - start) / tp.batchSize);

            for (int i = 0; i < count; i++) {
                byte[] bytes = new byte[tp.batchSize];
                fs.Read(bytes);
                queue.Enqueue(bytes);
                fs.Seek(interval-1, SeekOrigin.Current);
            }

            fs.Close();
        }
    }

    public class ReadThreadParams {

        public string filePath;
        public ConcurrentQueue<byte[]> readQueue;
        public int offset;
        public int count;
        public int batchSize;
        public bool reading;

        public ReadThreadParams(string filePath, int offset, int count, int batchSize, ConcurrentQueue<byte> readQueue) {
            this.filePath = filePath;
            this.offset = offset;
            this.count = count;
            this.batchSize = batchSize;
            this.readQueue = readQueue;
            reading = true;
        }
    }
}

public class SampleThreadParams {
    public string filePath;
    public ConcurrentQueue<byte[]> sampleQueue;

    public int start;
    public int end;
    public int count;
    public int sampleSize;
    public bool reading;

    public SampleThreadParams(string filePath, int start, int end, int count, int sampleSize, ConcurrentQueue<byte[]> sampleQueue) {
        this.filePath = filePath;
        this.start = start;
        this.end = end;
        this.count = count;
        this.sampleSize = sampleSize;
        this.sampleQueue = sampleQueue;
        reading = true;
    }
}