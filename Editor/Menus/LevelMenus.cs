using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Assets > Create > UE Architecture > Level (a playable level, named in the Project window) and GameObject > UE
    /// Architecture > Set Up This Level and Player Starts for Every Player.
    /// </summary>
    internal static class LevelMenus
    {
        [MenuItem("Assets/Create/UE Architecture/Level", false, 78)]
        private static void CreateLevel()
        {
            var action = ScriptableObject.CreateInstance<CreateLevelAction>();

            var icon = EditorGUIUtility.IconContent("SceneAsset Icon").image as Texture2D;

            EditorCompat.StartNameEditing(action, "New Level.unity", icon);
        }

        [MenuItem("GameObject/UE Architecture/Set Up This Level", false, 12)]
        private static void SetUpThisLevel(MenuCommand command)
        {
            if (!IsFirstContext_Internal(command))
            {
                return;
            }

            Report_Internal(UEArchitectureActions.SetUpLevel(TargetLevel_Internal(command)));
        }

        [MenuItem("GameObject/UE Architecture/Set Up This Level", true)]
        private static bool SetUpThisLevelValidate() => !EditorApplication.isPlaying && PrefabStageUtility.GetCurrentPrefabStage() == null;

        [MenuItem("GameObject/UE Architecture/Player Starts for Every Player", false, 13)]
        private static void PlayerStartsForEveryPlayer(MenuCommand command)
        {
            if (!IsFirstContext_Internal(command))
            {
                return;
            }

            Report_Internal(UEArchitectureActions.AddPlayerStarts(TargetLevel_Internal(command)));
        }

        [MenuItem("GameObject/UE Architecture/Player Starts for Every Player", true)]
        private static bool PlayerStartsForEveryPlayerValidate() => SetUpThisLevelValidate();

        /// <summary>
        /// Makes the level at the typed path, then opens it (after asking to save the open levels' changes).
        /// </summary>
        internal static void CreateAndOpen_Internal(string path)
        {
            var result = UEArchitectureActions.CreateLevel(path);

            Report_Internal(result);

            if (!result.Ok || Application.isBatchMode)
            {
                return;
            }

            // Called from the Project window's rename field, inside its GUI: the save dialog and the scene switch wait for
            // the next Editor update.
            var scenePath = GeneratorNaming.CleanFolder(path);

            EditorApplication.delayCall += () => Open_Internal(scenePath);
        }

        private static void Open_Internal(string scenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private static Scene TargetLevel_Internal(MenuCommand command)
        {
            return command.context is GameObject clicked && clicked.scene.IsValid() ? clicked.scene : SceneManager.GetActiveScene();
        }

        private static bool IsFirstContext_Internal(MenuCommand command)
        {
            return command.context == null || Selection.gameObjects.Length <= 1 || command.context == Selection.activeGameObject;
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

            ProblemProviders.Invalidate();
        }

        private sealed class CreateLevelAction : NameOnCreateAction
        {
            protected override void OnNamed(string pathName, string resourceFile)
            {
                CreateAndOpen_Internal(pathName);
            }
        }
    }
}
