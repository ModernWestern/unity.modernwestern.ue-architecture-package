using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// A new playable level at <paramref name="path"/> (Assets > Create > UE Architecture > Level): World Settings
        /// (with <paramref name="gameModePrefab"/>, or empty for the project default), one Player Start per player, a
        /// camera aimed at the first one, a light, an EventSystem, an AudioListener and a ground plane, saved and added to
        /// Build Settings. The level is made next to the open ones and closed again; open it to work in it. When the file
        /// exists it is set up instead (only what is missing is added), so a second run changes nothing.
        /// </summary>
        public static ActionResult CreateLevel(string path, GameObject gameModePrefab = null, bool addToBuild = true)
        {
            const string verb = "create-level";

            var clean = GeneratorNaming.CleanFolder(path);

            if (!clean.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return ActionResult.Fail(verb, $"\"{path}\" is not a level path: it must end in .unity (e.g. Assets/Scenes/Station 2.unity).");
            }

            var folderProblem = GeneratorNaming.FolderProblem(GeneratorNaming.FolderOf(clean));

            if (folderProblem.Length > 0)
            {
                return ActionResult.Fail(verb, folderProblem);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ActionResult.Fail(verb, "Leave Play mode first: levels are made in Edit mode.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(clean) != null)
            {
                var existing = SetUpLevel(clean, new LevelSetupOptions { GameMode = gameModePrefab });

                existing.Verb = verb;

                if (addToBuild && existing.Ok)
                {
                    existing.Merge(AddSceneToBuild(clean));
                }

                existing.AddNote($"{clean} already exists: only what it lacked was added.");

                return existing;
            }

            if (File.Exists(clean))
            {
                return ActionResult.Fail(verb, $"{clean} exists on disk but Unity has not imported it. Refresh the project or pick another name.");
            }

            using (var op = Scaffold.Begin($"Create level {Path.GetFileNameWithoutExtension(clean)}", verb))
            {
                if (!op.EnsureFolder(GeneratorNaming.FolderOf(clean)))
                {
                    return op.Result;
                }

                var scene = NewLevelScene_Internal(op, clean);

                if (!scene.IsValid())
                {
                    return op.Result;
                }

                var added = new List<string>();

                var fine = new List<string>();

                try
                {
                    LevelSetup.SetUp(op, scene, new LevelSetupOptions { GameMode = gameModePrefab, Ground = true }, added, fine, false);

                    if (!EditorSceneManager.SaveScene(scene, clean))
                    {
                        op.Problem($"Unity could not save {clean}. See the Console for the reason.");

                        return op.Result;
                    }
                }
                finally
                {
                    if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }

                // The objects were made in the new file: name the file, not each object.
                var objects = op.Result.Created.Where(item => item.Contains("#")).ToList();

                op.Result.AddCreated(clean);

                if (addToBuild)
                {
                    op.Result.Merge(AddSceneToBuild(clean));
                }

                op.Result.Result = AssetDatabase.LoadAssetAtPath<SceneAsset>(clean);

                op.Result.Message = $"Created {clean} with {string.Join(", ", added)}{(addToBuild ? ", in Build Settings" : string.Empty)}.";

                op.Result.AddNote($"{objects.Count} object(s) in the new level.");

                return op.Result;
            }
        }

        /// <summary>
        /// "Set Up This Level": adds what a level needs to be played and lacks (World Settings, one Player Start per
        /// player, a camera, an AudioListener, a light, an EventSystem) and says what was already fine. One Undo step; a
        /// second run adds nothing. The level is opened (then saved and closed) when it is not open.
        /// </summary>
        public static ActionResult SetUpLevel(string scenePath, LevelSetupOptions options = null)
        {
            using (var level = LevelScope.Open(scenePath, "set-up-level"))
            {
                return level.Ok ? SetUpLevel(level.Scene, options) : level.Failure;
            }
        }

        /// <summary>
        /// <see cref="SetUpLevel(string, LevelSetupOptions)"/> for an open level.
        /// </summary>
        public static ActionResult SetUpLevel(Scene scene, LevelSetupOptions options = null)
        {
            using (var op = Scaffold.Begin($"Set up {LevelName_Internal(scene)}", "set-up-level"))
            {
                if (!CheckLevel_Internal(op, scene))
                {
                    return op.Result;
                }

                var added = new List<string>();

                var fine = new List<string>();

                LevelSetup.SetUp(op, scene, options, added, fine, true);

                op.Result.Message = $"{LevelName_Internal(scene)}: {LevelSetup.Report(added, fine)}";

                return op.Result;
            }
        }

        /// <summary>
        /// Adds Player Starts until the level has <paramref name="count"/> (0: one per player at start of its GameMode),
        /// next to each other, on the ground. A second run adds nothing.
        /// </summary>
        public static ActionResult AddPlayerStarts(string scenePath, int count = 0)
        {
            using (var level = LevelScope.Open(scenePath, "add-player-starts"))
            {
                return level.Ok ? AddPlayerStarts(level.Scene, count) : level.Failure;
            }
        }

        /// <summary>
        /// <see cref="AddPlayerStarts(string, int)"/> for an open level.
        /// </summary>
        public static ActionResult AddPlayerStarts(Scene scene, int count = 0)
        {
            using (var op = Scaffold.Begin($"Add Player Starts to {LevelName_Internal(scene)}", "add-player-starts"))
            {
                if (!CheckLevel_Internal(op, scene))
                {
                    return op.Result;
                }

                var added = LevelSetup.AddPlayerStarts(op, scene, count, true);

                var total = LevelSetup.Starts_Internal(scene).Count;

                op.Result.Message = added == 0 ? $"{LevelName_Internal(scene)} already has {total} Player Start(s)." : $"Added {added} Player Start(s) to {LevelName_Internal(scene)}.";

                return op.Result;
            }
        }

        /// <summary>
        /// Adds an EventSystem (with the input module that fits the project) to a level that has none, so its UI buttons
        /// respond. Needs the Unity UI package.
        /// </summary>
        public static ActionResult AddEventSystem(Scene scene)
        {
            using (var op = Scaffold.Begin($"Add EventSystem to {LevelName_Internal(scene)}", "add-event-system"))
            {
                if (!CheckLevel_Internal(op, scene))
                {
                    return op.Result;
                }

                if (UiTypes.EventSystem != null && LevelSetup.HasAny_Internal(scene, UiTypes.EventSystem))
                {
                    op.Result.Message = $"{LevelName_Internal(scene)} already has an EventSystem.";

                    return op.Result;
                }

                var created = LevelSetup.AddEventSystem(op, scene, true);

                op.Result.Result = created;

                op.Result.Message = created != null ? $"Added an EventSystem to {LevelName_Internal(scene)}." : op.Result.Message;

                return op.Result;
            }
        }

        /// <summary>
        /// Adds an AudioListener to a level that has none: on its first camera, or on a new object.
        /// </summary>
        public static ActionResult AddAudioListener(Scene scene)
        {
            using (var op = Scaffold.Begin($"Add AudioListener to {LevelName_Internal(scene)}", "add-audio-listener"))
            {
                if (!CheckLevel_Internal(op, scene))
                {
                    return op.Result;
                }

                if (LevelSetup.FindInScene<AudioListener>(scene) != null)
                {
                    op.Result.Message = $"{LevelName_Internal(scene)} already has an AudioListener.";

                    return op.Result;
                }

                var listener = LevelSetup.AddAudioListener(op, scene, true);

                op.Result.Result = listener;

                op.Result.Message = listener != null ? $"Added an AudioListener to {ActionResult.Describe(listener.gameObject)}." : op.Result.Message;

                return op.Result;
            }
        }

        /// <summary>
        /// Adds a level to Build Settings (enabled) when it is not there, so Levels.Open can load it in a build. Build
        /// Settings are not part of Undo.
        /// </summary>
        public static ActionResult AddSceneToBuild(string scenePath)
        {
            var result = new ActionResult("add-to-build");

            var clean = GeneratorNaming.CleanFolder(scenePath);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(clean) == null)
            {
                result.AddProblem($"There is no scene at {clean}.");

                return result;
            }

            var scenes = EditorBuildSettings.scenes.ToList();

            var index = scenes.FindIndex(item => string.Equals(item.path, clean, StringComparison.Ordinal));

            if (index >= 0 && scenes[index].enabled)
            {
                result.Message = $"{clean} is already in Build Settings.";

                return result;
            }

            if (index >= 0)
            {
                scenes[index] = new EditorBuildSettingsScene(clean, true);
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(clean, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();

            ProblemProviders.Invalidate();

            result.AddChanged("Build Settings: " + clean);

            result.Message = $"Added {clean} to Build Settings.";

            return result;
        }

        /// <summary>
        /// Puts a Player Start on the ground below it, or with <paramref name="toNavMesh"/> on the closest point of the
        /// NavMesh within 5 m. Undo-able.
        /// </summary>
        public static ActionResult SnapPlayerStart(PlayerStart start, bool toNavMesh = false)
        {
            using (var op = Scaffold.Begin(toNavMesh ? "Snap Player Start to the NavMesh" : "Snap Player Start to the ground", "snap-player-start"))
            {
                if (start == null)
                {
                    op.Problem("Pick a Player Start.");

                    return op.Result;
                }

                if (!op.CheckEditable(start))
                {
                    return op.Result;
                }

                var position = start.transform.position;

                Vector3 target;

                if (toNavMesh ? !LevelSetup.TryNavMesh(position, 5f, out target) : !LevelSetup.TryGroundBelow(position, out target))
                {
                    op.Problem(toNavMesh ? $"No NavMesh within 5 m of {start.name}." : $"No collider below {start.name}.");

                    return op.Result;
                }

                if ((target - position).sqrMagnitude < 0.000001f)
                {
                    op.Result.Message = $"{start.name} is already there.";

                    return op.Result;
                }

                Undo.RecordObject(start.transform, op.Name);

                start.transform.position = target;

                op.Touch(start);

                op.Result.AddChanged(ActionResult.Describe(start) + ": position");

                op.Result.Message = $"Moved {start.name} by {Vector3.Distance(position, target):0.##} m.";

                return op.Result;
            }
        }

        /// <summary>
        /// A new, empty level saved at <paramref name="path"/> and open next to the others. Unity cannot add an untitled
        /// scene next to another untitled one, so then an empty copy is saved and opened instead.
        /// </summary>
        private static Scene NewLevelScene_Internal(ScaffoldOp op, string path)
        {
            Scene untitled = default;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);

                if (string.IsNullOrEmpty(loaded.path))
                {
                    untitled = loaded;

                    break;
                }
            }

            Scene scene;

            if (!untitled.IsValid())
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                if (!EditorSceneManager.SaveScene(scene, path))
                {
                    EditorSceneManager.CloseScene(scene, true);

                    op.Problem($"Unity could not save {path}.");

                    return default;
                }

                return scene;
            }

            if (!EditorSceneManager.SaveScene(untitled, path, true))
            {
                op.Problem($"Unity could not save {path}.");

                return default;
            }

            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            foreach (var root in scene.GetRootGameObjects())
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            return scene;
        }
    }
}
