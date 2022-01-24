using UnityEngine;
using Unity.Collections;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;

namespace FastPoints {
    public class PlyStream : BaseStream {

        Vector3 currMinPoint;
        Vector3 currMaxPoint;
        bool computeBounds=false;

        enum Format {
            INVALID,
            BINARY_LITTLE_ENDIAN,
            BINARY_BIG_ENDIAN,
            ASCII
        }

        enum Property {
            INVALID,
            R8, G8, B8, A8,
            R16, G16, B16, A16,
            SINGLE_X, SINGLE_Y, SINGLE_Z,
            DOUBLE_X, DOUBLE_Y, DOUBLE_Z,
            DATA_8, DATA_16, DATA_32, DATA_64
        }

        Property[] size8 = { Property.R8, Property.G8, Property.B8, Property.A8, Property.DATA_8 };
        Property[] size16 = { Property.R16, Property.G16, Property.B16, Property.A16, Property.DATA_16 };
        Property[] size32 = { Property.SINGLE_X, Property.SINGLE_Y, Property.SINGLE_Z, Property.DATA_32 };
        Property[] size64 = { Property.DOUBLE_X, Property.DOUBLE_Y, Property.DOUBLE_Z, Property.DATA_64 };

        Format format = Format.INVALID;
        StreamReader sReader;
        BinaryReader bReader;
        List<Property> properties = null;
        int pointSize;  // Number of bytes per point
        int bodyOffset; // Number of bytes before body begins

        public PlyStream(string filePath) : base(filePath) {
            sReader = new StreamReader(stream);
            bReader = new BinaryReader(stream);

            minPoint = new Vector3(1, 1, 1); // non-nullable, so just make bigger than maxpoint
            maxPoint = new Vector3(0, 0, 0);

            currMinPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            currMaxPoint = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            ReadHeader();
        }

        public override bool ReadPoints(int pointCount, Point[] target) {
            bool result = index + pointCount <= count;

            if (!result)
                throw new Exception($"Trying to read {pointCount} points from stream with {count-index} points remaining");
                // pointCount = count - index;

            index += pointCount;
            byte[] bytes;

            switch (format) {
                case Format.BINARY_LITTLE_ENDIAN:
                    bytes = bReader.ReadBytes(pointSize * pointCount);
                    if (computeBounds)
                        for (int i = 0; i < pointCount; i++) {
                            target[i] = ReadPointBLE(bytes, i*pointSize);
                            currMinPoint = Vector3.Min(currMinPoint, target[i].pos);
                            currMaxPoint = Vector3.Max(currMaxPoint, target[i].pos);
                        }
                    else
                        for (int i = 0; i < pointCount; i++)
                            target[i] = ReadPointBLE(bytes, i*pointSize);
                    
                    if (computeBounds && index == count) {
                        minPoint = currMinPoint;
                        maxPoint = currMaxPoint;
                    }

                    return true;
                case Format.BINARY_BIG_ENDIAN:
                    bytes = bReader.ReadBytes(pointSize * pointCount);
                    if (computeBounds)
                        for (int i = 0; i < pointCount; i++) {
                            target[i] = ReadPointBBE(bytes, i*pointSize);
                            currMinPoint = Vector3.Min(currMinPoint, target[i].pos);
                            currMaxPoint = Vector3.Max(currMaxPoint, target[i].pos);
                        }
                    else
                        for (int i = 0; i < pointCount; i++)
                            target[i] = ReadPointBBE(bytes, i*pointSize);

                    if (computeBounds && index == count) {
                        minPoint = currMinPoint;
                        maxPoint = currMaxPoint;
                    }

                    return true;
                case Format.ASCII:
                    if (computeBounds)
                        for (int i = 0; i < pointCount; i++) {
                            target[i] = ReadPointASCII(sReader.ReadLine());
                            currMinPoint = Vector3.Min(currMinPoint, target[i].pos);
                            currMaxPoint = Vector3.Max(currMaxPoint, target[i].pos);
                        }
                    else
                        for (int i = 0; i < pointCount; i++)
                            target[i] = ReadPointASCII(sReader.ReadLine());

                    if (computeBounds && index == count) {
                        minPoint = currMinPoint;
                        maxPoint = currMaxPoint;
                    }

                    return true;
                default:
                    return false;
            }
        }

