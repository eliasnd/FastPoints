using UnityEngine;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FastPoints {
    // Class for octree operations
    // Eventually add dynamic level increases like Potree 2.0

    public class Octree {
        ConcurrentQueue<Action> unityActions;   // Used for sending compute shader calls to main thread
        int treeDepth;
        string dirPath;
        public Octree(int treeDepth, string dirPath, ConcurrentQueue<Action> unityActions) {
            this.treeDepth = treeDepth;
            this.unityActions = unityActions;
            this.dirPath = dirPath;
        }

        public void BuildTree() {
            throw new NotImplementedException();
        }
    }

    struct BuildTreeParams {
        string dirPath;
        int treeDepth;


        public BuildTreeParams(string dirPath, int treeDepth) {
            this.dirPath = dirPath;
            this.treeDepth = treeDepth;
        }
    }
}