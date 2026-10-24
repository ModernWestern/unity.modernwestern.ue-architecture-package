using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// GameObject > UE Architecture (and the Hierarchy's right-click menu): World Settings and Player Start, placed in the
    /// right place: under the clicked object, in the scene it belongs to, in the prefab open in Prefab Mode, and a Player
    /// Start at the Scene view's pivot.
    /// </summary>
    internal static class FrameworkMenus
    {
        [MenuItem("GameObject/UE Architecture/World Settings", false, 10)]
        private static void CreateWorldSettings(MenuCommand command)
        {
            if (!IsFirstContext_Internal(command))
            {
                return;
            }

            var parent = command.context as GameObject;

            var scene = TargetScene_Internal(parent);

            var existing = GameModeInfo.Find<WorldSettings>(scene);

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;

                EditorCompat.Ping(existing.gameObject);

                return;
            }

            Create_Internal<WorldSettings>("World Settings", parent, scene, false);
        }

        [MenuItem("GameObject/UE Architecture/Player Start", false, 11)]
        private static void CreatePlayerStartMenu(MenuCommand command)
        {
            if (!IsFirstContext_Internal(command))
            {
                return;
            }

            var parent = command.context as GameObject;

            Create_Internal<PlayerStart>("Player Start", parent, TargetScene_Internal(parent), true);
        }

        /// <summary>
        /// Adds a Player Start to a level, at the Scene view's pivot (Undo-able).
        /// </summary>
        public static PlayerStart CreatePlayerStart(Scene scene)
        {
            return Create_Internal<PlayerStart>("Player Start", null, scene, true);
        }

        /// <summary>
        /// A new object with <typeparamref name="T"/> (default presets applied), under <paramref name="parent"/> when
        /// given, else in <paramref name="scene"/>, else in the current stage (the prefab open in Prefab Mode).
        /// </summary>
        internal static T Create_Internal<T>(string name, GameObject parent, Scene scene, bool atPivot) where T : Component
        {
            var gameObject = ObjectFactory.CreateGameObject(name, typeof(T));

            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(gameObject, parent);
            }
            else if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
            {
                if (gameObject.scene != scene)
                {
                    SceneManager.MoveGameObjectToScene(gameObject, scene);
                }
            }
            else
            {
                StageUtility.PlaceGameObjectInCurrentStage(gameObject);
            }

            if (parent == null && atPivot && SceneView.lastActiveSceneView != null)
            {
                gameObject.transform.position = SceneView.lastActiveSceneView.pivot;
            }

            GameObjectUtility.EnsureUniqueNameForSibling(gameObject);

            if (!Application.isBatchMode)
            {
                Selection.activeGameObject = gameObject;
            }

            return gameObject.GetComponent<T>();
        }

        /// <summary>
        /// The scene a new object goes to: the clicked object's, else the prefab open in Prefab Mode, else the active scene.
        /// </summary>
        private static Scene TargetScene_Internal(GameObject parent)
        {
            if (parent != null)
            {
                return parent.scene;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            return stage != null ? stage.scene : SceneManager.GetActiveScene();
        }

        /// <summary>
        /// With several objects selected, Unity calls a GameObject menu once per object: act only once.
        /// </summary>
        private static bool IsFirstContext_Internal(MenuCommand command)
        {
            return command.context == null || Selection.gameObjects.Length <= 1 || command.context == Selection.activeGameObject;
        }
    }
}
