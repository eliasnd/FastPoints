using UnityEngine;

using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FastPoints {
    public class QueuedWriter {
        string path;
        ConcurrentQueue<(byte[], uint, Action<uint>)> queue;
        int maxLength = 150;
        bool running;


        public QueuedWriter(string path) {
            this.path = path;
            queue = new ConcurrentQueue<(byte[], uint, Action<uint>)>();
        }

        public bool Start(uint offset=0) {
            if (running)
                return false;

            running = true;

            Task.Run(() => {
                FileStream fs = File.OpenWrite(path);
                fs.Seek(offset, SeekOrigin.Begin);
                while (running || queue.Count > 0) {
                    (byte[] b, uint l, Action<uint> cb) tup;
                    while (!queue.TryDequeue(out tup)) {}
                    if (tup.l == uint.MaxValue) {
                        tup.cb((uint)fs.Position);
                        fs.Write(tup.b);
                    } else {
                        uint oldPos = (uint)fs.Position;
                        fs.Seek(tup.l, SeekOrigin.Begin);
                        fs.Write(tup.b);
                        fs.Seek(oldPos, SeekOrigin.Begin);
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
            if (!running)
                Debug.LogError("QueuedWriter must be running!");

            uint ptr = 0;

            await Task.Run(() => {
                while (queue.Count > maxLength)
                    Thread.Sleep(50);

                ptr = uint.MaxValue;
                queue.Enqueue((bytes, pos, (uint p) => { ptr = p; }));

                while (ptr == uint.MaxValue)
                    Thread.Sleep(300);
            });

            return ptr;
        }        
    }
}