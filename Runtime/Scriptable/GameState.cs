using UnityEngine;

namespace UEArchitecture
{
    // Shared game-wide data (like score, timers, objectives)
    public abstract class GameState : ScriptableObject, IPersistent
    {
        public abstract void Clear();
    }
}