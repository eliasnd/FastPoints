using UnityEngine;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

using System;
using System.Collections.Generic;
using System.IO;

namespace FastPoints {

    [ScriptedImporter(1, "ply")]
    class PlyImporter : BaseImporter {
    
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


        public override bool OpenStream() {
            try {
                stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                sReader = new StreamReader(stream);
                bReader = new BinaryReader(stream);
                return true;
            } catch (System.Exception e) {
                return false;
            }
        }

        public override bool ReadPoints(int pointCount, Vector4[] target) {
            if (format == Format.INVALID)
                ReadHeader();

            bool result = index + pointCount <= count;

            if (!result)
                pointCount = count - index;


            switch (format) {
                case Format.BINARY_LITTLE_ENDIAN:
                    for (int i = 0; i < pointCount; i++)
                        target[i] = ReadPointBLE();
                    return true;
                case Format.BINARY_BIG_ENDIAN:
                    for (int i = 0; i < pointCount; i++)
                        target[i] = ReadPointBBE();
                    return true;
                case Format.ASCII:
                    for (int i = 0; i < pointCount; i++)
                        target[i] = ReadPointASCII();
                    return true;
                default:
                    return false;
            }
        }

        public override bool SamplePoints(int pointCount, Vector4[] target)
        {
            if (format == Format.INVALID)
                ReadHeader();

            int interval = count / pointCount;
            int lineLength;

            if (interval < 1)
                throw new ArgumentException("pointCount cannot exceed cloud size");

            switch (format) {
                case Format.BINARY_LITTLE_ENDIAN:
                    lineLength = CalculateLineBytes(); 
                    for (int i = 0; i < pointCount; i++) {
                        target[index] = ReadPointBLE();
                        bReader.ReadBytes(lineLength);
                    }
                    return true;
                case Format.ASCII:
                    for (int i = 0; i < pointCount; i++) {
                        target[i] = ReadPointASCII();
                        for (int j = 0; j < interval-1; j++) 
                            sReader.ReadLine(); 
                    }
                    return true;
                case Format.BINARY_BIG_ENDIAN:
                    lineLength = CalculateLineBytes(); 
                    for (int i = 0; i < pointCount; i++) {
                        target[index] = ReadPointBBE();
                        bReader.ReadBytes(lineLength);
                    }
                    return true;
            }

            return false;
        }

        void ReadHeader() {
            int bodyOffset = 0;
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

            stream.Position = bodyOffset;
        } 

        Vector4 ReadPointASCII() {
            float x = 0, y = 0, z = 0;
            byte r = 255, g = 255, b = 255, a = 255;

            string[] line = sReader.ReadLine().Split(' ');

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

            return new Vector4(x, y, z, ((r << 24) | (g << 16) | (b << 8) | a));

        }

        Vector4 ReadPointBLE() {
            float x = 0, y = 0, z = 0;
            byte r = 255, g = 255, b = 255, a = 255;

            foreach (Property prop in properties) {
                switch (prop) {
                    case Property.R8: r = bReader.ReadByte(); break;
                    case Property.G8: g = bReader.ReadByte(); break;
                    case Property.B8: b = bReader.ReadByte(); break;
                    case Property.A8: a = bReader.ReadByte(); break;

                    case Property.R16: r = (byte)(bReader.ReadUInt16() >> 8); break;
                    case Property.G16: g = (byte)(bReader.ReadUInt16() >> 8); break;
                    case Property.B16: b = (byte)(bReader.ReadUInt16() >> 8); break;
                    case Property.A16: a = (byte)(bReader.ReadUInt16() >> 8); break;

                    case Property.SINGLE_X: x = bReader.ReadSingle(); break;
                    case Property.SINGLE_Y: y = bReader.ReadSingle(); break;
                    case Property.SINGLE_Z: z = bReader.ReadSingle(); break;

                    case Property.DOUBLE_X: x = (float)bReader.ReadDouble(); break;
                    case Property.DOUBLE_Y: y = (float)bReader.ReadDouble(); break;
                    case Property.DOUBLE_Z: z = (float)bReader.ReadDouble(); break;

                    case Property.DATA_8: bReader.ReadByte(); break;
                    case Property.DATA_16: bReader.ReadUInt16(); break;
                    case Property.DATA_32: bReader.ReadSingle(); break;
                    case Property.DATA_64: bReader.ReadDouble(); break;
                }
            }

            return new Vector4(x, y, z, ((r << 24) | (g << 16) | (b << 8) | a));

        }

        Vector4 ReadPointBBE() {                               
            throw new NotImplementedException();
        }

        int CalculateLineBytes() {
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
    }

}