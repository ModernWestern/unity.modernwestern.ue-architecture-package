using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Pressing Play in a level without a GameMode says so once per level and Editor session: "Animations.unity has no
    /// GameMode: players get no input". Only a Console line: Play is never stopped. Off with Project Settings > UE
    /// Architecture > Editor > Play Without a GameMode = Ignore.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayStartCheck
    {
        private const string WarnedKey = "UEArchitecture.PlayStartCheck.";

        static PlayStartCheck()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged_Internal;

            EditorApplication.playModeStateChanged += OnPlayModeChanged_Internal;
        }

        private static void OnPlayModeChanged_Internal(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || UEArchitectureEditorSettings.instance.PlayStartCheck == PlayStartPolicy.Ignore)
            {
                return;
            }

            var settings = FrameworkIndex.Settings;

            // Started by hand (GameInstance.Launch): the project decides when there is a level.
            if (settings != null && !settings.StartAutomatically)
            {
                return;
            }

            var message = Message(SceneManager.GetActiveScene(), settings);

            if (message == null)
            {
                return;
            }

            var key = WarnedKey + SceneManager.GetActiveScene().path;

            if (SessionState.GetBool(key, false))
            {
                return;
            }

            SessionState.SetBool(key, true);

            Debug.LogWarning(message);
        }

        /// <summary>
        /// The warning for a level, or null when it has a GameMode.
        /// </summary>
        internal static string Message(Scene scene, UEArchitectureSettings settings)
        {
            if (!scene.IsValid() || GameModeInfo.Effective(scene, settings, out GameModeSource _) != null)
            {
                return null;
            }

            var name = string.IsNullOrEmpty(scene.path) ? "This level" : Path.GetFileName(scene.path);

            return $"UE Architecture: {name} has no GameMode: players get no input. Pick one in its World Settings (Scene view > Level overlay), " +
                   "or set a default GameMode in Project Settings > UE Architecture. To stop this message: Project Settings > UE Architecture > Editor > Play Without a GameMode.";
        }
    }
}
