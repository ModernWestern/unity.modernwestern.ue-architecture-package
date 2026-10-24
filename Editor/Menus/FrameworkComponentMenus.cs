using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The gear menu of GameMode and World Settings components: the same verbs where the user already is. On a copy of a
    /// prefab in a level, the GameMode verbs act on the prefab asset.
    /// </summary>
    internal static class FrameworkComponentMenus
    {
        private const string GameModeMenu = "CONTEXT/GameMode/";

        private const string WorldSettingsMenu = "CONTEXT/WorldSettings/";

        private const int Priority = 1500;

        [MenuItem(GameModeMenu + "Set as Project Default GameMode", false, Priority)]
        private static void SetAsDefault(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.SetProjectDefaultGameMode(AssetRoot_Internal(command)));
        }

        [MenuItem(GameModeMenu + "Set as Project Default GameMode", true)]
        private static bool SetAsDefaultValidate(MenuCommand command) => AssetRoot_Internal(command) != null;

        [MenuItem(GameModeMenu + "Use in Active Level", false, Priority + 1)]
        private static void UseInActiveLevel(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.UseGameModeInLevel(AssetRoot_Internal(command), SceneManager.GetActiveScene()));
        }

        [MenuItem(GameModeMenu + "Use in Active Level", true)]
        private static bool UseInActiveLevelValidate(MenuCommand command) => AssetRoot_Internal(command) != null && !EditorApplication.isPlaying;

        [MenuItem(GameModeMenu + "Create Variant for Active Level", false, Priority + 2)]
        private static void CreateVariantForActiveLevel(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.CreateGameModeVariantForLevel(AssetRoot_Internal(command), SceneManager.GetActiveScene()));
        }

        [MenuItem(GameModeMenu + "Create Variant for Active Level", true)]
        private static bool CreateVariantForActiveLevelValidate(MenuCommand command) => UseInActiveLevelValidate(command);

        [MenuItem(GameModeMenu + "Create Missing Default Classes", false, Priority + 3)]
        private static void CreateMissingDefaultClasses(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.CreateMissingDefaultClasses(command.context as GameMode));
        }

        [MenuItem(GameModeMenu + "Create Missing Default Classes", true)]
        private static bool CreateMissingDefaultClassesValidate(MenuCommand command) => command.context is GameMode && !EditorApplication.isPlaying;

        [MenuItem(WorldSettingsMenu + "Override for This Level", false, Priority)]
        private static void OverrideForLevel(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.OverrideGameModeForLevel(Level_Internal(command)));
        }

        [MenuItem(WorldSettingsMenu + "Override for This Level", true)]
        private static bool OverrideForLevelValidate(MenuCommand command) => Level_Internal(command).IsValid() && !EditorApplication.isPlaying;

        [MenuItem(WorldSettingsMenu + "Clear Override", false, Priority + 1)]
        private static void ClearOverride(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.ClearOverride(Level_Internal(command)));
        }

        [MenuItem(WorldSettingsMenu + "Clear Override", true)]
        private static bool ClearOverrideValidate(MenuCommand command) => command.context is WorldSettings worldSettings && worldSettings.GameModeOverride != null && Level_Internal(command).IsValid();

        [MenuItem(WorldSettingsMenu + "Select Effective GameMode", false, Priority + 2)]
        private static void SelectEffective(MenuCommand command)
        {
            var gameMode = GameModeInfo.Effective(Level_Internal(command), out GameModeSource _);

            if (gameMode == null)
            {
                Debug.Log("UE Architecture: this level has no GameMode.");

                return;
            }

            Selection.activeObject = gameMode.gameObject;

            EditorCompat.Ping(gameMode.gameObject);
        }

        [MenuItem(WorldSettingsMenu + "Select Effective GameMode", true)]
        private static bool SelectEffectiveValidate(MenuCommand command) => Level_Internal(command).IsValid();

        /// <summary>
        /// The root of the prefab asset a GameMode in the menu stands for (itself in the Project window, the asset of a
        /// copy in a level or in Prefab Mode), or null for a GameMode that only exists in a level.
        /// </summary>
        private static GameObject AssetRoot_Internal(MenuCommand command)
        {
            var asset = GameModeInfo.AssetOf(command.context as GameMode);

            return asset != null && asset.transform.parent == null ? asset.gameObject : null;
        }

        private static Scene Level_Internal(MenuCommand command)
        {
            var scene = command.context is WorldSettings worldSettings && !EditorUtility.IsPersistent(worldSettings) ? worldSettings.gameObject.scene : default;

            return scene.IsValid() && scene.isLoaded && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene) ? scene : default;
        }

        private static void Report_Internal(ActionResult result)
        {
            if (result.Ok)
            {
                LastCreatedFiles.Remember(result);

                Debug.Log("UE Architecture: " + result);
            }
            else
            {
                Debug.LogWarning("UE Architecture: " + string.Join("\n", result.Problems));
            }
        }
    }
}
