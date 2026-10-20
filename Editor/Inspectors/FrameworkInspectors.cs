using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What a GameMode creates: reads the GameState and PlayerState classes from GameMode&lt;TGameState, TPlayerState&gt;.
    /// </summary>
    internal static class GameModeInfo
    {
        public static Type GameStateType(Type gameModeType) => GenericArgument_Internal(gameModeType, 0) ?? typeof(GameState);

        public static Type PlayerStateType(Type gameModeType) => GenericArgument_Internal(gameModeType, 1) ?? typeof(PlayerState);

        /// <summary>
        /// The GameMode a level will use: one placed in the scene, else its World Settings override, else the project default.
        /// </summary>
        public static GameMode Effective(Scene scene, out string source)
        {
            var placed = Find<GameMode>(scene);

            if (placed != null)
            {
                source = "placed in the scene";

                return placed;
            }

            var worldSettings = Find<WorldSettings>(scene);

            if (worldSettings != null && worldSettings.GameModeOverride != null)
            {
                source = "World Settings override";

                return worldSettings.GameModeOverride;
            }

            var settings = UEArchitectureSettingsProvider.FindAll().FirstOrDefault();

            if (settings != null && settings.DefaultGameMode != null)
            {
                source = "project default";

                return settings.DefaultGameMode;
            }

            source = "none";

            return null;
        }

        public static T Find<T>(Scene scene) where T : Component
        {
            return scene.IsValid() && scene.isLoaded
                       ? scene.GetRootGameObjects().Select(root => root.GetComponentInChildren<T>(true)).FirstOrDefault(found => found != null)
                       : null;
        }

        public static int Count<T>(Scene scene) where T : Component
        {
            return scene.IsValid() && scene.isLoaded ? scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<T>(true).Length) : 0;
        }

        public static string Describe(Object prefab, Type baseType, string empty)
        {
            if (prefab == null)
            {
                return empty;
            }

            var type = prefab.GetType();

            return type == baseType ? prefab.name : $"{prefab.name}  ({type.Name})";
        }

        private static Type GenericArgument_Internal(Type type, int index)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(GameMode<,>))
                {
                    return current.GetGenericArguments()[index];
                }
            }

            return null;
        }
    }

    [CustomEditor(typeof(GameMode), true)]
    internal sealed class GameModeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var gameMode = (GameMode)target;

            UEArchitectureStyles.Section("When a level begins, it creates");

            UEArchitectureStyles.Row("GameState", GameModeInfo.GameStateType(gameMode.GetType()).Name);

            UEArchitectureStyles.Row("PlayerState (each player)", GameModeInfo.PlayerStateType(gameMode.GetType()).Name);

            UEArchitectureStyles.Row("PlayerController", GameModeInfo.Describe(gameMode.PlayerControllerPrefab, typeof(PlayerController), "PlayerController (default)"), gameMode.PlayerControllerPrefab == null);

            UEArchitectureStyles.Row("Pawn", GameModeInfo.Describe(gameMode.DefaultPawnPrefab, typeof(Pawn), "none: players start without a Pawn"), gameMode.DefaultPawnPrefab == null);

            UEArchitectureStyles.Row("HUD", GameModeInfo.Describe(gameMode.HUDPrefab, typeof(HUD), "none"), gameMode.HUDPrefab == null);

            UEArchitectureStyles.Row("Players at start", gameMode.PlayersAtStart.ToString());

            if (Application.isPlaying && gameMode.HasStartedPlay)
            {
                UEArchitectureStyles.Section("Playing");

                foreach (var player in World.Players)
                {
                    UEArchitectureStyles.Row(player.PlayerState?.PlayerName ?? player.name, player.Pawn != null ? "controls " + player.Pawn.name : "no Pawn", player.Pawn == null);
                }
            }
        }
    }

    [CustomEditor(typeof(WorldSettings))]
    internal sealed class WorldSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("gameModeOverride"), new GUIContent("GameMode Override"));

            serializedObject.ApplyModifiedProperties();

            var scene = ((WorldSettings)target).gameObject.scene;

            if (!scene.IsValid())
            {
                return;
            }

            UEArchitectureStyles.Section("This level");

            var gameMode = GameModeInfo.Effective(scene, out var source);

            UEArchitectureStyles.Row("GameMode", gameMode != null ? $"{gameMode.name}  ({source})" : "none: no players are spawned", gameMode == null);

            if (gameMode == null)
            {
                if (UEArchitectureStyles.Notice("Pick a GameMode prefab above, or set a default one for every level.", MessageType.None, "Open Project Settings"))
                {
                    SettingsService.OpenProjectSettings(UEArchitectureSettingsProvider.SettingsPath);
                }

                return;
            }

            UEArchitectureStyles.Row("GameState / PlayerState", $"{GameModeInfo.GameStateType(gameMode.GetType()).Name} / {GameModeInfo.PlayerStateType(gameMode.GetType()).Name}");

            UEArchitectureStyles.Row("Pawn", GameModeInfo.Describe(gameMode.DefaultPawnPrefab, typeof(Pawn), "none"), gameMode.DefaultPawnPrefab == null);

            var starts = GameModeInfo.Count<PlayerStart>(scene);

            UEArchitectureStyles.Row("Player Starts", starts.ToString(), starts == 0);

            if (gameMode.DefaultPawnPrefab != null && starts == 0 &&
                UEArchitectureStyles.Notice("No PlayerStart in this level: players will spawn where the GameMode is.", MessageType.Warning, "Add PlayerStart"))
            {
                FrameworkMenus.CreatePlayerStart(scene);
            }

            if (GameModeInfo.Count<WorldSettings>(scene) > 1)
            {
                EditorGUILayout.HelpBox("This level has more than one World Settings; only the first one is used.", MessageType.Warning);
            }
        }
    }

    /// <summary>
    /// GameObject > UE Architecture: World Settings and Player Start.
    /// </summary>
    internal static class FrameworkMenus
    {
        [MenuItem("GameObject/UE Architecture/World Settings", false, 10)]
        private static void CreateWorldSettings(MenuCommand command)
        {
            var existing = GameModeInfo.Find<WorldSettings>(SceneManager.GetActiveScene());

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;

                return;
            }

            Create_Internal<WorldSettings>("World Settings", command.context as GameObject, SceneManager.GetActiveScene());
        }

        [MenuItem("GameObject/UE Architecture/Player Start", false, 11)]
        private static void CreatePlayerStartMenu(MenuCommand command)
        {
            Create_Internal<PlayerStart>("Player Start", command.context as GameObject, SceneManager.GetActiveScene());
        }

        public static PlayerStart CreatePlayerStart(Scene scene)
        {
            return Create_Internal<PlayerStart>("Player Start", null, scene);
        }

        private static T Create_Internal<T>(string name, GameObject parent, Scene scene) where T : Component
        {
            var gameObject = new GameObject(name);

            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(gameObject, parent);
            }
            else if (scene.IsValid() && gameObject.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(gameObject, scene);
            }

            var component = gameObject.AddComponent<T>();

            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);

            Selection.activeGameObject = gameObject;

            return component;
        }
    }
}
