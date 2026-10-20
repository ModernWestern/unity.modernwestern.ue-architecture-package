using System;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Opens and streams levels, like Unreal's OpenLevel and level streaming.
    /// <code>
    /// Levels.Open(nextLevel);          // ends this level, shows the loading screen, loads and begins the next one
    /// Levels.Stream("Level 1 - Caves"); // loads a scene into the current level (additive)
    /// Levels.Unstream("Level 1 - Caves");
    /// </code>
    /// Scenes can be given as a <see cref="SceneReference"/>, a name or a path, and must be in Build Settings.
    /// </summary>
    public static class Levels
    {
        private static LevelLoad _opening;

#if UNITY_EDITOR
        private static readonly System.Collections.Generic.HashSet<string> WarnedScenes = new();

        private static bool EditorOnlyScene_Internal(string scene)
        {
            return scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scene) != null;
        }
#endif

        private static readonly System.Collections.Generic.Dictionary<LoadingScreen, LoadingScreen> LoadingScreens = new();

        /// <summary>
        /// Raised when a level opened with <see cref="Open(string)"/> began play and its loading screen is gone.
        /// </summary>
        public static event Action<Scene> Opened;

        /// <summary>
        /// True while <see cref="Open(string)"/> is loading a level.
        /// </summary>
        public static bool IsOpening => _opening != null && !_opening.IsDone;

        public static LevelLoad Open(SceneReference level)
        {
            return Open(level?.Path);
        }

        public static LevelLoad Open(SceneReference level, LoadingScreen loadingScreen)
        {
            return Open(level?.Path, loadingScreen);
        }

        /// <summary>
        /// Ends the current level and opens another one (the old scenes are unloaded). Shows the loading screen picked in
        /// Project Settings > UE Architecture, if any. While a level is opening, new calls are ignored and return that load.
        /// </summary>
        public static LevelLoad Open(string scene)
        {
            return Open(scene, GameFramework.Settings != null ? GameFramework.Settings.LoadingScreen : null);
        }

        /// <summary>
        /// Like <see cref="Open(string)"/> with a loading screen of your choice (a prefab), or none (null).
        /// </summary>
        public static LevelLoad Open(string scene, LoadingScreen loadingScreen)
        {
            if (IsOpening)
            {
                Debug.LogWarning($"[UEArchitecture] Levels.Open(\"{scene}\") ignored: \"{_opening.Scene}\" is still opening.");

                return _opening;
            }

            var load = new LevelLoad(scene);

            if (!CanLoad_Internal(scene, load) || !RequireRunning_Internal(load))
            {
                return load;
            }

            _opening = load;

            GameInstance.Current.StartCoroutine(Open_Internal(load, loadingScreen));

            return load;
        }

        public static LevelLoad Stream(SceneReference scene)
        {
            return Stream(scene?.Path);
        }

        /// <summary>
        /// Loads a scene into the current level (additive), e.g. a part of a big map or the lighting of a level.
        /// It does not begin a new level. Does nothing if the scene is already loaded.
        /// </summary>
        public static LevelLoad Stream(string scene)
        {
            var load = new LevelLoad(scene);

            if (!CanLoad_Internal(scene, load))
            {
                return load;
            }

            if (IsLoaded(scene))
            {
                load.Complete_Internal(true);

                return load;
            }

            var operation = Load_Internal(scene, LoadSceneMode.Additive);

            load.Track_Internal(operation);

            operation.completed += _ => load.Complete_Internal(true);

            return load;
        }

        public static LevelLoad Unstream(SceneReference scene)
        {
            return Unstream(scene?.Path);
        }

        /// <summary>
        /// Unloads a scene that was streamed in. Does nothing if it is not loaded.
        /// </summary>
        public static LevelLoad Unstream(string scene)
        {
            var load = new LevelLoad(scene);

            if (!IsLoaded(scene))
            {
                load.Complete_Internal(true);

                return load;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);

            if (operation == null)
            {
                Debug.LogError($"[UEArchitecture] Could not unload \"{scene}\" (the last loaded scene cannot be unloaded).");

                load.Complete_Internal(false);

                return load;
            }

            load.Track_Internal(operation);

            operation.completed += _ => load.Complete_Internal(true);

            return load;
        }

        /// <summary>
        /// True if a scene with this name or path is loaded.
        /// </summary>
        public static bool IsLoaded(string scene)
        {
            if (string.IsNullOrEmpty(scene))
            {
                return false;
            }

            var loaded = scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? SceneManager.GetSceneByPath(scene) : SceneManager.GetSceneByName(scene);

            return loaded.IsValid() && loaded.isLoaded;
        }

        internal static void Reset_Internal()
        {
#if UNITY_EDITOR
            WarnedScenes.Clear();
#endif
            _opening = null;

            LoadingScreens.Clear();

            Opened = null;
        }

        private static IEnumerator Open_Internal(LevelLoad load, LoadingScreen loadingScreen)
        {
            var screen = LoadingScreen_Internal(loadingScreen);

            var shownAt = Time.unscaledTime;

            if (screen != null)
            {
                yield return screen.Show_Internal();
            }

            var operation = Load_Internal(load.Scene, LoadSceneMode.Single);

            // Load in the background first, and swap only when ready: the current level keeps running meanwhile.
            operation.allowSceneActivation = false;

            while (!operation.isDone)
            {
                if (!operation.allowSceneActivation && operation.progress >= 0.9f)
                {
                    // End the level while its objects are still alive, so EndPlay code can use them. Its subsystems
                    // stay until its scene is unloaded.
                    World.EndPlay_Internal();

                    operation.allowSceneActivation = true;
                }

                var progress = Mathf.Clamp01(operation.progress / 0.9f);

                load.SetProgress_Internal(progress);

                if (screen != null)
                {
                    screen.SetProgress_Internal(progress);
                }

                yield return null;
            }

            if (screen != null)
            {
                screen.SetProgress_Internal(1f);

                while (Time.unscaledTime - shownAt < screen.MinimumSeconds)
                {
                    yield return null;
                }

                yield return screen.Hide_Internal();
            }

            _opening = null;

            load.Complete_Internal(true);

            var opened = Opened;

            opened?.Invoke(World.Scene);
        }

        // One instance per loading screen prefab, kept across levels.
        private static LoadingScreen LoadingScreen_Internal(LoadingScreen prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            if (LoadingScreens.TryGetValue(prefab, out var screen) && screen != null)
            {
                return screen;
            }

            screen = Object.Instantiate(prefab);

            screen.name = prefab.name;

            screen.gameObject.SetActive(false);

            Object.DontDestroyOnLoad(screen.gameObject);

            LoadingScreens[prefab] = screen;

            return screen;
        }

        private static AsyncOperation Load_Internal(string scene, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.CanStreamedLevelBeLoaded(scene) && EditorOnlyScene_Internal(scene))
            {
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(mode));
            }
