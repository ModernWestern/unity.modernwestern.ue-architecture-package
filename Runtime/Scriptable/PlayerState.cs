using UnityEngine;

namespace UEArchitecture
{
    // Player-specific replicated data (like kills, team, health)
    public abstract class PlayerState : ScriptableObject, IPersistent
    {
        public abstract void Clear();

        public abstract void ForceBroadcast();
    }
}