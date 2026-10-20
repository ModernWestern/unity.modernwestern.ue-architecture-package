using System;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// A level being loaded by <see cref="Levels"/>. Wait for it the way you prefer:
    /// <code>
    /// Levels.Open(level).Completed += load => Debug.Log("Ready");   // callback
    /// yield return Levels.Open(level);                              // coroutine
    /// await Levels.Open(level);                                     // async method
    /// </code>
    /// </summary>
    public sealed class LevelLoad : CustomYieldInstruction
    {
        private Action<LevelLoad> _completed;

        private AsyncOperation _operation;

        private float _progress;

        internal LevelLoad(string scene)
        {
            Scene = scene;
        }

        /// <summary>
        /// Name or path of the scene being loaded.
        /// </summary>
        public string Scene { get; }

        public bool IsDone { get; private set; }

        /// <summary>
        /// False if the load could not start (e.g. the scene is not in Build Settings); the reason was logged.
        /// </summary>
        public bool Succeeded { get; private set; }

        /// <summary>
        /// From 0 to 1.
        /// </summary>
        public float Progress
        {
            get
            {
                if (IsDone)
                {
                    return 1f;
                }

                return _operation != null ? Mathf.Clamp01(_operation.progress / 0.9f) : _progress;
            }
        }

        public override bool keepWaiting => !IsDone;

        /// <summary>
        /// Called when the load finishes. Subscribing after it finished calls you right away.
        /// </summary>
        public event Action<LevelLoad> Completed
        {
            add
            {
                if (IsDone)
                {
                    value?.Invoke(this);
                }
                else
                {
                    _completed += value;
                }
            }
            remove => _completed -= value;
        }

        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        internal void Track_Internal(AsyncOperation operation)
        {
            _operation = operation;
        }

        internal void SetProgress_Internal(float progress)
        {
            _progress = progress;
        }

        internal void Complete_Internal(bool succeeded)
        {
            if (IsDone)
            {
                return;
            }

            _operation = null;

            Succeeded = succeeded;

            IsDone = true;

            var completed = _completed;

            _completed = null;

            try
            {
                completed?.Invoke(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly LevelLoad _load;

            internal Awaiter(LevelLoad load)
            {
                _load = load;
            }

            public bool IsCompleted => _load.IsDone;

            public bool GetResult()
            {
                return _load.Succeeded;
            }

            public void OnCompleted(Action continuation)
            {
                _load.Completed += _ => continuation();
            }
        }
    }
}
