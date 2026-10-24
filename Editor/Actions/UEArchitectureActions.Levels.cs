using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        private static readonly (string Path, Type Type)[] _defaultClassSlots =
        {
            ("playerController", typeof(PlayerController)),
            ("defaultPawn", typeof(Pawn)),
            ("hud", typeof(HUD))
        };

        /// <summary>
        /// Unreal's "GameMode Override" for one level, in one call: makes (or reuses) a prefab variant of the GameMode the
        /// level uses now, in the level's own folder (Project Settings > UE Architecture > Editor, by default
        /// Assets/Prefabs/Modes/&lt;Level&gt;/&lt;Class&gt;.prefab, or <paramref name="folder"/>), adds World Settings to
        /// the level when it has none and puts the variant in its GameMode Override. With no GameMode at all it makes a
        /// plain GameMode prefab instead. The level is opened (and saved and closed again) when it is not open. Running it
        /// again changes nothing.
        /// </summary>
        public static ActionResult OverrideGameModeForLevel(string scenePath, string folder = null)
        {
            using (var level = LevelScope.Open(scenePath, "override-gamemode"))
            {
                return level.Ok ? OverrideGameModeForLevel(level.Scene, folder) : level.Failure;
            }
        }

        /// <summary>
        /// <see cref="OverrideGameModeForLevel(string, string)"/> for an open level. A level that was never saved needs
        /// <paramref name="folder"/>.
        /// </summary>
        public static ActionResult OverrideGameModeForLevel(Scene scene, string folder = null)
        {
            return VariantForLevel_Internal(scene, null, folder, "override-gamemode");
        }

        /// <summary>
        /// Makes (or reuses) a variant of <paramref name="gameModePrefab"/> in the level's own folder and uses it in that
        /// level's World Settings ("Create Variant for Active Level").
        /// </summary>
        public static ActionResult CreateGameModeVariantForLevel(GameObject gameModePrefab, string scenePath, string folder = null)
        {
            using (var level = LevelScope.Open(scenePath, "create-gamemode-variant"))
            {
                return level.Ok ? VariantForLevel_Internal(level.Scene, gameModePrefab, folder, "create-gamemode-variant") : level.Failure;
            }
        }

        internal static ActionResult CreateGameModeVariantForLevel(GameObject gameModePrefab, Scene scene, string folder = null)
        {
            return VariantForLevel_Internal(scene, gameModePrefab, folder, "create-gamemode-variant");
        }

        /// <summary>
        /// Puts a GameMode prefab in a level's World Settings (added when missing), like picking it as Unreal's GameMode
        /// Override. A GameMode placed in the level would still win, so that case is reported instead.
        /// </summary>
        public static ActionResult UseGameModeInLevel(GameObject gameModePrefab, string scenePath)
        {
            using (var level = LevelScope.Open(scenePath, "use-gamemode"))
            {
                return level.Ok ? UseGameModeInLevel(gameModePrefab, level.Scene) : level.Failure;
            }
        }

        /// <summary>
        /// <see cref="UseGameModeInLevel(GameObject, string)"/> for an open level.
        /// </summary>
        public static ActionResult UseGameModeInLevel(GameObject gameModePrefab, Scene scene)
        {
            using (var op = Scaffold.Begin($"Use {(gameModePrefab != null ? gameModePrefab.name : "GameMode")} in {LevelName_Internal(scene)}", "use-gamemode"))
            {
                var gameMode = GameModePrefab_Internal(op, gameModePrefab);

                if (gameMode == null || !CheckLevel_Internal(op, scene) || !CheckNothingPlaced_Internal(op, scene))
                {
                    return op.Result;
                }

                var worldSettings = EnsureWorldSettings_Internal(op, scene);

                if (worldSettings == null)
                {
                    return op.Result;
                }

                op.Assign(worldSettings, "gameModeOverride", gameMode);

                op.Result.Result = gameMode;

                op.Result.Message = op.Result.Changed.Count > 0 ? $"{LevelName_Internal(scene)} now uses {gameModePrefab.name}." : $"{LevelName_Internal(scene)} already uses {gameModePrefab.name}.";

                return op.Result;
            }
        }

        /// <summary>
        /// Empties a level's GameMode Override, so it uses the project default again. World Settings stays.
        /// </summary>
        public static ActionResult ClearOverride(string scenePath)
        {
            using (var level = LevelScope.Open(scenePath, "clear-override"))
            {
                return level.Ok ? ClearOverride(level.Scene) : level.Failure;
            }
        }

        /// <summary>
        /// <see cref="ClearOverride(string)"/> for an open level.
        /// </summary>
        public static ActionResult ClearOverride(Scene scene)
        {
            using (var op = Scaffold.Begin($"Clear the GameMode Override of {LevelName_Internal(scene)}", "clear-override"))
            {
                if (!CheckLevel_Internal(op, scene))
                {
                    return op.Result;
                }

                var worldSettings = GameModeInfo.Find<WorldSettings>(scene);

                if (worldSettings == null || worldSettings.GameModeOverride == null)
                {
                    op.Result.Message = $"{LevelName_Internal(scene)} has no GameMode Override: it already uses the project default.";

                    return op.Result;
                }

                op.Assign(worldSettings, "gameModeOverride", null);

                op.Result.Message = $"{LevelName_Internal(scene)} uses the project default GameMode again. The override prefab stays in the project.";

                return op.Result;
            }
        }

        /// <summary>
        /// Makes a GameMode prefab the project default (Project Settings > UE Architecture, Unreal's Default GameMode). The
        /// settings asset (Assets/Resources/UEArchitectureSettings.asset) is created when the project has none.
        /// </summary>
        public static ActionResult SetProjectDefaultGameMode(GameObject gameModePrefab)
        {
            using (var op = Scaffold.Begin($"Make {(gameModePrefab != null ? gameModePrefab.name : "GameMode")} the project default", "set-default-gamemode"))
            {
                var gameMode = GameModePrefab_Internal(op, gameModePrefab);

                if (gameMode == null)
                {
                    return op.Result;
                }

                var settings = EnsureSettings_Internal(op);

                if (settings == null)
                {
                    return op.Result;
                }

                op.Assign(settings, "defaultGameMode", gameMode);

                op.Result.Result = gameMode;

                op.Result.Message = op.Result.Changed.Count > 0 ? $"{gameModePrefab.name} is now the project's default GameMode." : $"{gameModePrefab.name} already is the project's default GameMode.";

                return op.Result;
            }
        }

        /// <summary>
        /// Fills the empty default classes of a GameMode (Player Controller, Default Pawn, HUD) with prefabs of the base
        /// classes, made when missing (in <paramref name="folder"/>, or where the editor settings say). Slots that are set
        /// stay as they are, so a second run changes nothing.
        /// </summary>
        public static ActionResult CreateMissingDefaultClasses(GameMode gameMode, string folder = null)
        {
            using (var op = Scaffold.Begin($"Create the missing default classes of {(gameMode != null ? gameMode.name : "GameMode")}", "create-missing-default-classes"))
            {
                if (gameMode == null)
                {
                    op.Problem("Pick a GameMode.");

                    return op.Result;
                }

                var serialized = new SerializedObject(Scaffold.Writable(gameMode));

                var filled = 0;

                foreach (var (path, type) in _defaultClassSlots)
                {
                    var property = serialized.FindProperty(path);

                    if (property == null || property.objectReferenceValue != null)
                    {
                        continue;
                    }

                    var prefabPath = string.IsNullOrEmpty(folder) ? null : $"{GeneratorNaming.CleanFolder(folder)}/{GeneratorNaming.PrefabName(type, UEArchitectureEditorSettings.Current)}.prefab";

                    var prefab = CreateClassPrefab_Internal(op, type, prefabPath);

                    if (prefab != null && op.Assign(gameMode, path, prefab))
                    {
                        filled++;
                    }
                }

                op.Result.Result = gameMode;

                op.Result.Message = filled == 0 ? $"Every default class of {gameMode.name} is already set." : $"Filled {filled} default class(es) of {gameMode.name}.";

                return op.Result;
            }
        }

        /// <summary>
        /// <see cref="CreateMissingDefaultClasses(GameMode, string)"/> for a GameMode prefab.
        /// </summary>
        public static ActionResult CreateMissingDefaultClasses(GameObject gameModePrefab, string folder = null)
        {
            var gameMode = gameModePrefab != null ? gameModePrefab.GetComponent<GameMode>() : null;

            return gameMode != null || gameModePrefab == null
                       ? CreateMissingDefaultClasses(gameMode, folder)
                       : ActionResult.Fail("create-missing-default-classes", $"{gameModePrefab.name} has no GameMode on its root.");
        }

        private static ActionResult VariantForLevel_Internal(Scene scene, GameObject basePrefab, string folder, string verb)
        {
            using (var op = Scaffold.Begin($"Override the GameMode of {LevelName_Internal(scene)}", verb))
            {
                VariantForLevel_Internal(op, scene, basePrefab, folder);

                return op.Result;
            }
        }

        internal static GameMode VariantForLevel_Internal(ScaffoldOp op, Scene scene, GameObject basePrefab, string folder)
        {
            if (!CheckLevel_Internal(op, scene) || !CheckNothingPlaced_Internal(op, scene))
            {
                return null;
            }

            var sceneName = string.IsNullOrEmpty(scene.path) ? string.Empty : Path.GetFileNameWithoutExtension(scene.path);

            if (sceneName.Length == 0 && string.IsNullOrEmpty(folder))
            {
                op.Problem("Save the level first: its own GameMode goes to a folder named after it (Project Settings > UE Architecture > Editor).");

                return null;
            }

            var conventions = UEArchitectureEditorSettings.Current;

            var explicitFolder = !string.IsNullOrEmpty(folder);

            var effective = GameModeInfo.Effective(scene, out GameModeSource source);

            var ownAlready = source == GameModeSource.WorldSettings &&
                             (explicitFolder ? GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(effective)) == GeneratorNaming.CleanFolder(folder) : GameModeInfo.IsLevelOverride(effective, scene));

            GameMode result;

            if (basePrefab == null && ownAlready)
            {
                op.Result.AddNote($"{LevelName_Internal(scene)} already has its own GameMode: {AssetDatabase.GetAssetPath(effective)}.");

                result = effective;
            }
            else
            {
                var baseMode = basePrefab != null ? GameModePrefab_Internal(op, basePrefab) : effective;

                if (basePrefab != null && baseMode == null)
                {
                    return null;
                }

                var fileName = GeneratorNaming.PrefabName(baseMode != null ? baseMode.GetType() : typeof(GameMode), conventions) + ".prefab";

                var path = explicitFolder ? $"{GeneratorNaming.CleanFolder(folder)}/{fileName}" : OwnPath_Internal(op, scene, fileName, conventions);

                if (path == null)
                {
                    return null;
                }

                if (baseMode != null)
                {
                    if (path == AssetDatabase.GetAssetPath(baseMode))
                    {
                        result = baseMode;
                    }
                    else
                    {
                        var variant = op.CreateVariant(baseMode.transform.root.gameObject, path);

                        result = variant != null ? variant.GetComponentInChildren<GameMode>(true) : null;
                    }
                }
                else
                {
                    var prefab = op.FindOrCreatePrefab(path, nameof(GameMode), new[] { typeof(GameMode) });

                    result = prefab != null ? prefab.GetComponent<GameMode>() : null;
                }

                if (result == null)
                {
                    return null;
                }
            }

            var worldSettings = EnsureWorldSettings_Internal(op, scene);

            if (worldSettings == null || !op.Assign(worldSettings, "gameModeOverride", result))
            {
                return null;
            }

            op.Result.Result = result;

            op.PingWhenDone(result.gameObject);

            var file = AssetDatabase.GetAssetPath(result);

            op.Result.Message = op.Result.Created.Contains(file)
                                    ? $"{LevelName_Internal(scene)} now has its own GameMode: {file}" + (effective != null && effective != result ? $", a variant of {effective.name}." : ".")
                                    : op.Result.Changed.Count > 0 ? $"{LevelName_Internal(scene)} uses its own GameMode {file} again." : $"{LevelName_Internal(scene)} already uses its own GameMode {file}.";

            return result;
        }

        /// <summary>
        /// Where a level's own copy called <paramref name="fileName"/> goes: its override folder
        /// (Assets/Prefabs/Modes/Boss), unless a GameMode there belongs to another level with the same file name (whatever
        /// its class); then the folder named after both ("Assets/Prefabs/Modes/Boss (Arena)"). The same folder as
        /// <see cref="GameModeInfo.LevelKey"/>. Null, with a problem, when both are taken.
        /// </summary>
        internal static string OwnPath_Internal(ScaffoldOp op, Scene scene, string fileName, GeneratorConventions conventions)
        {
            var folders = GeneratorNaming.LevelFolders(scene.path, conventions);

            var levelFolder = GameModeInfo.OwnFolder(scene.path);

            if (levelFolder == null)
            {
                op.Problem($"{string.Join(" and ", folders)} already hold the GameMode of other levels called {Path.GetFileNameWithoutExtension(scene.path)}. Rename this level, or pick a folder (folder=Assets/...).");

                return null;
            }

            if (levelFolder != folders[0])
            {
                op.Result.AddNote($"Another level is also called {Path.GetFileNameWithoutExtension(scene.path)} and uses {folders[0]}: this level's own copies go to {levelFolder}.");
            }

            return $"{levelFolder}/{fileName}";
        }

        /// <summary>
        /// The level's World Settings, added (as a root object called "World Settings") when it has none.
        /// </summary>
        internal static WorldSettings EnsureWorldSettings_Internal(ScaffoldOp op, Scene scene)
        {
            var existing = GameModeInfo.Find<WorldSettings>(scene);

            if (existing != null)
            {
                return existing;
            }

            var created = op.CreateSceneObject(scene, "World Settings", typeof(WorldSettings));

            return created != null ? created.GetComponent<WorldSettings>() : null;
        }

        /// <summary>
        /// The project's settings asset, created at Assets/Resources/UEArchitectureSettings.asset when there is none.
        /// </summary>
        internal static UEArchitectureSettings EnsureSettings_Internal(ScaffoldOp op)
        {
            var settings = FrameworkIndex.Settings;

            if (settings != null)
            {
                return settings;
            }

            settings = UEArchitectureSettingsProvider.CreateAsset();

            if (settings == null)
            {
                op.Problem($"Could not create {UEArchitectureSettingsProvider.DefaultAssetPath}.");

                return null;
            }

            op.Result.AddCreated(AssetDatabase.GetAssetPath(settings));

            return settings;
        }

        private static GameMode GameModePrefab_Internal(ScaffoldOp op, GameObject prefab)
        {
            if (prefab == null)
            {
                op.Problem("Pick a GameMode prefab.");

                return null;
            }

            var gameMode = prefab.GetComponent<GameMode>();

            if (gameMode == null)
            {
                op.Problem($"{prefab.name} has no GameMode on its root.");

                return null;
            }

            if (!EditorUtility.IsPersistent(prefab))
            {
                op.Problem($"{prefab.name} is an object of a level, not a prefab: save it as a prefab first.");

                return null;
            }

            return gameMode;
        }

        private static bool CheckLevel_Internal(ScaffoldOp op, Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                op.Problem("Leave Play mode first: levels are set up in Edit mode.");

                return false;
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                op.Problem("Open the level first.");

                return false;
            }

            if (EditorSceneManager.IsPreviewScene(scene))
            {
                op.Problem("This is a prefab, not a level: open a level to change its GameMode.");

                return false;
            }

            return true;
        }

        private static bool CheckNothingPlaced_Internal(ScaffoldOp op, Scene scene)
        {
            var placed = GameModeInfo.Find<GameMode>(scene);

            if (placed == null)
            {
                return true;
            }

            op.Problem($"{LevelName_Internal(scene)} has a GameMode placed in it ({ActionResult.Describe(placed)}), which wins over World Settings. Change that one, or remove it to use an override.");

            return false;
        }

        private static string LevelName_Internal(Scene scene)
        {
            if (!scene.IsValid())
            {
                return "the level";
            }

            return string.IsNullOrEmpty(scene.path) ? string.IsNullOrEmpty(scene.name) ? "the unsaved level" : scene.name : Path.GetFileNameWithoutExtension(scene.path);
        }
    }

    /// <summary>
    /// A level an action works on, by path: the open one, or opened additively for the action and then saved and closed.
    /// </summary>
    internal sealed class LevelScope : IDisposable
    {
        private bool _opened;

        private LevelScope()
        {
        }

        public Scene Scene { get; private set; }

        public ActionResult Failure { get; private set; }

        public bool Ok => Failure == null;

        public static LevelScope Open(string scenePath, string verb)
        {
            var scope = new LevelScope();

            var clean = (scenePath ?? string.Empty).Trim().Replace('\\', '/');

            if (clean.Length == 0)
            {
                scope.Failure = ActionResult.Fail(verb, "Name the level, e.g. Assets/Scenes/Boss.unity.");

                return scope;
            }

            var scene = SceneManager.GetSceneByPath(clean);

            if (scene.IsValid() && scene.isLoaded)
            {
                scope.Scene = scene;

                return scope;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(clean) == null)
            {
                scope.Failure = ActionResult.Fail(verb, $"There is no scene at {clean}.");

                return scope;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                scope.Failure = ActionResult.Fail(verb, "Leave Play mode first: levels are set up in Edit mode.");

                return scope;
            }

            scope.Scene = EditorSceneManager.OpenScene(clean, OpenSceneMode.Additive);

            scope._opened = true;

            return scope;
        }

        /// <summary>
        /// Saves and closes the level when this scope opened it.
        /// </summary>
        public void Dispose()
        {
            if (!_opened)
            {
                return;
            }

            _opened = false;

            if (!Scene.IsValid() || !Scene.isLoaded)
            {
                return;
            }

            if (Scene.isDirty)
            {
                EditorSceneManager.SaveScene(Scene);
            }

            if (SceneManager.sceneCount > 1)
            {
                EditorSceneManager.CloseScene(Scene, true);
            }
        }
    }
}
