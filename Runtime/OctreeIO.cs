using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;

using Debug = UnityEngine.Debug;

namespace FastPoints {

    // Helper class for reading and writing octrees
    public class OctreeIO : MonoBehaviour {
        static int headerBytes = 0; // TODO: Number of bytes in ply file header System.Text.ASCIIEncoding.ASCII.GetByteCount
        static int pointBytes = 15; // Marshal.SizeOf<Point>();    // Number of bytes per point - should be 12 for position + 3 for color = 15(?)

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
            headerBytes = System.Text.ASCIIEncoding.ASCII.GetByteCount(header);
            System.IO.File.WriteAllText(path, header);
        }

        // Writes leaf nodes to file -- eventually expand to write whole tree
        // Params: 
        //  points - list of points to write
        //  sorted - node index of each point
        //  nodeOffsets - total number of points before each node starts
        //  nodeIndices - current index in each node
        public static async Task WriteLeafNodes(Point[] points, uint[] sorted, uint[] nodeOffsets, int[] nodeIndices, string root = "", int[] countWrapper = null) {
            await Task.Run(() => {
                Debug.Log("Task started");
                uint pointsWritten = 0;
                // Convert to dictionary
                Dictionary<uint, List<Point>> nodes = new Dictionary<uint, List<Point>>();
                for (int i = 0; i < points.Length; i++) {
                    if (!nodes.ContainsKey(sorted[i]))
                        nodes.Add(sorted[i], new List<Point>());
                    if (points[i].pos == Vector3.zero)
                        throw new Exception($"Found zero position at index {i}");
                    nodes[sorted[i]].Add(points[i]);
                }

                int pointSum = 0;
                foreach (List<Point> node in nodes.Values)
                    pointSum += node.Count;
                // if (points.Length != pointSum)
                    // throw new Exception($"Dictionary has {pointSum} points, array has {points.Length}");

                // Write points

                string path = root != "" ? $"{root}/leaf_nodes.ply" : $"leaf_nodes.ply";

                FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                BinaryWriter bw = new BinaryWriter(stream);
                foreach (KeyValuePair<uint, List<Point>> node in nodes) {
                    int startIdx = Interlocked.Add(ref nodeIndices[node.Key], node.Value.Count) - node.Value.Count;    // Should work as fetch-and-add operation, reserving space in file
                    if (node.Key == 3655)
                        Debug.Log($"Reserved {node.Value.Count} positions at index {startIdx} / {nodeOffsets[node.Key+1]-nodeOffsets[node.Key]} of node {node.Key}");
                    List<byte> bytes = new List<byte>(pointBytes * node.Value.Count);
                    for (int i = 0; i < node.Value.Count; i++) {
                        Point pt = node.Value[i];
                        byte[] x_bytes = BitConverter.GetBytes(pt.pos.x);
                        byte[] y_bytes = BitConverter.GetBytes(pt.pos.y);
                        byte[] z_bytes = BitConverter.GetBytes(pt.pos.z);

                        if (x_bytes.Length != 4 || y_bytes.Length != 4 || z_bytes.Length != 4)
                            throw new Exception("Wrong number of bytes from floats");
                        bytes.AddRange(new byte[] { 
                            x_bytes[0], x_bytes[1], x_bytes[2], x_bytes[3],
                            y_bytes[0], y_bytes[1], y_bytes[2], y_bytes[3],
                            z_bytes[0], z_bytes[1], z_bytes[2], z_bytes[3],
                            Convert.ToByte((int)(pt.col.r * 255.0f)),
                            Convert.ToByte((int)(pt.col.g * 255.0f)),
                            Convert.ToByte((int)(pt.col.b * 255.0f)),
                        });

                        // if (bytes.Count != pointBytes)
                            // throw new Exception($"Byte size {bytes.Count}, expected size {pointBytes}");
                    }

                    if (node.Key == 3655)
                        Debug.Log($"Writing {bytes.Count} bytes, {bytes.Count / pointBytes} points");
                    stream.Seek(headerBytes + (nodeOffsets[node.Key] + startIdx) * pointBytes, SeekOrigin.Begin);
                    bw.Write(bytes.ToArray(), 0, bytes.Count);
                }
                bw.Dispose();
                Debug.Log("Task done");

                if (countWrapper != null)
                    Interlocked.Add(ref countWrapper[0], points.Length);
            });
        }
    }
}