using System;
using UnityEngine;

namespace UEArchitecture
{
    public abstract class GameEvents : ScriptableObject, IPersistent
    {
        public event Action OnGameStart;
        public event Action OnGamePaused;
        public event Action OnGameResumed;
        public event Action OnGameOver;
        public event Action OnGameRestart;

        public abstract void Clear();
    }
}