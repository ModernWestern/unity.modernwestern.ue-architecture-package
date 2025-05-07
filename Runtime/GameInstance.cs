namespace ModernWestern.UEArchitecture
{
    using TMPro;
    using System;
    using UnityEngine;
    using System.Collections.Generic;
    using UnityEngine.SceneManagement;
    
    using GS = GameState;
    using PS = PlayerState;
    using GE = GameEvents;

    public abstract class GameInstanceBase : MonoBehaviour
    {
        protected static Func<(GS gameState, PS playerState, GE gameEvents)> PersistentData;

        protected readonly static Stack<string> SceneStack = new();

        protected static string UnRootedSceneName;

        public static event Action<string> OnSceneChanged;

        public static bool Exists { get; private set; }

        [field: SerializeField, HideInInspector]
        public string SelectedScene { get; private set; }

        protected abstract void Construct();

        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(UnRootedSceneName))
            {
                LoadScene(SelectedScene, false);
            }

            Exists = true;

            Construct();
        }

        public static void LoadScene(string sceneName, bool unloadPreviousScene = true)
        {
            if (SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                SceneManager.UnloadSceneAsync(sceneName);

                RebuildStack(sceneName);
            }

            SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (unloadPreviousScene && SceneStack.Count > 0)
            {
                SceneManager.UnloadSceneAsync(SceneStack.Pop());
            }
            else
            {
                SceneStack.Clear();
            }

            OnSceneChanged?.Invoke(sceneName);

            SceneStack.Push(sceneName);
        }

        private static void RebuildStack(string sceneName)
        {
            var tempStack = new Stack<string>();

            while (SceneStack.Count > 0)
            {
                var top = SceneStack.Pop();

                if (top != sceneName)
                {
                    tempStack.Push(top);
                }
            }

            while (tempStack.Count > 0)
            {
                SceneStack.Push(tempStack.Pop());
            }
        }

        public static void RootScene()
        {
            UnRootedSceneName = SceneManager.GetActiveScene().name;

            SceneStack.Push(UnRootedSceneName);

            LoadScene("PersistentLevel");

            UnRootedSceneName = null;
        }
    }

    public abstract class GameInstance<TGameState, TPlayerState, TGameEvents> : GameInstanceBase
    
    where TGameState : GS
    where TPlayerState : PS
    where TGameEvents : GE
    {
        [SerializeField]
        protected TGameState gameState;

        [SerializeField]
        protected TPlayerState playerState;

        [SerializeField]
        protected TGameEvents gameEvents;

        [SerializeField]
        protected TMP_Text version;

        protected override void Awake()
        {
            PersistentData = () => (gameState, playerState, gameEvents);

            if (version)
            {
                version.text = $"v{Application.version}";
            }

            base.Awake();
        }

        protected void SetEnvironment(string environment)
        {
            version.text += $"_{environment}";
        }

        public static TGameState GameState => (TGameState)PersistentData().gameState;

        public static TPlayerState PlayerState => (TPlayerState)PersistentData().playerState;

        public static TGameEvents GameEvents => (TGameEvents)PersistentData().gameEvents;

        private void OnDestroy()
        {
            playerState.Clear();

            gameEvents.Clear();

            gameState.Clear();
        }
    }
}