        /* public override async Task SamplePoints(int pointCount, Point[] target) {
            await Task.Run(() => {
                Debug.Log($"Calling sample points. Format {format}, body offset {bodyOffset}");
                if (format != Format.BINARY_LITTLE_ENDIAN)
                    throw new NotImplementedException();

                int interval = count / pointCount;

                if (interval < 1)
                    throw new ArgumentException("pointCount cannot exceed cloud size");

                FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(bodyOffset, SeekOrigin.Begin);
                byte[] firstPointBytes = new byte[pointSize];
                fs.Read(firstPointBytes);

                // Debug.Log($"Creating threaded reader with offset {bodyOffset}");

                ConcurrentQueue<byte[]> bytes = new ConcurrentQueue<byte[]>();
                ThreadedReader tr = new ThreadedReader(path, 1, bytes, bodyOffset, pointSize);
                tr.StartSampling(pointCount);

                // Debug.Log("Threaded reader sampling started");

                int i = 0;

                while (tr.IsRunning || bytes.Count > 0) {
                    byte[] pointBytes;
                    while (!bytes.TryDequeue(out pointBytes)) 
                        if (!tr.IsRunning)
                            break;

                    if (!tr.IsRunning)
                        break;
                        
                    target[i] = ReadPointBLE(pointBytes);
                    // Debug.Log($"Writing point {i}: {target[i].ToString()}");
                    i++;
                }

                Debug.Log($"First point: {target[0].ToString()}");
            });

            Debug.Log("Done sampling");
        } */

        public override async Task SamplePoints(int pointCount, Point[] target) {
            await Task.Run(() => {
                Debug.Log("Sampling started");

                if (format == Format.INVALID)
                    ReadHeader();

                int interval = count / pointCount;
                int lineLength;

                if (interval < 1)
                    throw new ArgumentException("pointCount cannot exceed cloud size");

                Debug.Log($"Interval is {interval}");

                lineLength = CalculatePointBytes(); 
                bool allPointsRead = false;
                
                bool result = false;

                switch (format) {
                    case Format.BINARY_LITTLE_ENDIAN:
                        for (int i = 0; i < pointCount; i++) {
                            target[i] = ReadPointBLE(bReader.ReadBytes(lineLength));
                            stream.Seek(lineLength * (interval - 1), SeekOrigin.Current);
                            // Debug.Log($"Sampled point {i}");
                        }
                        allPointsRead = true;
                        UnityEngine.Debug.Log("Done sampling points");
                        break;
                    case Format.BINARY_BIG_ENDIAN:
                        for (int i = 0; i < pointCount; i++) {
                            target[i] = ReadPointBBE(bReader.ReadBytes(lineLength));
                            stream.Seek(lineLength * (interval - 1), SeekOrigin.Current);
                        }
                        allPointsRead = true;
                        UnityEngine.Debug.Log("Done sampling points");
                        break;
                    case Format.ASCII:
                        for (int i = 0; i < pointCount; i++) {
                            target[i] = ReadPointASCII(sReader.ReadLine());
                            for (int j = 0; j < interval-1; j++)
                                sReader.ReadLine();
                            
                        }
                        allPointsRead = true;
                        break;
                }
            });
        }

