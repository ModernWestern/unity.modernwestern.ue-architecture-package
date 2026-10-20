using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Starts and stops the framework: the GameInstance, its subsystems, the frame tick and the level lifecycle.
    /// </summary>
    internal static class GameFramework
    {
        public static readonly SubsystemCollection<GameInstanceSubsystem> GameSubsystems = new();

        public static GameInstance Instance { get; private set; }

        public static bool IsRunning { get; private set; }

        public static UEArchitectureSettings Settings { get; private set; }

        /// <summary>
        /// Raised after the GameInstance and its subsystems are ready (integrations hook in here).
        /// </summary>
        public static event Action Launched;

        // With "Enter Play Mode Options" and no domain reload, statics survive between Play sessions: forget the last one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics_Internal()
        {
            Unhook_Internal();

            FrameworkLoop.Uninstall();

            World.Reset_Internal();

            Levels.Reset_Internal();

            Timers.Reset_Internal();

            Messages.Reset_Internal();

            GameSubsystems.Clear();

            TickRegistry.Clear();

            FrameworkTypes.Reset();

            Instance = null;

            Settings = null;

            IsRunning = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LaunchOnLoad_Internal()
        {
            if (UEArchitectureSettings.Current.StartAutomatically)
            {
                // The first scene's objects are not awake yet: its level begins when it finishes loading.
                Launch(false);
            }
        }

        public static void Launch(bool beginCurrentLevel, UEArchitectureSettings settings = null)
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;

            Settings = settings != null ? settings : UEArchitectureSettings.Current;

            foreach (var tag in Settings.GameplayTags)
            {
                GameplayTagRegistry.Register(tag);
            }

            FrameworkLoop.Install();

            Instance = CreateGameInstance_Internal(Settings.GameInstancePrefab);

            GameSubsystems.Create();

            try
            {
                Instance.Init_Internal();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, Instance);
            }

            SceneManager.sceneLoaded += OnSceneLoaded_Internal;

            SceneManager.sceneUnloaded += OnSceneUnloaded_Internal;

            Application.quitting += Shutdown;

            Launched?.Invoke();

            var active = SceneManager.GetActiveScene();

            if (beginCurrentLevel && active.isLoaded)
            {
                World.Begin_Internal(active);
            }
        }

        public static void Shutdown()
        {
            if (!IsRunning)
            {
                return;
            }

            Unhook_Internal();

            World.End_Internal();

            try
            {
                if (Instance != null)
                {
                    Instance.Shutdown_Internal();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            GameSubsystems.Deinitialize();

            Timers.Reset_Internal();

            FrameworkLoop.Uninstall();

            IsRunning = false;
        }

        /// <summary>
        /// Starts over: a new GameInstance, new subsystems and the current scene as a new level. Used by tests.
        /// </summary>
        internal static void Relaunch_Internal(UEArchitectureSettings settings = null)
        {
            Shutdown();

            if (Instance != null)
            {
                Object.DestroyImmediate(Instance.gameObject);
            }

            Instance = null;

            GameSubsystems.Clear();

            FrameworkTypes.Reset();

            Launch(true, settings);
        }

        private static GameInstance CreateGameInstance_Internal(GameInstance prefab)
        {
            var instance = prefab != null ? Object.Instantiate(prefab) : new GameObject().AddComponent<GameInstance>();

            instance.name = "GameInstance";

            Object.DontDestroyOnLoad(instance.gameObject);

            return instance;
        }

        private static void OnSceneLoaded_Internal(Scene scene, LoadSceneMode mode)
        {
            // A single load opens a new level; an additive load streams into the current one.
            if (mode == LoadSceneMode.Single)
            {
                World.Begin_Internal(scene);
            }
            else if (!World.HasBegunPlay)
            {
                World.Begin_Internal(SceneManager.GetActiveScene());
            }
        }

        private static void OnSceneUnloaded_Internal(Scene scene)
        {
            if (World.HasBegunPlay && scene == World.Scene)
            {
                World.End_Internal();
            }
        }

        private static void Unhook_Internal()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded_Internal;

            SceneManager.sceneUnloaded -= OnSceneUnloaded_Internal;

            Application.quitting -= Shutdown;
        }
    }
}
