using UnityEngine;

namespace FastPoints {
    // axis-aligned bounding box
    public struct AABB {
        public Vector3 Min;
        public Vector3 Max;

        public AABB(Vector3 Min, Vector3 Max) {
            this.Min = Min;
            this.Max = Max;
        }

        public AABB[] Subdivide(int count) {
            AABB[] result = new AABB[count * count * count];

            float minX, minY, minZ;
            float maxX, maxY, maxZ;
            float xStep, yStep, zStep;

            minX = minY = minZ = 0;

            xStep = maxX = Mathf.Lerp(Min.x, Max.x, 1f/count);
            yStep = maxY = Mathf.Lerp(Min.y, Max.y, 1f/count);
            zStep = maxZ = Mathf.Lerp(Min.z, Max.z, 1f/count);

            for (int x = 0; x < count; x++) {
                for (int y = 0; y < count; y++) {
                    for (int z = 0; z < count; z++) {
                        result[count * count * x + count * y + z] = new AABB(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
                        minZ = maxZ;
                        maxZ += zStep;
                    }
                    minY = maxY;
                    maxY += yStep;
                }
                minX = maxX;
                maxX += xStep;
            }
             
            return result;
        }

        public bool InAABB(Vector3 pos) {
            return (
                Min.x <= pos.x && pos.x <= Max.x &&
                Min.y <= pos.y && pos.y <= Max.y &&
                Min.z <= pos.z && pos.z <= Max.z
            );
        }
    }
}