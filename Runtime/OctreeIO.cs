using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;

namespace FastPoints {

    // Helper class for reading and writing octrees
    public static class OctreeIO {
        static int headerBytes = 0; // TODO: Number of bytes in ply file header System.Text.ASCIIEncoding.ASCII.GetByteCount
        static int pointBytes = Marshal.SizeOf<Point>();    // Number of bytes per point - should be 12 for position + 3 for color = 15(?)

        // Creates leaf node file
        public static async void CreateLeafFile(uint pointCount, string root = "") {
            String path = root != "" ? $"{root}/leaf_nodes.ply" : $"leaf_nodes.ply";
            String header = $@"ply
            format binary_little_endian 1.0
            element vertex {pointCount}
            property float x
            property float y
            property float z
            property uchar red
            property uchar green
            property uchar blue
            end_header
            ";
            System.IO.File.WriteAllText(path, header);
        }

        // Writes leaf nodes to file -- eventually expand to write whole tree
        // Params: 
        //  points - list of points to write
        //  sorted - node index of each point
        //  nodeOffsets - total number of points before each node starts
        //  nodeIndices - current index in each node
        public static async void WriteLeafNodes(Point[] points, uint[] sorted, uint[] nodeOffsets, int[] nodeIndices, string root = "") {
            await Task.Run(() => {
                // Convert to dictionary
                Dictionary<uint, List<Point>> nodes = new Dictionary<uint, List<Point>>();
                for (int i = 0; i < points.Length; i++) {
                    if (!nodes.ContainsKey(sorted[i]))
                        nodes.Add(sorted[i], new List<Point>());
                    nodes[sorted[i]].Add(points[i]);
                }

                // Write points

                string path = root != "" ? $"{root}/leaf_nodes.ply" : $"leaf_nodes.ply";

                FileStream stream = File.Open(path, FileMode.Open);
                BinaryWriter bw = new BinaryWriter(stream);
                foreach (KeyValuePair<uint, List<Point>> node in nodes) {
                    int startIdx = Interlocked.Add(ref nodeIndices[node.Key], node.Value.Count) - node.Value.Count;    // Should work as fetch-and-add operation, reserving space in file
                    List<byte> bytes = new List<byte>(pointBytes * node.Value.Count);
                    for (int i = 0; i < node.Value.Count; i++) {
                        Point pt = node.Value[i];
                        byte[] x_bytes = BitConverter.GetBytes(pt.pos.x);
                        byte[] y_bytes = BitConverter.GetBytes(pt.pos.y);
                        byte[] z_bytes = BitConverter.GetBytes(pt.pos.z);
                        bytes.AddRange(new byte[] { 
                            x_bytes[0], x_bytes[1], x_bytes[2], x_bytes[3],
                            y_bytes[0], y_bytes[1], y_bytes[2], y_bytes[3],
                            z_bytes[0], z_bytes[1], z_bytes[2], z_bytes[3],
                            Convert.ToByte((int)(pt.col.r * 255.0f)),
                            Convert.ToByte((int)(pt.col.g * 255.0f)),
                            Convert.ToByte((int)(pt.col.b * 255.0f)),
                        });
                    }
                    stream.Seek(headerBytes + (nodeOffsets[node.Key] + startIdx) * pointBytes, SeekOrigin.Begin);
                    bw.Write(bytes.ToArray(), 0, bytes.Count);
                }
                bw.Dispose();
            });
            
        }
    }
}