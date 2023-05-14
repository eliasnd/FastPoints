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
using System.Collections.Generic;
using System.IO;

namespace FastPoints
{
    // [ExecuteInEditMode]
    public class OrbitCamera : MonoBehaviour // : LoggableCamera
    {
        public float orbitDuration = 2.5f;
        public float radius;
        public float inclination;   // Not implemented
        public bool useLookAt = true;
        public Vector3 lookAt;
        public bool showPath = false;
        float oldRadius;
        float oldInclination;

        float t = 0;

        int positionCount = 50;
        Vector3[] positions;

        public void Start()
        {
            // LineRenderer lr = gameObject.GetComponent<LineRenderer>() ?? gameObject.AddComponent<LineRenderer>();
            LineRenderer lr = gameObject.GetComponent<LineRenderer>();

            if (!lr)
                lr = gameObject.AddComponent<LineRenderer>();
            lr.loop = true;

            UpdatePositions();
        }

        public void Update()
        {
            transform.position = new Vector3(Mathf.Sin(t) * radius, 0, Mathf.Cos(t) * radius);

            if (useLookAt)
                transform.LookAt(lookAt);

            t = (t + Time.deltaTime / orbitDuration) % (2 * Mathf.PI);

            if (oldRadius != radius || oldInclination != inclination)
                UpdatePositions();

            gameObject.GetComponent<LineRenderer>().enabled = showPath;

            oldRadius = radius;
            oldInclination = inclination;

            // super();
        }

        public void UpdatePositions()
        {
            positions = new Vector3[positionCount];
            for (int i = 0; i < positionCount; i++)
                positions[i] = new Vector3(Mathf.Sin(Mathf.PI * 2f / (float)positionCount * i) * radius, 0, Mathf.Cos(Mathf.PI * 2f / (float)positionCount * i) * radius);

            LineRenderer lr = gameObject.GetComponent<LineRenderer>();

            lr.positionCount = positionCount;
            lr.SetPositions(positions);
        }
    }
}