using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Project-wide settings, like Unreal's Project Settings > Maps &amp; Modes. Edit them in Project Settings > UE Architecture.
    /// Everything is optional: without this asset the framework still starts with the defaults.
    /// </summary>
    public sealed class UEArchitectureSettings : ScriptableObject
    {
        /// <summary>
        /// Name of the asset inside a Resources folder, so players can load it.
        /// </summary>
        public const string ResourcesName = "UEArchitectureSettings";

        [SerializeField, Tooltip("Prefab with your GameInstance component. Empty = a plain GameInstance.")]
        private GameInstance gameInstance;

        [SerializeField, Tooltip("GameMode prefab used by levels that do not pick one in their World Settings. Empty = no GameMode.")]
        private GameMode defaultGameMode;

        [SerializeField, Tooltip("Shown while Levels.Open loads a level. Empty = no loading screen.")]
        private LoadingScreen loadingScreen;

        [SerializeField, Tooltip("Start the framework before the first scene loads, from any scene you press Play in. " +
                                 "Turn it off only if you start it yourself with GameInstance.Launch().")]
        private bool startAutomatically = true;

        private static UEArchitectureSettings _defaults;

        public GameInstance GameInstancePrefab => gameInstance;

        public GameMode DefaultGameMode => defaultGameMode;

        public LoadingScreen LoadingScreen => loadingScreen;

        public bool StartAutomatically => startAutomatically;

        /// <summary>
        /// The project's settings, or the defaults if the project has none.
        /// </summary>
        public static UEArchitectureSettings Current
        {
            get
            {
                var settings = Resources.Load<UEArchitectureSettings>(ResourcesName);

                if (settings != null)
                {
                    return settings;
                }

                if (_defaults == null)
                {
                    _defaults = CreateInstance<UEArchitectureSettings>();

                    _defaults.hideFlags = HideFlags.HideAndDontSave;
                }

                return _defaults;
            }
        }

        internal void Set_Internal(GameInstance gameInstancePrefab, GameMode gameModePrefab, LoadingScreen loadingScreenPrefab, bool start = true)
        {
            gameInstance = gameInstancePrefab;

            defaultGameMode = gameModePrefab;

            loadingScreen = loadingScreenPrefab;

            startAutomatically = start;
        }
    }
}