        /* public override async Task SamplePoints(int pointCount, Point[] target) {
            if (format == Format.INVALID)
                ReadHeader();

            int interval = count / pointCount;
            int lineLength;

            if (interval < 1)
                throw new ArgumentException("pointCount cannot exceed cloud size");

            ConcurrentQueue<byte[]> readBinaryPoints = new ConcurrentQueue<byte[]>();
            ConcurrentQueue<string> readASCIIPoints = new ConcurrentQueue<string>();
            lineLength = CalculatePointBytes(); 
            bool allPointsRead = false;
            
            bool result = false;

            Task t1 = Task.Run(() => {
                switch (format) {
                    case Format.BINARY_LITTLE_ENDIAN:
                    case Format.BINARY_BIG_ENDIAN:
                        for (int i = 0; i < pointCount; i++) {
                            readBinaryPoints.Enqueue(bReader.ReadBytes(lineLength));
                            stream.Seek(lineLength * (interval - 1), SeekOrigin.Current);
                        }
                        allPointsRead = true;
                        UnityEngine.Debug.Log("Done queueing points");
                        break;
                    case Format.ASCII:
                        for (int i = 0; i < pointCount; i++) {
                            readASCIIPoints.Enqueue(sReader.ReadLine());
                            for (int j = 0; j < interval-1; j++)
                                sReader.ReadLine();
                        }
                        allPointsRead = true;
                        break;
                }
            });

            Task t2 = Task.Run(() => {
                switch (format) {
                    case Format.BINARY_LITTLE_ENDIAN:
                        for (int i = 0; i < pointCount; i++) {
                            byte[] bytes;
                            while (!readBinaryPoints.TryDequeue(out bytes)) {} // Loop until successful dequeue
                            target[i] = ReadPointBLE(bytes);
                        }
                        UnityEngine.Debug.Log("Done dequeueing points");
                        result = true;
                        break;
                    case Format.BINARY_BIG_ENDIAN:
                        for (int i = 0; i < pointCount; i++) {
                            byte[] bytes;
                            while (!readBinaryPoints.TryDequeue(out bytes)) {} // Loop until successful dequeue
                            target[i] = ReadPointBBE(bytes);
                        }
                        result = true;
                        break;
                    case Format.ASCII:
                        for (int i = 0; i < pointCount; i++) {
                            string str;
                            while (!readASCIIPoints.TryDequeue(out str)) {} // Loop until successful dequeue
                            target[i] = ReadPointASCII(str);
                        }
                        result = true;
                        break;
                }
            });

            await t1;
            await t2;
        } */

        void ReadHeader() {
            bodyOffset = 0;
            properties = new List<Property>();

            string line = sReader.ReadLine();
            bodyOffset += line.Length + 1;

            if (line != "ply")
                throw new System.Exception("Error: Missing header keyword 'ply'");

            line = sReader.ReadLine();
            bodyOffset += line.Length + 1;

            switch (line) {
                case "format binary_little_endian 1.0":
                    format = Format.BINARY_LITTLE_ENDIAN;
                    break;
                case "format binary_big_endian 1.0":
                    format = Format.BINARY_BIG_ENDIAN;
                    break;
                case "format ascii 1.0":
                    format = Format.ASCII;
                    break;
                default:
                    throw new System.Exception("Error: PLY file format not recognized");
            }

            while (line != "end_header") {
                line = sReader.ReadLine();
                bodyOffset += line.Length + 1;

                string[] words = line.Split();

                if (words[0] == "element") {
                    if (words[1] == "vertex")
                        count = Int32.Parse(words[2]);
                    continue;
                }

                if (words[0] == "property") {
                    Property prop = Property.INVALID;

                    switch (words[2]) {
                        case "x": prop = Property.SINGLE_X; break;
                        case "y": prop = Property.SINGLE_Y; break;
                        case "z": prop = Property.SINGLE_Z; break;
                        case "red": prop = Property.R8; break;
                        case "green": prop = Property.G8; break;
                        case "blue": prop = Property.B8; break;
                        case "alpha": prop = Property.A8; break;
                    }

                    switch (words[1]) {
                        case "char": case "uchar": case "int8": case "uint8":
                            if (prop == Property.INVALID)
                                prop = Property.DATA_8;
                            else if (!Array.Exists(size8, e => e == prop))
                                throw new System.Exception("Invalid property: " + line);
                            break;
                        case "short": case "ushort": case "int16": case "uint16":
                            switch (prop) {
                                case Property.R8: prop = Property.R16; break;
                                case Property.G8: prop = Property.G16; break;
                                case Property.B8: prop = Property.B16; break;
                                case Property.A8: prop = Property.A16; break;
                                case Property.INVALID: prop = Property.DATA_16; break;
                            } 
                            if (!Array.Exists(size16, e => e == prop))
                                throw new System.Exception("Invalid property: " + line);
                            break;
                        case "int": case "uint": case "float": case "int32": case "uint32": case "float32":
                            if (prop == Property.INVALID)
                                prop = Property.DATA_32;
                            else if (!Array.Exists(size32, e => e == prop))
                                throw new System.Exception("Invalid property: " + line);
                            break;
                        case "int64": case "uint64": case "double": case "float64":
                            switch (prop) {
                                case Property.SINGLE_X: prop = Property.DOUBLE_X; break;
                                case Property.SINGLE_Y: prop = Property.DOUBLE_Y; break;
                                case Property.SINGLE_Z: prop = Property.DOUBLE_Z; break;
                                case Property.INVALID: prop = Property.DATA_64; break;
                            }
                            if (!Array.Exists(size64, e => e == prop))
                                throw new System.Exception("Invalid property: " + line);
                            break;
                    }

                    properties.Add(prop);
                }
            }

            pointSize = CalculatePointBytes();

            stream.Position = bodyOffset;
            // Debug.Log($"Body offset is {bodyOffset}");
        } 

