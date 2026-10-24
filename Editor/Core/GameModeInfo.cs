using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where the GameMode of a level comes from, in the order the runtime picks it (GameMode.Spawn_Internal).
    /// </summary>
    internal enum GameModeSource
    {
        /// <summary>
        /// No GameMode: the level spawns no players.
        /// </summary>
        None,

        /// <summary>
        /// A GameMode placed in the scene: it wins over everything else.
        /// </summary>
        Placed,

        /// <summary>
        /// The GameMode Override of the level's World Settings.
        /// </summary>
        WorldSettings,

        /// <summary>
        /// The default GameMode of Project Settings > UE Architecture.
        /// </summary>
        ProjectDefault
    }

    /// <summary>
    /// What a GameMode creates and which GameMode a level uses: reads the GameState and PlayerState classes from
    /// GameMode&lt;TGameState, TPlayerState&gt; and mirrors the runtime's choice of GameMode.
    /// </summary>
    internal static class GameModeInfo
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> _levelKeys = new(StringComparer.Ordinal);

        private static int _levelKeysVersion = -1;

        public static Type GameStateType(Type gameModeType) => GenericArgument_Internal(gameModeType, 0) ?? typeof(GameState);

        public static Type PlayerStateType(Type gameModeType) => GenericArgument_Internal(gameModeType, 1) ?? typeof(PlayerState);

        /// <summary>
        /// The GameMode a level will use: one placed in the scene, else its World Settings override, else the project default.
        /// </summary>
        public static GameMode Effective(Scene scene, out string source)
        {
            var gameMode = Effective(scene, out GameModeSource kind);

            source = Describe(kind);

            return gameMode;
        }

        /// <summary>
        /// The GameMode a level will use, and where it comes from.
        /// </summary>
        public static GameMode Effective(Scene scene, out GameModeSource source)
        {
            return Effective(scene, FrameworkIndex.Settings, out source);
        }

        /// <summary>
        /// The GameMode a level will use with these project settings: the same order as GameMode.Spawn_Internal.
        /// </summary>
        public static GameMode Effective(Scene scene, UEArchitectureSettings settings, out GameModeSource source)
        {
            var placed = Find<GameMode>(scene);

            if (placed != null)
            {
                source = GameModeSource.Placed;

                return placed;
            }

            var worldSettings = Find<WorldSettings>(scene);

            if (worldSettings != null && worldSettings.GameModeOverride != null)
            {
                source = GameModeSource.WorldSettings;

                return worldSettings.GameModeOverride;
            }

            if (settings != null && settings.DefaultGameMode != null)
            {
                source = GameModeSource.ProjectDefault;

                return settings.DefaultGameMode;
            }

            source = GameModeSource.None;

            return null;
        }

        /// <summary>
        /// "placed in the scene", "World Settings override", "project default" or "none".
        /// </summary>
        public static string Describe(GameModeSource source)
        {
            switch (source)
            {
                case GameModeSource.Placed:
                    return "placed in the scene";

                case GameModeSource.WorldSettings:
                    return "World Settings override";

                case GameModeSource.ProjectDefault:
                    return "project default";

                default:
                    return "none";
            }
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

        /// <summary>
        /// The prefab asset a GameMode stands for: itself when it is in a prefab asset, the asset of the copy open in Prefab
        /// Mode, the source of a prefab instance, or null for a GameMode that only exists in a scene.
        /// </summary>
        public static GameMode AssetOf(GameMode gameMode)
        {
            if (gameMode == null)
            {
                return null;
            }

            if (EditorUtility.IsPersistent(gameMode))
            {
                return gameMode;
            }

            if (PrefabStageUtility.GetPrefabStage(gameMode.gameObject) != null)
            {
                var persistent = Scaffold.Persistent(gameMode) as GameMode;

                return persistent != null && EditorUtility.IsPersistent(persistent) ? persistent : null;
            }

            return PrefabUtility.IsPartOfPrefabInstance(gameMode) ? PrefabUtility.GetCorrespondingObjectFromSource(gameMode) : null;
        }

        /// <summary>
        /// True when the GameMode is a prefab in a level's override folder (<paramref name="folder"/>, or the folder of
        /// the editor settings, e.g. Assets/Prefabs/Modes/Boss). Only the folder is checked: see
        /// <see cref="IsLevelOverride(GameMode, Scene)"/> for "this level's own GameMode".
        /// </summary>
        public static bool IsLevelOverride(GameMode gameMode, string sceneName, string folder = null)
        {
            if (gameMode == null || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(gameMode);

            var levelFolder = string.IsNullOrEmpty(folder) ? GeneratorNaming.OverrideFolder(sceneName, UEArchitectureEditorSettings.Current) : GeneratorNaming.CleanFolder(folder);

            return !string.IsNullOrEmpty(path) && GeneratorNaming.FolderOf(path) == levelFolder;
        }

        /// <summary>
        /// True when the GameMode is this level's own: a prefab in one of the level's override folders (see
        /// <see cref="GeneratorNaming.LevelFolders"/>) that no other level uses. Two levels with the same file name in
        /// different folders never share an "own" GameMode this way.
        /// </summary>
        public static bool IsLevelOverride(GameMode gameMode, Scene scene)
        {
            if (gameMode == null || !scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(gameMode);

            if (string.IsNullOrEmpty(path) || Array.IndexOf(GeneratorNaming.LevelFolders(scene.path, UEArchitectureEditorSettings.Current), GeneratorNaming.FolderOf(path)) < 0)
            {
                return false;
            }

            return !UsedByAnotherLevel(gameMode.transform.root.gameObject, scene.path);
        }

        /// <summary>
        /// The name of a level's override folder ("Boss" for Assets/Prefabs/Modes/Boss): the name of
        /// <see cref="OwnFolder"/>, so "Gameplay (Snow)" when another level called Gameplay has its GameMode in
        /// Modes/Gameplay. Null for a level that was never saved. Cached until assets change.
        /// </summary>
        public static string LevelKey(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                return null;
            }

            if (_levelKeysVersion != FrameworkIndex.Version)
            {
                _levelKeys.Clear();

                _levelKeysVersion = FrameworkIndex.Version;
            }

            if (_levelKeys.TryGetValue(scene.path, out var known))
            {
                return known;
            }

            var folder = OwnFolder(scene.path);

            var key = (folder != null ? GeneratorNaming.SceneOfOverrideFolder(folder, UEArchitectureEditorSettings.Current) : null) ??
                      GeneratorNaming.DisambiguatedLevelName(scene.path);

            _levelKeys[scene.path] = key;

            return key;
        }

        /// <summary>
        /// The override folder a level's own copies go to (see <see cref="GeneratorNaming.LevelFolders"/>): the one that
        /// holds its own GameMode already, else the first that holds no GameMode of another level. Null when another level
        /// has its GameMode in every one. "Override for this level", the class fields' "Variant for this level" and the
        /// picker's "(this level)" all use it, so two levels with the same file name never share a folder.
        /// </summary>
        public static string OwnFolder(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return null;
            }

            string free = null;

            foreach (var folder in GeneratorNaming.LevelFolders(scenePath, UEArchitectureEditorSettings.Current))
            {
                if (FolderTakenByAnotherLevel(folder, scenePath))
                {
                    continue;
                }

                if (GameModesIn_Internal(folder).Any(prefab => FrameworkIndex.LevelsReferencing(prefab).Contains(scenePath)))
                {
                    return folder;
                }

                free ??= folder;
            }

            return free;
        }

        /// <summary>
        /// True when a GameMode prefab in <paramref name="folder"/> is picked by a level other than
        /// <paramref name="scenePath"/>: the folder holds that level's own copies, so this level's go elsewhere.
        /// </summary>
        public static bool FolderTakenByAnotherLevel(string folder, string scenePath)
        {
            return GameModesIn_Internal(folder).Any(prefab => UsedByAnotherLevel(prefab, scenePath));
        }

        /// <summary>
        /// True when a level other than <paramref name="scenePath"/> picks this GameMode prefab (World Settings or a placed
        /// copy), in its saved file or, when it is open with unsaved changes, as it is now.
        /// </summary>
        public static bool UsedByAnotherLevel(GameObject gameModePrefab, string scenePath)
        {
            return gameModePrefab != null && FrameworkIndex.LevelsReferencing(gameModePrefab).Any(other => !string.Equals(other, scenePath, StringComparison.Ordinal));
        }

        /// <summary>
        /// A Pawn placed in the level that the GameMode points to from one of its own fields (a GameMode that possesses a
        /// placed Pawn from code, like Sitizens' playerCharacter): the field's label and the Pawn, or false.
        /// </summary>
        public static bool TryPlacedPawn(SerializedObject serialized, out string fieldLabel, out Pawn pawn)
        {
            fieldLabel = null;

            pawn = null;

            if (serialized == null || serialized.targetObject == null || EditorUtility.IsPersistent(serialized.targetObject))
            {
                return false;
            }

            var iterator = serialized.GetIterator();

            var enter = true;

            while (iterator.NextVisible(enter))
            {
                enter = iterator.propertyType == SerializedPropertyType.Generic && iterator.hasVisibleChildren;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                if (iterator.objectReferenceValue is Pawn found && !EditorUtility.IsPersistent(found))
                {
                    fieldLabel = iterator.propertyPath.Contains(".Array.data[") ? iterator.propertyPath.Replace(".Array.data[", "[") : iterator.name;

                    pawn = found;

                    return true;
                }
            }

            return false;
        }

        private static System.Collections.Generic.IEnumerable<GameObject> GameModesIn_Internal(string folder)
        {
            return FrameworkIndex.PrefabsWith(typeof(GameMode)).Where(prefab => GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(prefab)) == folder);
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
}
