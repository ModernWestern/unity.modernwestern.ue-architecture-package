using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Per-level settings, like Unreal's World Settings: add one to a scene to choose its GameMode (GameMode Override).
    /// Without it the level uses the default GameMode from Project Settings > UE Architecture. A GameMode placed directly
    /// in the scene wins over both.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UE Architecture/World Settings")]
    public sealed class WorldSettings : MonoBehaviour
    {
        [SerializeField, Tooltip("GameMode prefab for this level. Empty = the default GameMode from Project Settings.")]
        private GameMode gameModeOverride;

        public GameMode GameModeOverride => gameModeOverride;

        internal static T FindInScene_Internal<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
