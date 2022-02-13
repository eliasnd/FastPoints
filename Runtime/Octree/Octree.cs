using UnityEngine;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

using Debug = UnityEngine.Debug;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    public class Octree {
        Dispatcher dispatcher;   // Used for sending compute shader calls to main thread
        int treeDepth;
        string dirPath;
        AABB bbox;

        // Construction params
        int batchSize = 5000000;  // Size of batches for reading and running shader

        bool doTests = true;

        // Debug
        Stopwatch watch;
        TimeSpan checkpoint = new TimeSpan();
        ComputeShader computeShader = (ComputeShader)Resources.Load("CountAndSort");


        public Octree(int treeDepth, string dirPath, Dispatcher dispatcher) {
            this.treeDepth = treeDepth;
            this.dispatcher = dispatcher;
            this.dirPath = dirPath;
        }

        public async Task BuildTree(PointCloudData data) {
            if (data.MinPoint.x >= data.MaxPoint.x || data.MinPoint.y >= data.MaxPoint.y || data.MinPoint.z >= data.MaxPoint.z) 
                await data.PopulateBounds();

            // await Chunker.MakeChunks(data, dirPath, dispatcher);

            // foreach (string chunkPath in Directory.GetFiles($"{dirPath}/chunks"))

            Task indexTask;
            try {
                string chunkPath = Directory.GetFiles($"{dirPath}/chunks")[0];
                indexTask = Indexer.IndexChunk(data, chunkPath, dispatcher);
                await indexTask;
            } catch (Exception e) {
                Debug.LogError(e.InnerException);
            }
            
        }
    }

    struct BuildTreeParams {
        public Octree tree;
        public PointCloudData data;
        public BuildTreeParams(Octree tree, PointCloudData data) {
            this.tree = tree;
            this.data = data;
        }
    }

    struct Chunk {
        public uint size;
        public AABB bbox;
        public string path;
        public int level;
        public int x;
        public int y;
        public int z;
        public Chunk(int x, int y, int z, int level, uint size) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.level = level;
            this.bbox = new AABB(new Vector3(1,1,1), new Vector3(0,0,0));
            this.path = null;
            this.size = size;
        }
    }
}