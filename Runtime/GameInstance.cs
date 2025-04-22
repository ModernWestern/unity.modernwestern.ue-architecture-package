namespace UEArchitecture
{
    using TMPro;
    using System;
    using UnityEngine;
    using System.Collections;
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

        private static Action<IEnumerator> _coroutineRunner;

        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(UnRootedSceneName))
            {
                LoadScene(SelectedScene, false);
            }

            _coroutineRunner += ie => StartCoroutine(ie);

            Exists = true;

            Construct();
        }

        public static void LoadScene(string sceneName, bool unloadPreviousScene = true)
        {
            if (unloadPreviousScene && SceneStack.Count > 0)
            {
                var operation = SceneManager.UnloadSceneAsync(SceneStack.Pop());

                _coroutineRunner(OperationProcess(operation, Handler));
            }
            else
            {
                SceneStack.Clear();
            }

            return;

            void Handler()
            {
                SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

                OnSceneChanged?.Invoke(sceneName);

                SceneStack.Push(sceneName);
            }
        }

        private static IEnumerator OperationProcess(AsyncOperation operation, Action onComplete)
        {
            while (!operation.isDone)
            {
                yield return null;
            }

            onComplete?.Invoke();
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
