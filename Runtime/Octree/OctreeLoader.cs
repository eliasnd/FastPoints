using UnityEngine;
using System;
using System.Collections.Generic;
using System.Numerics;

using Vector3 = UnityEngine.Vector3;

namespace FastPoints
{
    public class JsonAttribute
    {
        public string name;
        public string description;
        public int size;
        public int numElements;
        public int elementSize;
        public string type;
        public float[] min;
        public float[] max;
    }

    public class Hierarchy
    {
        public int firstChunkSize;
        public int stepSize;
        public int depth;
    }

    public class BBox
    {
        public float[] min;
        public float[] max;
    }

    public class Metadata
    {
        public string version;
        public string name;
        public string description;
        public int points;
        public string projection;

        public Hierarchy hierarchy;
        public float[] offset;
        public float[] scale;
        public float spacing;
        public BBox boundingBox;

        public string encoding;
        public JsonAttribute[] attributes;
    }

    public class OctreeLoader
    {

        static PointAttributeType AttributeNameMap(string name)
        {
            switch (name)
            {
                case "double": return PointAttributeTypes.DATA_TYPE_DOUBLE;
                case "float": return PointAttributeTypes.DATA_TYPE_FLOAT;
                case "int8": return PointAttributeTypes.DATA_TYPE_INT8;
                case "uint8": return PointAttributeTypes.DATA_TYPE_UINT8;
                case "int16": return PointAttributeTypes.DATA_TYPE_INT16;
                case "uint16": return PointAttributeTypes.DATA_TYPE_UINT16;
                case "int32": return PointAttributeTypes.DATA_TYPE_INT32;
                case "uint32": return PointAttributeTypes.DATA_TYPE_UINT32;
                case "int64": return PointAttributeTypes.DATA_TYPE_INT64;
                case "uint64": return PointAttributeTypes.DATA_TYPE_UINT64;
                default: throw new Exception("Unsupported attribute name!");
            }
        }

        static PointAttributes ParseAttributes(JsonAttribute[] jsonAttributes)
        {

            PointAttributes attributes = new PointAttributes();

            Dictionary<string, string> replacements = new Dictionary<string, string>();
            replacements.Add("rgb", "rgba");

            foreach (JsonAttribute jsonAttribute in jsonAttributes)
            {

                PointAttributeType type = AttributeNameMap(jsonAttribute.type);

                string potreeAttributeName = replacements.ContainsKey(jsonAttribute.name) ? replacements[jsonAttribute.name] : jsonAttribute.name;

                PointAttribute attribute = new PointAttribute(potreeAttributeName, type, jsonAttribute.numElements);

                attribute.range = new float[] { jsonAttribute.min[0], jsonAttribute.max[0] };

                if (jsonAttribute.name == "gps-time" && attribute.range[0] == attribute.range[1])  // HACK: Guard against bad gpsTime range in metadata, see potree/potree#909
                    attribute.range[1] += 1;

                attribute.initialRange = attribute.range;

                attributes.Add(attribute);
            }

            // check if it has normals
            bool hasNormals =
                attributes.attributes.Find(a => a.name == "NormalX") != null &&
                attributes.attributes.Find(a => a.name == "NormalY") != null &&
                attributes.attributes.Find(a => a.name == "NormalZ") != null;

            if (hasNormals)
            {
                Vector vector = new Vector("NORMAL", new string[] { "NormalX", "NormalY", "NormalZ" });
                attributes.AddVector(vector);
            }

            return attributes;
        }

        public static OctreeGeometry Load(string path)
        {
            Metadata metadata = JSONParser.FromJson<Metadata>(System.IO.File.ReadAllText(path));

            PointAttributes attributes = ParseAttributes(metadata.attributes);

            NodeLoader loader = new NodeLoader(path);
            loader.metadata = metadata;
            loader.attributes = attributes;
            loader.scale = metadata.scale;
            loader.offset = metadata.offset;

            OctreeGeometry octree = new OctreeGeometry();
            octree.path = path;
            octree.spacing = metadata.spacing;
            octree.scale = metadata.scale;

            // 		// let aPosition = metadata.attributes.find(a => a.name === "position");
            // 		// octree

            Vector3 min = new Vector3(metadata.boundingBox.min[0], metadata.boundingBox.min[1], metadata.boundingBox.min[2]);
            Vector3 max = new Vector3(metadata.boundingBox.max[0], metadata.boundingBox.max[1], metadata.boundingBox.max[2]);
            AABB boundingBox = new AABB(min, max);

            Vector3 offset = min;
            boundingBox.Min -= offset;
            boundingBox.Max -= offset;

            octree.projection = metadata.projection;
            octree.boundingBox = boundingBox;
            // 		octree.tightBoundingBox = boundingBox.clone();
            // 		octree.boundingSphere = boundingBox.getBoundingSphere(new THREE.Sphere());
            // 		octree.tightBoundingSphere = boundingBox.getBoundingSphere(new THREE.Sphere());
            octree.offset = offset;
            octree.pointAttributes = ParseAttributes(metadata.attributes);
            octree.loader = loader;

            OctreeGeometryNode root = new OctreeGeometryNode("r", octree, boundingBox);
            root.level = 0;
            root.nodeType = 2;
            root.hierarchyByteOffset = 0;
            root.hierarchyByteSize = metadata.hierarchy.firstChunkSize;
            root.hasChildren = false;
            root.spacing = octree.spacing;
            root.byteOffset = 0;

            octree.root = root;

            loader.Load(root);

            return octree;
        }
    }
}