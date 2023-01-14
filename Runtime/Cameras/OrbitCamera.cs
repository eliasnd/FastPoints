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