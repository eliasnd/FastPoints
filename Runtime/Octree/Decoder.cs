// FastPoints
// Copyright (C) 2023  Elias Neuman-Donihue

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using UnityEngine;
using System;
using System.Collections.Generic;

namespace FastPoints {

    using DecoderOutput = Dictionary<string, Tuple<byte[], PointAttribute>>;

    public struct DecoderInput {
        public byte[] buffer;
        public PointAttributes pointAttributes;
        public float[] scale;
        public string name;
        public Vector3 min;
        public Vector3 max;
        public Vector3 size;
        public Vector3 offset;
        public int numPoints;

        public DecoderInput(byte[] buffer, PointAttributes pointAttributes, float[] scale, string name, Vector3 min, Vector3 max, Vector3 size, Vector3 offset, int numPoints) {
            this.buffer = buffer;
            this.pointAttributes = pointAttributes;
            this.scale = scale;
            this.name = name;
            this.min = min;
            this.max = max;
            this.size = size;
            this.offset = offset;
            this.numPoints = numPoints;
        }
    }

    public class Decoder {
        public static DecoderOutput Decode(DecoderInput encoded) {
	
	        DecoderOutput attributeBuffers = new DecoderOutput();
	        int attributeOffset = 0;

	        int bytesPerPoint = 0;
            foreach (PointAttribute pointAttribute in encoded.pointAttributes.attributes)
               bytesPerPoint += pointAttribute.byteSize;

            int gridSize = 32;
            UInt32[] grid = new UInt32[gridSize * gridSize * gridSize];

            int ToIndex(float x, float y, float z) {
                float dx = gridSize * x / encoded.size.x;
                float dy = gridSize * y / encoded.size.y;
                float dz = gridSize * z / encoded.size.z;

                int ix = (int)Mathf.Min(dx, gridSize - 1f);
                int iy = (int)Mathf.Min(dy, gridSize - 1f);
                int iz = (int)Mathf.Min(dz, gridSize - 1f);

                int index = ix + iy * gridSize + iz * gridSize * gridSize;

                return index;
            }

            int numOccupiedCells = 0;

            foreach (PointAttribute pointAttribute in encoded.pointAttributes.attributes) {
                if (pointAttribute.name == "POSITION_CARTESIAN" || pointAttribute.name == "position") {
                    float[] positions = new float[encoded.numPoints * 3];

                    for (int j = 0; j < encoded.numPoints; j++) {
                        int pointOffset = j * bytesPerPoint;

                        float x = (BitConverter.ToInt32(encoded.buffer, pointOffset + attributeOffset + 0) * encoded.scale[0]) + encoded.offset[0] - encoded.min.x;
                        float y = (BitConverter.ToInt32(encoded.buffer, pointOffset + attributeOffset + 4) * encoded.scale[1]) + encoded.offset[1] - encoded.min.y;
                        float z = (BitConverter.ToInt32(encoded.buffer, pointOffset + attributeOffset + 8) * encoded.scale[2]) + encoded.offset[2] - encoded.min.z;

                        // int index = ToIndex(x, y, z);
                        // int count = grid[index]++;
                        // if (count == 0)
                        //     numOccupiedCells++;

                        positions[3 * j + 0] = x;
                        positions[3 * j + 1] = y;
                        positions[3 * j + 2] = z;
                    }

                    byte[] buffer = new byte[encoded.numPoints * 4 * 3];
                    Buffer.BlockCopy(positions, 0, buffer, 0, buffer.Length);

                    float[] positionsTest = new float[encoded.numPoints * 3];
                    Buffer.BlockCopy(buffer, 0, positions, 0, buffer.Length);

                    // attributeBuffers.Add(pointAttribute.name, new Tuple<byte[], PointAttribute>(buffer, pointAttribute));
                    attributeBuffers.Add("position", new Tuple<byte[], PointAttribute>(buffer, pointAttribute));

                } else if (pointAttribute.name == "RGBA" || pointAttribute.name == "rgba") {

                    byte[] buffer = new byte[encoded.numPoints * 4];

                    for (int j = 0; j < encoded.numPoints; j++) {
                        int pointOffset = j * bytesPerPoint;

                        UInt16 r = BitConverter.ToUInt16(encoded.buffer, pointOffset + attributeOffset + 0);
                        UInt16 g = BitConverter.ToUInt16(encoded.buffer, pointOffset + attributeOffset + 2);
                        UInt16 b = BitConverter.ToUInt16(encoded.buffer, pointOffset + attributeOffset + 4);

                        buffer[4 * j + 0] = (byte)(r > 255 ? r / 256 : r);
                        buffer[4 * j + 1] = (byte)(g > 255 ? g / 256 : g);
                        buffer[4 * j + 2] = (byte)(b > 255 ? b / 256 : b);
                    }

                    //byte[] buffer = new byte[encoded.numPoints * 4];
                    //Buffer.BlockCopy(colors, 0, buffer, 0, buffer.Length);

                    //char[] testCol = new char[encoded.numPoints * 4];
                    //Buffer.BlockCopy(buffer, 0, testCol, 0, buffer.Length);

                    // attributeBuffers.Add(pointAttribute.name, new Tuple<byte[], PointAttribute>(buffer, pointAttribute));
                    attributeBuffers.Add("rgba", new Tuple<byte[], PointAttribute>(buffer, pointAttribute));
                } else {
                    // Don't handle yet
                }

                attributeOffset += pointAttribute.byteSize;
            }

            return attributeBuffers;
        }
    }
}