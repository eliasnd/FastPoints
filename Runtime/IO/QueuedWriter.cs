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

                int bufferSize = 4096; 
                int bufferIdx = 0;
                byte[] buffer = new byte[bufferSize];

                while (running || queue.Count > 0) {
                    (byte[] b, uint l, Action<uint> cb) tup;
                    while (!queue.TryDequeue(out tup))
                        Thread.Sleep(50);

                    if (tup.l == uint.MaxValue) {   // If no offset specified, use buffer
                        /* if (tup.b.Length > bufferSize) { // Should be rare, but possible
                            byte[] tempBuffer = new byte[bufferIdx];
                            for (int i = 0; i < bufferIdx; i++)
                                tempBuffer[i] = buffer[i];
                            fs.Write(tempBuffer);
                            bufferIdx = 0;

                            tup.cb((uint)fs.Position);
                            fs.Write(tup.b);
                        } else if (bufferIdx + tup.b.Length >= bufferSize) {
                            // Flush buffer
                            fs.Write(buffer);
                            tup.cb((uint)fs.Position);

                            // Populate buffer
                            for (int i = 0; i < tup.b.Length; i++)
                                buffer[i] = tup.b[i];
                            bufferIdx = tup.b.Length;
                        } else {
                            tup.cb((uint)(fs.Position + bufferIdx));
                            for (int i = 0; i < tup.b.Length; i++)
                                buffer[bufferIdx+i] = tup.b[i];
                            bufferIdx += tup.b.Length;
                        } */

                        tup.cb((uint)fs.Position);
                        fs.Write(tup.b);
                    } else {
                        uint oldPos = (uint)fs.Position;
                        fs.Seek(tup.l, SeekOrigin.Begin);
                        fs.Write(tup.b);
                        fs.Seek(oldPos, SeekOrigin.Begin);
                    }
                    // Debug.Log($"Wrote {tup.b.Length} bytes");
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
                    Thread.Sleep(50);
            });

            return ptr;
        }        
    }
}