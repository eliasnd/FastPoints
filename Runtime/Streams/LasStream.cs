using UnityEngine;

using System;
using System.Collections.Generic;
using System.IO;

namespace FastPoints {
    public class LasStream : BaseStream {
        
        public LasStream(string path) : base(path) {}

        public override bool ReadPoints(int pointCount, Vector4[] target) {
            throw new NotImplementedException();
        }

        public override bool SamplePoints(int pointCount, Vector4[] target) {
            throw new NotImplementedException();
        }
    }
}