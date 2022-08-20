using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace FastPoints {
    [ExecuteInEditMode]
    public class PathCamera : MonoBehaviour {
        public float transitionDuration = 2.5f;
        public Vector3[] positions;
        int currIdx = 0;
        int nextIdx;
        float t = 0;
        public bool useLookAt = true;
        public Vector3 lookAt;
        public bool showPath = false;

        // FPS Logging
        public int averageInterval = 10;
        int sample = 0;
        float intervalAverage = 0;
        public bool resetFPSCount;
        public bool debugFPSCount;
        List<float> fpsCounts = new List<float>();
        string fpsCountString = "";
        FileStream fs;

        public void LogFPS(string target = "fps.csv") {
            Debug.Log($"Log FPS: {string.Join(",", fpsCounts)}");
            File.WriteAllTextAsync("fps.csv", string.Join(",", fpsCounts));
        }

        public void Update() {
            if (positions.Length <= 1)
                return;

            if (resetFPSCount) {
                Debug.Log("Reset FPS Count");
                fpsCounts.Clear();
                fpsCountString = "";
                resetFPSCount = false;
            }

            if (debugFPSCount) {
                Debug.Log("Debug FPS Count");
                string output = string.Join(",", fpsCounts);
                // Debug.Log("FPS Debug: " + output);
                
                debugFPSCount = false;
            }

            if (sample == averageInterval) {
                fpsCounts.Add(intervalAverage / averageInterval);
                sample = 0;
                intervalAverage = 0;
            }

            intervalAverage += 1f / Time.unscaledDeltaTime;
            sample++;


            LineRenderer lr = gameObject.GetComponent<LineRenderer>();

            if (!lr)
                lr = gameObject.AddComponent<LineRenderer>();

            lr.enabled = showPath;

            lr.positionCount = positions.Length;
            lr.loop = true;
            lr.SetPositions(positions);

            if (t >= 1) {
                t = 0;
                currIdx = nextIdx;
                nextIdx = (currIdx+1) % positions.Length;
            }
            transform.position = Vector3.Lerp(positions[currIdx], positions[nextIdx], t);
            t += Time.deltaTime / transitionDuration;

            if (useLookAt)
                transform.LookAt(lookAt);
        }
    }
}