        Point ReadPointASCII(string str) {
            float x = 0, y = 0, z = 0;
            byte r = 255, g = 255, b = 255, a = 255;

            string[] line = str.Split(' ');

            for (int j = 0; j < properties.Count; j++) {
                double val = double.Parse(line[j]);

                switch (properties[j]) {
                    case Property.R8: r = Convert.ToByte(val); break;
                    case Property.G8: g = Convert.ToByte(val); break;
                    case Property.B8: b = Convert.ToByte(val); break;
                    case Property.A8: a = Convert.ToByte(val); break;

                    case Property.R16: r = (byte)(Convert.ToUInt16(val) >> 8); break;
                    case Property.G16: g = (byte)(Convert.ToUInt16(val) >> 8); break;
                    case Property.B16: b = (byte)(Convert.ToUInt16(val) >> 8); break;
                    case Property.A16: a = (byte)(Convert.ToUInt16(val) >> 8); break;

                    case Property.SINGLE_X: x = (float)val; break;
                    case Property.SINGLE_Y: y = (float)val; break;
                    case Property.SINGLE_Z: z = (float)val; break;

                    case Property.DOUBLE_X: x = (float)val; break;
                    case Property.DOUBLE_Y: y = (float)val; break;
                    case Property.DOUBLE_Z: z = (float)val; break;

                    // case Property.DATA_8: case Property.DATA_16: case Property.DATA_32: case Property.DATA_64: break;
                }
            }

            // return new Point(x, y, z, ((r << 24) | (g << 16) | (b << 8) | a));
            return new Point(new Vector3(x, y, z), new Color(r / 255f, g / 255f, b / 255f, a / 255f));
        }

        Point ReadPointBLE(byte[] bytes, int startIdx=0) {
            float x = 0, y = 0, z = 0;
            byte r = 255, g = 255, b = 255, a = 255;

            int i = 0;

            foreach (Property prop in properties) {
                switch (prop) {
                    case Property.R8: r = bytes[startIdx+i]; i++; break;
                    case Property.G8: g = bytes[startIdx+i]; i++; break;
                    case Property.B8: b = bytes[startIdx+i]; i++; break;
                    case Property.A8: a = bytes[startIdx+i]; i++; break;

                    case Property.R16: r = (byte)(BitConverter.ToUInt16(bytes, startIdx+i) >> 8); i += 2; break;
                    case Property.G16: g = (byte)(BitConverter.ToUInt16(bytes, startIdx+i) >> 8); i += 2; break;
                    case Property.B16: b = (byte)(BitConverter.ToUInt16(bytes, startIdx+i) >> 8); i += 2; break;
                    case Property.A16: a = (byte)(BitConverter.ToUInt16(bytes, startIdx+i) >> 8); i += 2; break;

                    case Property.SINGLE_X: x = BitConverter.ToSingle(bytes, startIdx+i); i += 4; break;
                    case Property.SINGLE_Y: y = BitConverter.ToSingle(bytes, startIdx+i); i += 4; break;
                    case Property.SINGLE_Z: z = BitConverter.ToSingle(bytes, startIdx+i); i += 4; break;

                    case Property.DOUBLE_X: x = (float)BitConverter.ToDouble(bytes, startIdx+i); i += 8; break;
                    case Property.DOUBLE_Y: y = (float)BitConverter.ToDouble(bytes, startIdx+i); i += 8; break;
                    case Property.DOUBLE_Z: z = (float)BitConverter.ToDouble(bytes, startIdx+i); i += 8; break;

                    case Property.DATA_8: i++; break;
                    case Property.DATA_16: i += 2; break;
                    case Property.DATA_32: i += 4; break;
                    case Property.DATA_64: i += 8; break;
                }
            }

            return new Point(new Vector3(x, y, z), new Color(r / 255f, g / 255f, b / 255f, a / 255f));
        }

