using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Migration menus: a MonoBehaviour's gear > UE Architecture > Convert to GameMode… (and the other framework classes),
    /// a GameMode's gear > Change Game State and Player State Classes…, a framework component's gear > Change Class…, and
    /// the Project window's right-click > UE Architecture > Find Usages in Scenes and Prefabs. Every script edit opens a
    /// preview first and asks before writing.
    /// </summary>
    internal static class ConvertMenus
    {
        private const string Convert = "CONTEXT/MonoBehaviour/UE Architecture/Convert to ";

        private const int Priority = 1600;

        [MenuItem(Convert + "GameMode…", false, Priority)]
        private static void ToGameMode(MenuCommand command) => Open_Internal(command, typeof(GameMode));

        [MenuItem(Convert + "GameMode…", true)]
        private static bool ToGameModeValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "Player Controller…", false, Priority + 1)]
        private static void ToPlayerController(MenuCommand command) => Open_Internal(command, typeof(PlayerController));

        [MenuItem(Convert + "Player Controller…", true)]
        private static bool ToPlayerControllerValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "AI Controller…", false, Priority + 2)]
        private static void ToAIController(MenuCommand command) => Open_Internal(command, typeof(AIController));

        [MenuItem(Convert + "AI Controller…", true)]
        private static bool ToAIControllerValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "Pawn…", false, Priority + 3)]
        private static void ToPawn(MenuCommand command) => Open_Internal(command, typeof(Pawn));

        [MenuItem(Convert + "Pawn…", true)]
        private static bool ToPawnValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "HUD…", false, Priority + 4)]
        private static void ToHud(MenuCommand command) => Open_Internal(command, typeof(HUD));

        [MenuItem(Convert + "HUD…", true)]
        private static bool ToHudValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "Player Start…", false, Priority + 5)]
        private static void ToPlayerStart(MenuCommand command) => Open_Internal(command, typeof(PlayerStart));

        [MenuItem(Convert + "Player Start…", true)]
        private static bool ToPlayerStartValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "Loading Screen…", false, Priority + 6)]
        private static void ToLoadingScreen(MenuCommand command) => Open_Internal(command, typeof(LoadingScreen));

        [MenuItem(Convert + "Loading Screen…", true)]
        private static bool ToLoadingScreenValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem(Convert + "Game Instance…", false, Priority + 7)]
        private static void ToGameInstance(MenuCommand command) => Open_Internal(command, typeof(GameInstance));

        [MenuItem(Convert + "Game Instance…", true)]
        private static bool ToGameInstanceValidate(MenuCommand command) => CanConvert_Internal(command);

        [MenuItem("CONTEXT/GameMode/Change Game State and Player State Classes…", false, 1510)]
        private static void ChangeStates(MenuCommand command)
        {
            var script = command.context is MonoBehaviour behaviour ? MonoScript.FromMonoBehaviour(behaviour) : null;

            if (script != null)
            {
                ConvertClassWindow.OpenStates(script);
            }
        }

        [MenuItem("CONTEXT/GameMode/Change Game State and Player State Classes…", true)]
        private static bool ChangeStatesValidate(MenuCommand command)
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && command.context is GameMode gameMode && gameMode.GetType() != typeof(GameMode) &&
                   ClassConversion.IsEditable(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(gameMode)), out _);
        }

        [MenuItem("CONTEXT/GameMode/Change Class…", false, 1520)]
        private static void ChangeGameModeClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/GameMode/Change Class…", true)]
        private static bool ChangeGameModeClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("CONTEXT/Controller/Change Class…", false, 1520)]
        private static void ChangeControllerClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/Controller/Change Class…", true)]
        private static bool ChangeControllerClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("CONTEXT/Pawn/Change Class…", false, 1520)]
        private static void ChangePawnClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/Pawn/Change Class…", true)]
        private static bool ChangePawnClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("CONTEXT/HUD/Change Class…", false, 1520)]
        private static void ChangeHudClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/HUD/Change Class…", true)]
        private static bool ChangeHudClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("CONTEXT/PlayerStart/Change Class…", false, 1520)]
        private static void ChangePlayerStartClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/PlayerStart/Change Class…", true)]
        private static bool ChangePlayerStartClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("CONTEXT/LoadingScreen/Change Class…", false, 1520)]
        private static void ChangeLoadingScreenClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/LoadingScreen/Change Class…", true)]
        private static bool ChangeLoadingScreenClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("CONTEXT/GameInstance/Change Class…", false, 1520)]
        private static void ChangeGameInstanceClass(MenuCommand command) => ChangeClass_Internal(command);

        [MenuItem("CONTEXT/GameInstance/Change Class…", true)]
        private static bool ChangeGameInstanceClassValidate(MenuCommand command) => CanChangeClass_Internal(command);

        [MenuItem("Assets/UE Architecture/Find Usages in Scenes and Prefabs", false, 1545)]
        private static void FindUsages()
        {
            if (!(Selection.activeObject is MonoScript script))
            {
                return;
            }

            var result = UEArchitectureActions.FindUsages(script);

            ConvertClassWindow.Report(result);

            var found = result.Notes.Select(line => line.Split(new[] { " (through " }, StringSplitOptions.None)[0])
                              .Select(AssetDatabase.LoadMainAssetAtPath).Where(asset => asset != null).ToArray();

            if (found.Length > 0)
            {
                Selection.objects = found;

                EditorCompat.Ping(found[0]);
            }
        }

        [MenuItem("Assets/UE Architecture/Find Usages in Scenes and Prefabs", true)]
        private static bool FindUsagesValidate() => Selection.activeObject is MonoScript script && script.GetClass() != null;

        /// <summary>
        /// The framework class whose subclasses a component can switch to (Pawn for a TestPawn), or null.
        /// </summary>
        internal static Type FrameworkRoot_Internal(Type type)
        {
            return ClassConversion.Bases.FirstOrDefault(root => root.IsAssignableFrom(type));
        }

        /// <summary>
        /// The classes a component can become with Change Class: its framework class and every class derived from it that
        /// can be added to an object (not its own class, not test classes).
        /// </summary>
        internal static List<Type> ChangeClassCandidates_Internal(Type type)
        {
            var root = FrameworkRoot_Internal(type);

            if (root == null)
            {
                return new List<Type>();
            }

            return TypeCache.GetTypesDerivedFrom(root).Append(root)
                            .Where(candidate => candidate != type && !candidate.IsAbstract && !candidate.ContainsGenericParameters &&
                                                !UEArchitectureSettingsProvider.IsTestType_Internal(candidate))
                            .Distinct()
                            .OrderBy(candidate => candidate == root ? 0 : 1).ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                            .ToList();
        }

        private static void Open_Internal(MenuCommand command, Type target)
        {
            var script = command.context is MonoBehaviour behaviour ? MonoScript.FromMonoBehaviour(behaviour) : null;

            if (script != null)
            {
                ConvertClassWindow.OpenConvert(script, target);
            }
        }

        private static bool CanConvert_Internal(MenuCommand command)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !(command.context is MonoBehaviour behaviour) || behaviour.GetType().BaseType != typeof(MonoBehaviour))
            {
                return false;
            }

            var script = MonoScript.FromMonoBehaviour(behaviour);

            return script != null && script.GetClass() == behaviour.GetType() && ClassConversion.IsEditable(AssetDatabase.GetAssetPath(script), out _);
        }

        private static bool CanChangeClass_Internal(MenuCommand command)
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && command.context is MonoBehaviour behaviour &&
                   PrefabUtility.GetCorrespondingObjectFromSource(behaviour) == null && ChangeClassCandidates_Internal(behaviour.GetType()).Count > 0;
        }

        private static void ChangeClass_Internal(MenuCommand command)
        {
            if (!(command.context is Component component))
            {
                return;
            }

            var candidates = ChangeClassCandidates_Internal(component.GetType());

            var menu = new GenericMenu();

            foreach (var candidate in candidates)
            {
                var picked = candidate;

                menu.AddItem(new GUIContent(UEArchitectureStyles.TypeName(candidate, candidates)), false, () => Confirm_Internal(component, picked));
            }

            menu.ShowAsContext();
        }

        private static void Confirm_Internal(Component component, Type type)
        {
            if (component == null)
            {
                return;
            }

            var preview = UEArchitectureActions.ChangeComponentClass(component, type);

            if (!preview.Ok)
            {
                ConvertClassWindow.Report(preview);

                return;
            }

            var lost = preview.Notes.Count > 0 ? "\n\n" + string.Join("\n", preview.Notes) : "\n\nEvery field is kept.";

            if (!EditorUtility.DisplayDialog("Change Class", $"{component.gameObject.name}: {component.GetType().Name} becomes {type.Name}.{lost}\n\n{UEArchitectureActions.UndoNote_Internal(preview.Notes.Count > 0)}",
                                             "Change", "Cancel"))
            {
                return;
            }

            ConvertClassWindow.Report(UEArchitectureActions.ChangeComponentClass(component, type, false));

            ProblemProviders.Invalidate();
        }
    }
}
