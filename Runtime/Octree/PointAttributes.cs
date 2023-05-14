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

using System;
using System.Collections.Generic;

namespace FastPoints {
    public struct PointAttributeType {
        public int ordinal;
        public string name;
        public int size;

        public PointAttributeType(int ordinal, string name, int size) {
            this.ordinal = ordinal;
            this.name = name;
            this.size = size;
        }
    }

    public struct Vector {
        public string name;
        public string[] attributes;

        public Vector(string name, string[] attributes) {
            this.name = name;
            this.attributes = attributes;
        }
    }

    public class PointAttributeTypes {
        public static PointAttributeType DATA_TYPE_DOUBLE = new PointAttributeType(0, "double", 8);
        public static PointAttributeType DATA_TYPE_FLOAT =  new PointAttributeType(1, "float",  4);
        public static PointAttributeType DATA_TYPE_INT8 =   new PointAttributeType(2, "int8",   1);
        public static PointAttributeType DATA_TYPE_UINT8 =  new PointAttributeType(3, "uint8",  1);
        public static PointAttributeType DATA_TYPE_INT16 =  new PointAttributeType(4, "int16",  2);
        public static PointAttributeType DATA_TYPE_UINT16 = new PointAttributeType(5, "uint16", 2);
        public static PointAttributeType DATA_TYPE_INT32 =  new PointAttributeType(6, "int32",  4);
        public static PointAttributeType DATA_TYPE_UINT32 = new PointAttributeType(7, "uint32", 4);
        public static PointAttributeType DATA_TYPE_INT64 =  new PointAttributeType(8, "int64",  8);
        public static PointAttributeType DATA_TYPE_UINT64 = new PointAttributeType(9, "uint64", 8);
    }

    public class PointAttribute {
        public string name;
        public PointAttributeType type;
        public int numElements;
        public int byteSize;
        public string description;
        public float[] range;
        public float[] initialRange;
        
        public PointAttribute(string name, PointAttributeType type, int numElements) {
            this.name = name;
            this.type = type;
            this.numElements = numElements;
            this.byteSize = this.numElements * this.type.size;
            this.description = "";
            this.range = new float[] { Single.PositiveInfinity, Single.NegativeInfinity };
        }
    };

    public class PointAttributes {
        public List<PointAttribute> attributes;
        public List<Vector> vectors;
        public int byteSize;
        public int size;

        public static PointAttribute POSITION_CARTESIAN = new PointAttribute(
	        "POSITION_CARTESIAN", PointAttributeTypes.DATA_TYPE_FLOAT, 3);
        public static PointAttribute RGBA_PACKED = new PointAttribute(
            "COLOR_PACKED", PointAttributeTypes.DATA_TYPE_INT8, 4);
        public static PointAttribute COLOR_PACKED = RGBA_PACKED;
        public static PointAttribute RGB_PACKED = new PointAttribute(
            "COLOR_PACKED", PointAttributeTypes.DATA_TYPE_INT8, 3);
        public static PointAttribute NORMAL_FLOATS = new PointAttribute(
            "NORMAL_FLOATS", PointAttributeTypes.DATA_TYPE_FLOAT, 3);
        public static PointAttribute INTENSITY = new PointAttribute(
            "INTENSITY", PointAttributeTypes.DATA_TYPE_UINT16, 1);
        public static PointAttribute CLASSIFICATION = new PointAttribute(
            "CLASSIFICATION", PointAttributeTypes.DATA_TYPE_UINT8, 1);
        public static PointAttribute NORMAL_SPHEREMAPPED = new PointAttribute(
            "NORMAL_SPHEREMAPPED", PointAttributeTypes.DATA_TYPE_UINT8, 2);
        public static PointAttribute NORMAL_OCT16 = new PointAttribute(
            "NORMAL_OCT16", PointAttributeTypes.DATA_TYPE_UINT8, 2);
        public static PointAttribute NORMAL = new PointAttribute(
            "NORMAL", PointAttributeTypes.DATA_TYPE_FLOAT, 3);
        public static PointAttribute RETURN_NUMBER = new PointAttribute(
            "RETURN_NUMBER", PointAttributeTypes.DATA_TYPE_UINT8, 1);
        public static PointAttribute NUMBER_OF_RETURNS = new PointAttribute(
            "NUMBER_OF_RETURNS", PointAttributeTypes.DATA_TYPE_UINT8, 1);
        public static PointAttribute SOURCE_ID = new PointAttribute(
            "SOURCE_ID", PointAttributeTypes.DATA_TYPE_UINT16, 1);
        public static PointAttribute INDICES = new PointAttribute(
            "INDICES", PointAttributeTypes.DATA_TYPE_UINT32, 1);
        public static PointAttribute SPACING = new PointAttribute(
            "SPACING", PointAttributeTypes.DATA_TYPE_FLOAT, 1);
        public static PointAttribute GPS_TIME = new PointAttribute(
            "GPS_TIME", PointAttributeTypes.DATA_TYPE_DOUBLE, 1);

        public PointAttributes(List<PointAttribute> attributes = null) {
            this.attributes = attributes ?? new List<PointAttribute>();
            this.vectors = new List<Vector>();
            this.byteSize = 0;
            this.size = 0;

            if (attributes != null)
                for (int i = 0; i < attributes.Count; i++) {
                    byteSize += attributes[i].byteSize;
                    size++;
                }
        }

        public void Add(PointAttribute attribute) {
            attributes.Add(attribute);
            byteSize += attribute.byteSize;
            size++;
        }

        public void AddVector(Vector v) {
            vectors.Add(v);
        }

        public bool HasNormals() {
            foreach (PointAttribute attribute in attributes) {
                if (
                    attribute == NORMAL_SPHEREMAPPED ||
                    attribute == NORMAL_FLOATS ||
                    attribute == NORMAL ||
                    attribute == NORMAL_OCT16
                )
                    return true;
            }

            return false;
        }
    }
}