        Point[] ReadPointsBLE(byte[] bytes) {
            Point[] points = new Point[bytes.Length / pointSize];

            for (int s = 0; s < points.Length; s++) {
                float x = 0, y = 0, z = 0;
                byte r = 255, g = 255, b = 255, a = 255;

                int i = 0;

                foreach (Property prop in properties) {
                    switch (prop) {
                        case Property.R8: r = bytes[pointSize*s+i]; i++; break;
                        case Property.G8: g = bytes[pointSize*s+i]; i++; break;
                        case Property.B8: b = bytes[pointSize*s+i]; i++; break;
                        case Property.A8: a = bytes[pointSize*s+i]; i++; break;

                        case Property.R16: r = (byte)(BitConverter.ToUInt16(bytes, pointSize*s+i) >> 8); i += 2; break;
                        case Property.G16: g = (byte)(BitConverter.ToUInt16(bytes, pointSize*s+i) >> 8); i += 2; break;
                        case Property.B16: b = (byte)(BitConverter.ToUInt16(bytes, pointSize*s+i) >> 8); i += 2; break;
                        case Property.A16: a = (byte)(BitConverter.ToUInt16(bytes, pointSize*s+i) >> 8); i += 2; break;

                        case Property.SINGLE_X: x = BitConverter.ToSingle(bytes, pointSize*s+i); i  += 4; break;
                        case Property.SINGLE_Y: y = BitConverter.ToSingle(bytes, pointSize*s+i); i  += 4; break;
                        case Property.SINGLE_Z: z = BitConverter.ToSingle(bytes, pointSize*s+i); i  += 4; break;

                        case Property.DOUBLE_X: x = (float)BitConverter.ToDouble(bytes, pointSize*s+i); i += 8; break;
                        case Property.DOUBLE_Y: y = (float)BitConverter.ToDouble(bytes, pointSize*s+i); i += 8; break;
                        case Property.DOUBLE_Z: z = (float)BitConverter.ToDouble(bytes, pointSize*s+i); i += 8; break;

                        case Property.DATA_8: i++; break;
                        case Property.DATA_16: i += 2; break;
                        case Property.DATA_32: i += 4; break;
                        case Property.DATA_64: i += 8; break;
                    }
                }

                points[s] = new Point(new Vector3(x, y, z), new Color(r / 255f, g / 255f, b / 255f, a / 255f));
            }
            

            return points;
        }
        
        Point ReadPointBBE(byte[] bytes, int startIdx=0) {                               
            throw new NotImplementedException();
        }

        int CalculatePointBytes() {
            int count = 0;
            foreach (Property prop in properties) {
                switch (prop) {
                    case Property.R8: case Property.G8: case Property.B8: case Property.A8: case Property.DATA_8: count += 1; break;
                    case Property.R16: case Property.G16: case Property.B16: case Property.A16: case Property.DATA_16: count += 2; break;
                    case Property.SINGLE_X: case Property.SINGLE_Y: case Property.SINGLE_Z: case Property.DATA_32: count += 4; break;
                    case Property.DOUBLE_X: case Property.DOUBLE_Y: case Property.DOUBLE_Z: case Property.DATA_64: count += 8; break;            
                }
            }

            return count;
        }

        public void SetComputeBounds(bool val) {
            computeBounds = val;
        }

        public override async Task ReadPointsToQueue(ConcurrentQueue<Point[]> queue, int maxQueued, int batchSize) {
            await Task.Run(() => {
                ConcurrentQueue<byte[]> bQueue = new ConcurrentQueue<byte[]>();

                ThreadedReader tr = new ThreadedReader(path, batchSize * pointSize, bQueue, bodyOffset, pointSize, 5);
                tr.Start();

                int totalEnqueued = 0;

                while (totalEnqueued < count) {
                    byte[] bytes;
                    while (!bQueue.TryDequeue(out bytes))
                        Thread.Sleep(5);

                    if (queue.Count >= maxQueued) {}

                    Point[] batch = ReadPointsBLE(bytes);
                    //Point[] batch = Point.ToPoints(bytes);
                    //int c1 = 0, c2 = 0;
                    queue.Enqueue(batch);
                    //Debug.Log("Dequeued bytes");
                    //foreach (Point p in batch)
                    //{
                    //    if (p.pos.x < -11)
                    //        c1++;
                    //    if (p.pos.x > 11)
                    //        c2++;
                    //}
                    //if (c1 > 0 || c2 > 0)
                    //    Debug.Log($"Batch contains {c1} small points, {c2} big points");
                    totalEnqueued += bytes.Length / pointSize;
                }
            });  
        }
    }
}