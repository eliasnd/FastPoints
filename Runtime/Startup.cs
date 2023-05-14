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
using UnityEditor;

namespace FastPoints
{
    [InitializeOnLoad]
    public class Startup
    {
        static Startup()
        {
            AppMonitor[] monitors = GameObject.FindObjectsOfType<AppMonitor>();
            foreach (AppMonitor am in monitors)
                Object.DestroyImmediate(am.gameObject);

            GameObject go = new GameObject();
            go.name = "AppMonitor";
            go.AddComponent<AppMonitor>();

            FastPointsNativeApi.InitializeConverter();
        }
    }

    public class AppMonitor : MonoBehaviour
    {
        public enum RenderWindow { SCENE_VIEW, GAME_VIEW }
        RenderWindow mostRecentRenderWindow = RenderWindow.SCENE_VIEW;
        public RenderWindow MostRecentRenderWindow { get { return mostRecentRenderWindow; } }

        public void Awake()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.LockReloadAssemblies();
#endif
        }

        public void Update() {
            string focusedWindowName = EditorWindow.focusedWindow == null ? null : EditorWindow.focusedWindow.ToString();

            if (focusedWindowName == " (UnityEditor.SceneView)")
                mostRecentRenderWindow = RenderWindow.SCENE_VIEW;
            else if (focusedWindowName == " (UnityEditor.GameView)")
                mostRecentRenderWindow = RenderWindow.GAME_VIEW;
        }

        void OnApplicationQuit()
        {
            //Prevent editor from reloading dlls files during the gameplay
#if UNITY_EDITOR
            UnityEditor.EditorApplication.UnlockReloadAssemblies();
#endif
            FastPointsNativeApi.AbortConverter();
        }
    }
}