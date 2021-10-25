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

        public override void OnImportAsset(AssetImportContext context) {
            path = context.assetPath;
            index = 0;
        }

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

            switch (format) {
                case Format.BINARY_LITTLE_ENDIAN:
                    return ReadPointsBLE(pointCount, target);
                    break;
                case Format.BINARY_BIG_ENDIAN:
                    return ReadPointsBBE(pointCount, target);
                    break;
                case Format.ASCII:
                    return ReadPointsASCII(pointCount, target);
                    break;
                default:
                    return false;
                    break;
            }
        }

        void ReadHeader() {
            int bodyOffset = 0;

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


        bool ReadPointsBLE(int pointCount, Vector4[] target) {

            bool result = index + pointCount >= count;

            if (result)
                pointCount = count - index;

            for (int i = 0; i < pointCount; i++) {

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

                target[i] = new Vector4(x, y, z, ((r << 24) | (g << 16) | (b << 8) | a));
            }

            return result;
        }

        bool ReadPointsBBE(int pointCount, Vector4[] target) {
            return false;
        }

        bool ReadPointsASCII(int pointCount, Vector4[] target) {
            return false;
        }
    }

}