#endif
            return SceneManager.LoadSceneAsync(scene, mode);
        }

        private static bool CanLoad_Internal(string scene, LevelLoad load)
        {
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError("[UEArchitecture] No scene given to load.");

                load.Complete_Internal(false);

                return false;
            }

            if (Application.CanStreamedLevelBeLoaded(scene))
            {
                return true;
            }

#if UNITY_EDITOR
            // In the Editor, a scene asset given by path loads even if it is not in Build Settings, so prototypes and
            // samples just work. Builds still need it there: warn once per scene.
            if (EditorOnlyScene_Internal(scene))
            {
                if (WarnedScenes.Add(scene))
                {
                    Debug.LogWarning($"[UEArchitecture] \"{scene}\" is not in File > Build Settings. It loads in the Editor, but a build " +
                                     "will fail to load it: add it there (a SceneReference field in the Inspector has a button for it).");
                }

                return true;
            }
#endif

            Debug.LogError($"[UEArchitecture] \"{scene}\" cannot be loaded: it is not in File > Build Settings. Add it there " +
                           "(a SceneReference field in the Inspector has a button for it).");

            load.Complete_Internal(false);

            return false;
        }

        private static bool RequireRunning_Internal(LevelLoad load)
        {
            if (GameInstance.IsRunning && GameInstance.Current != null)
            {
                return true;
            }

            Debug.LogError("[UEArchitecture] Levels.Open needs the framework running (it starts by itself unless it was turned off in Project Settings).");

            load.Complete_Internal(false);

            return false;
        }
    }
}
