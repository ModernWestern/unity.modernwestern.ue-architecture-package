using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Project window > right-click > UE Architecture: on framework scripts, "Create Prefab" (several at once) and
    /// "Create Prefab and Use as" (the Player Controller, Default Pawn or HUD of the selected or default GameMode, the
    /// project's Game Instance or Loading Screen), like Unreal's "Create Blueprint class based on X"; on GameMode prefabs,
    /// "Set as Project Default GameMode", "Use in Active Level", "Create Variant for Active Level" and "Create Missing
    /// Default Classes". The menus only check what is selected; the actions say what they could not do.
    /// </summary>
    internal static class FrameworkAssetMenus
    {
        private const string Root = "Assets/UE Architecture/";

        private const string UseAs = Root + "Create Prefab and Use as/";

        private const int Priority = 1500;

        [MenuItem(Root + "Create Prefab", false, Priority)]
        private static void CreatePrefabs()
        {
            var created = new List<Object>();

            var merged = new ActionResult("create-prefab");

            foreach (var type in FrameworkScripts_Internal(Selection.objects))
            {
                var result = UEArchitectureActions.CreateClassPrefab(type);

                merged.Merge(result);

                if (result.Result != null)
                {
                    created.Add(result.Result);
                }
            }

            Report_Internal(merged);

            if (created.Count > 0)
            {
                Selection.objects = created.ToArray();
            }
        }

        [MenuItem(Root + "Create Prefab", true)]
        private static bool CreatePrefabsValidate()
        {
            var selected = Selection.objects;

            return selected.Length > 0 && FrameworkScripts_Internal(selected).Count == selected.Length;
        }

        [MenuItem(UseAs + "Player Controller", false, Priority + 1)]
        private static void UseAsPlayerController() => UseInGameMode_Internal("playerController");

        [MenuItem(UseAs + "Player Controller", true)]
        private static bool UseAsPlayerControllerValidate() => CanUseInGameMode_Internal(typeof(PlayerController));

        [MenuItem(UseAs + "Default Pawn", false, Priority + 2)]
        private static void UseAsDefaultPawn() => UseInGameMode_Internal("defaultPawn");

        [MenuItem(UseAs + "Default Pawn", true)]
        private static bool UseAsDefaultPawnValidate() => CanUseInGameMode_Internal(typeof(Pawn));

        [MenuItem(UseAs + "HUD", false, Priority + 3)]
        private static void UseAsHud() => UseInGameMode_Internal("hud");

        [MenuItem(UseAs + "HUD", true)]
        private static bool UseAsHudValidate() => CanUseInGameMode_Internal(typeof(HUD));

        [MenuItem(UseAs + "Project Game Instance", false, Priority + 14)]
        private static void UseAsGameInstance() => UseInSettings_Internal("gameInstance");

        [MenuItem(UseAs + "Project Game Instance", true)]
        private static bool UseAsGameInstanceValidate() => SingleScript_Internal(typeof(GameInstance)) != null;

        [MenuItem(UseAs + "Loading Screen", false, Priority + 15)]
        private static void UseAsLoadingScreen() => UseInSettings_Internal("loadingScreen");

        [MenuItem(UseAs + "Loading Screen", true)]
        private static bool UseAsLoadingScreenValidate() => SingleScript_Internal(typeof(LoadingScreen)) != null;

        [MenuItem(Root + "Set as Project Default GameMode", false, Priority + 20)]
        private static void SetAsDefault()
        {
            Report_Internal(UEArchitectureActions.SetProjectDefaultGameMode(GameModePrefabs_Internal().First()));
        }

        [MenuItem(Root + "Set as Project Default GameMode", true)]
        private static bool SetAsDefaultValidate() => Selection.objects.Length == 1 && GameModePrefabs_Internal().Count == 1;

        [MenuItem(Root + "Use in Active Level", false, Priority + 21)]
        private static void UseInActiveLevel()
        {
            Report_Internal(UEArchitectureActions.UseGameModeInLevel(GameModePrefabs_Internal().First(), SceneManager.GetActiveScene()));
        }

        [MenuItem(Root + "Use in Active Level", true)]
        private static bool UseInActiveLevelValidate() => Selection.objects.Length == 1 && GameModePrefabs_Internal().Count == 1 && !EditorApplication.isPlaying;

        [MenuItem(Root + "Create Variant for Active Level", false, Priority + 22)]
        private static void CreateVariantForActiveLevel()
        {
            var scene = SceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(scene.path) && !Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene))
            {
                return;
            }

            Report_Internal(UEArchitectureActions.CreateGameModeVariantForLevel(GameModePrefabs_Internal().First(), scene));
        }

        [MenuItem(Root + "Create Variant for Active Level", true)]
        private static bool CreateVariantForActiveLevelValidate() => UseInActiveLevelValidate();

        [MenuItem(Root + "Create Missing Default Classes", false, Priority + 23)]
        private static void CreateMissingDefaultClasses()
        {
            var merged = new ActionResult("create-missing-default-classes");

            foreach (var prefab in GameModePrefabs_Internal())
            {
                merged.Merge(UEArchitectureActions.CreateMissingDefaultClasses(prefab));
            }

            Report_Internal(merged);
        }

        [MenuItem(Root + "Create Missing Default Classes", true)]
        private static bool CreateMissingDefaultClassesValidate() => Selection.objects.Length > 0 && GameModePrefabs_Internal().Count == Selection.objects.Length;

        /// <summary>
        /// The framework classes of the selected scripts (compiled, not abstract), one per script.
        /// </summary>
        internal static List<Type> FrameworkScripts_Internal(IEnumerable<Object> selected)
        {
            var types = new List<Type>();

            foreach (var item in selected ?? Array.Empty<Object>())
            {
                var type = item is MonoScript script ? script.GetClass() : null;

                if (type != null && !type.IsAbstract && !type.ContainsGenericParameters && FrameworkIndex.IndexedTypes.Any(indexed => indexed.IsAssignableFrom(type)))
                {
                    types.Add(type);
                }
            }

            return types;
        }

        /// <summary>
        /// The GameMode prefabs of the selection.
        /// </summary>
        internal static List<GameObject> GameModePrefabs_Internal()
        {
            return Selection.objects.OfType<GameObject>().Where(prefab => EditorUtility.IsPersistent(prefab) && prefab.GetComponent<GameMode>() != null).ToList();
        }

        /// <summary>
        /// The GameMode "Create Prefab and Use as" fills: a GameMode prefab selected with the script, else the project
        /// default.
        /// </summary>
        internal static GameMode TargetGameMode_Internal()
        {
            var selected = GameModePrefabs_Internal().FirstOrDefault();

            if (selected != null)
            {
                return selected.GetComponent<GameMode>();
            }

            var settings = FrameworkIndex.Settings;

            return settings != null ? settings.DefaultGameMode : null;
        }

        private static Type SingleScript_Internal(Type baseType)
        {
            var scripts = Selection.objects.OfType<MonoScript>().ToArray();

            if (scripts.Length != 1)
            {
                return null;
            }

            var type = FrameworkScripts_Internal(scripts).FirstOrDefault();

            return type != null && baseType.IsAssignableFrom(type) ? type : null;
        }

        private static bool CanUseInGameMode_Internal(Type baseType)
        {
            return SingleScript_Internal(baseType) != null && TargetGameMode_Internal() != null;
        }

        private static void UseInGameMode_Internal(string propertyPath)
        {
            var gameMode = TargetGameMode_Internal();

            var type = SingleScript_Internal(Scaffold.FieldType(typeof(GameMode), propertyPath));

            if (gameMode == null || type == null)
            {
                Debug.LogWarning("UE Architecture: select one script of the right class, with a GameMode prefab or a project default GameMode.");

                return;
            }

            // Menu labels cannot name the GameMode they change: when it is not in the selection (the project default, shared
            // by every level without one), say which one before changing it.
            if (!Application.isBatchMode && GameModePrefabs_Internal().Count == 0 &&
                !EditorUtility.DisplayDialog("UE Architecture", ConfirmDefaultText(gameMode, propertyPath, type), "Change it", "Cancel"))
            {
                return;
            }

            Report_Internal(UEArchitectureActions.AssignClass(gameMode, propertyPath, type));
        }

        /// <summary>
        /// "Use ArenaPawn as the Default Pawn of ArenaGameMode, the project's default GameMode (used by 3 levels)?" for
        /// "Create Prefab and Use as" without a GameMode prefab in the selection.
        /// </summary>
        internal static string ConfirmDefaultText(GameMode gameMode, string propertyPath, Type type)
        {
            var prefab = gameMode.transform.root.gameObject;

            var levels = FrameworkIndex.LevelsUsing(prefab);

            var usage = levels == 1 ? "used by 1 level" : $"used by {levels} levels";

            return $"Use {type.Name} as the {ObjectNames.NicifyVariableName(propertyPath)} of {prefab.name}, the project's default GameMode ({usage})?\n\n" +
                   "To change another GameMode instead, select its prefab together with the script.";
        }

        [MenuItem(Root + "Remove Files Made by the Last Action…", false, Priority + 40)]
        private static void RemoveLastCreated()
        {
            var paths = LastCreatedFiles.Paths.Where(path => AssetDatabase.LoadMainAssetAtPath(path) != null || AssetDatabase.IsValidFolder(path)).ToArray();

            if (paths.Length == 0)
            {
                Debug.Log("UE Architecture: the last action made no file that is still there.");

                LastCreatedFiles.Forget();

                return;
            }

            var shown = string.Join("\n", paths.Take(15)) + (paths.Length > 15 ? $"\n... and {paths.Length - 15} more" : string.Empty);

            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Remove generated files",
                                             $"Move what \"{LastCreatedFiles.Label}\" made to the trash? Undo cannot remove files; the system's trash can bring them back.\n\n{shown}",
                                             "Move to Trash", "Cancel"))
            {
                return;
            }

            var result = UEArchitectureActions.RemoveCreatedFiles(paths);

            LastCreatedFiles.Forget();

            Report_Internal(result);
        }

        [MenuItem(Root + "Remove Files Made by the Last Action…", true)]
        private static bool RemoveLastCreatedValidate() => LastCreatedFiles.Paths.Length > 0;

        private static void UseInSettings_Internal(string propertyPath)
        {
            var type = SingleScript_Internal(Scaffold.FieldType(typeof(UEArchitectureSettings), propertyPath));

            if (type == null)
            {
                Debug.LogWarning("UE Architecture: select one script of the right class.");

                return;
            }

            Report_Internal(UEArchitectureActions.AssignProjectClass(propertyPath, type));
        }

        private static void Report_Internal(ActionResult result)
        {
            if (result == null)
            {
                return;
            }

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
