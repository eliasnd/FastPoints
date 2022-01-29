using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastPoints {
    public class QueuedWriter {
        string path;
        ConcurrentQueue<(byte[], uint, Action)> queue;
        int maxLength = 150;
        bool running;


        public QueuedWriter(string path) {
            this.path = path;
            queue = new ConcurrentQueue<(byte[], Action)>();
        }

        public bool Start(uint offset=0) {
            if (running)
                return false;

            running = true;

            Task.Run(() => {
                FileStream fs = File.OpenWrite(path);
                fs.Seek(offset);
                while (running || queue.Count > 0) {
                    (byte[] b, uint l, Action cb) tup;
                    while (!queue.TryDequeue(out tup)) {}
                    if (tup.l == uint.MaxValue) {
                        tup.cb(fs.Position);
                        fs.Write(tup.b);
                    } else {
                        uint oldPos = fs.Position;
                        fs.Seek(tup.l);
                        fs.Write(tup.b);
                        fs.Seek(oldPos);
                    }
                }
            });

            return true;
        }

        public async Task Stop() {
            await Task.Run(() => {
                if (!running)
                    return;

                running = false;
            });
        }

        public async Task<uint> Enqueue(byte[] bytes, uint pos=uint.MaxValue) {
            if (!writing)
                Debug.LogError("QueuedWriter must be running!");

            uint ptr;

            await Task.Run(() => {
                while (queue.Count > maxLength)
                    Thread.Sleep(50);

                ptr = uint.MaxValue;
                queue.Enqueue(bytes, pos, (uint p) => { ptr = p; });

                while (ptr == uint.MaxValue)
                    Thread.Sleep(300);
            });

            return ptr;
        }        
    }
}