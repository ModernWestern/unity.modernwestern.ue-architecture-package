using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Project Settings > UE Architecture: the project's GameInstance, default GameMode and loading screen, like Unreal's
    /// Maps &amp; Modes. The settings live in an asset inside a Resources folder so players can read them.
    /// </summary>
    internal static class UEArchitectureSettingsProvider
    {
        private static UEArchitectureSettings _transient;

        private static readonly GameplayTagManager _tags = new();

        private static readonly string[] Keywords =
        {
            "GameInstance", "Game Instance", "GameMode", "Default GameMode", "Player Controller", "Pawn", "HUD", "Subsystem", "Loading Screen", "Gameplay Tags", "Tags",
            "Tag constants", "Save", "Unreal", "Maps & Modes", "Levels", "Build Settings", "Scenes", "Tick"
        };

        public const string SettingsPath = "Project/UE Architecture";

        public const string DefaultAssetPath = "Assets/Resources/" + UEArchitectureSettings.ResourcesName + ".asset";

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "UE Architecture",
                guiHandler = Draw_Internal,
                hasSearchInterestHandler = HasSearchInterest_Internal,
                keywords = Keywords
            };
        }

        /// <summary>
        /// The page answers a search for its words, and for the name of a tag, a subsystem or a level it lists. Runs on
        /// every keystroke of the Project Settings search, so it only compares names: levels by path (the project's and
        /// Build Settings'), without reading them.
        /// </summary>
        private static bool HasSearchInterest_Internal(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return UEArchitectureStyles.Matches(search, Keywords) || UEArchitectureStyles.Matches(search, "UE Architecture") || FrameworkIndex.Tags.Any(tag => UEArchitectureStyles.Matches(search, tag)) ||
                   SubsystemList.Project().Any(row => UEArchitectureStyles.Matches(search, row.Type.Name)) || LevelPaths_Internal().Any(path => UEArchitectureStyles.Matches(search, path));
        }

        internal static IEnumerable<string> LevelPaths_Internal()
        {
            return FrameworkIndex.ProjectScenes.Concat(EditorBuildSettings.scenes.Select(scene => scene.path)).Where(path => !string.IsNullOrEmpty(path));
        }

        public static UEArchitectureSettings[] FindAll()
        {
            return AssetDatabase.FindAssets("t:" + nameof(UEArchitectureSettings))
                                .Select(AssetDatabase.GUIDToAssetPath)
                                .Select(AssetDatabase.LoadAssetAtPath<UEArchitectureSettings>)
                                .Where(asset => asset != null)
                                .ToArray();
        }

        public static UEArchitectureSettings CreateAsset(string path = DefaultAssetPath)
        {
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";

            EnsureFolder_Internal(folder);

            var settings = ScriptableObject.CreateInstance<UEArchitectureSettings>();

            AssetDatabase.CreateAsset(settings, path);

            AssetDatabase.SaveAssets();

            FrameworkIndex.Invalidate();

            return settings;
        }

        /// <summary>
        /// Empty when players can load the asset; otherwise what is wrong with its location.
        /// </summary>
        public static string LocationProblem(string path)
        {
            var inResources = path.Split('/').Contains("Resources");

            var named = Path.GetFileNameWithoutExtension(path) == UEArchitectureSettings.ResourcesName;

            if (inResources && named)
            {
                return string.Empty;
            }

            return $"Players load this asset by name from a Resources folder, so it must be \"Resources/{UEArchitectureSettings.ResourcesName}.asset\". Right now builds would use the defaults.";
        }

        private static void Draw_Internal(string search)
        {
            var previousWidth = EditorGUIUtility.labelWidth;

            EditorGUIUtility.labelWidth = 200f;

            try
            {
                DrawPage_Internal(search);
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousWidth;
            }
        }

        private static void DrawPage_Internal(string search)
        {
            var all = FrameworkIndex.AllSettings;

            var settings = all.Length > 0 ? all[0] : null;

            if (settings == null)
            {
                EditorGUILayout.Space(6);

                if (UEArchitectureStyles.Notice("The project uses the defaults: a plain GameInstance, no default GameMode and no loading screen. " +
                                                $"Setting anything below creates {DefaultAssetPath}.", MessageType.Info, "Create settings"))
                {
                    Selection.activeObject = CreateAsset();

                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(settings);

                if (all.Length > 1)
                {
                    EditorGUILayout.HelpBox($"There are {all.Length} settings assets; this page edits {path}. Delete the others.", MessageType.Warning);
                }

                var problem = LocationProblem(path);

                if (problem.Length > 0 && UEArchitectureStyles.Notice(problem, MessageType.Error, "Move it to " + DefaultAssetPath))
                {
                    EnsureFolder_Internal(Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/'));

                    AssetDatabase.MoveAsset(path, AssetDatabase.GenerateUniqueAssetPath(DefaultAssetPath));

                    FrameworkIndex.Invalidate();

                    GUIUtility.ExitGUI();
                }
            }

            // Without an asset the page edits a stand-in; the first change creates the asset (see Materialize_Internal).
            var owner = settings != null ? settings : Transient_Internal();

            var options = new ClassFieldOptions { Materialize = settings != null ? null : (Func<Object>)Materialize_Internal };

            var serialized = new SerializedObject(owner);

            serialized.Update();

            if (UEArchitectureStyles.Matches(search, "Game", "Game Instance", "GameInstance", "Start Automatically"))
            {
                UEArchitectureStyles.Section("Game");

                ClassField.Layout(serialized.FindProperty("gameInstance"), new GUIContent("Game Instance", "Prefab with your GameInstance component. Empty = a plain GameInstance."),
                                  typeof(GameInstance), options);

                EditorGUILayout.PropertyField(serialized.FindProperty("startAutomatically"), new GUIContent("Start Automatically", "Start before the first scene loads, from any scene you press Play in."));
            }

            if (UEArchitectureStyles.Matches(search, "Levels", "Default GameMode", "GameMode", "Player Controller", "Default Pawn", "Pawn", "HUD", "Players", "GameState",
                                             "PlayerState", "Loading Screen"))
            {
                UEArchitectureStyles.Section("Levels");

                ClassField.Layout(serialized.FindProperty("defaultGameMode"), new GUIContent("Default GameMode", "Used by levels whose World Settings do not pick one."),
                                  typeof(GameMode), options);

                var defaultGameMode = serialized.FindProperty("defaultGameMode").objectReferenceValue as GameMode;

                if (defaultGameMode != null && EditorUtility.IsPersistent(defaultGameMode))
                {
                    EditorGUI.indentLevel++;

                    SelectedGameModePanel.DrawForProject(defaultGameMode);

                    EditorGUI.indentLevel--;

                    EditorGUILayout.Space(4);
                }

                ClassField.Layout(serialized.FindProperty("loadingScreen"), new GUIContent("Loading Screen", "Shown by Levels.Open while a level loads."), typeof(LoadingScreen), options);
            }

            // A search for a tag's name ("burn") shows this section too, with the tree filtered by it.
            var tagHit = !string.IsNullOrWhiteSpace(search) && FrameworkIndex.Tags.Any(tag => tag.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

            if (UEArchitectureStyles.Matches(search, "Gameplay Tags", "Tags", "Tag constants") || tagHit)
            {
                UEArchitectureStyles.Section("Gameplay Tags");

                EditorGUILayout.LabelField("Like \"Status.Burning\": parents (\"Status\") come by themselves, and Status.Burning matches Status. Tag fields in the Inspector offer these.",
                                           UEArchitectureStyles.MutedLabel);

                _tags.Draw(serialized, UEArchitectureStyles.Matches(search, "Gameplay Tags", "Tags", "Tag constants") ? null : search);
            }

            if (serialized.ApplyModifiedProperties() && settings == null)
            {
                CopyToAsset_Internal(owner);

                GUIUtility.ExitGUI();
            }

            if (settings != null)
            {
                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(settings), UEArchitectureStyles.MutedLabel);
            }

            if (UEArchitectureStyles.Matches(search, "Subsystems", "Subsystem", "Game", "World", "Tick") || SubsystemList.Project().Any(row => UEArchitectureStyles.Matches(search, row.Type.Name)))
            {
                ProjectListsSection.DrawSubsystems(UEArchitectureStyles.Matches(search, "Subsystems", "Subsystem", "Game", "World", "Tick") ? null : search);
            }

            if (UEArchitectureStyles.Matches(search, "Levels", "Level", "Build Settings", "Build", "Scenes", "Scene") || FrameworkIndex.LevelRows().Any(row => UEArchitectureStyles.Matches(search, row.Path)))
            {
                ProjectListsSection.DrawLevels(UEArchitectureStyles.Matches(search, "Levels", "Level", "Build Settings", "Build", "Scenes", "Scene") ? null : search);
            }

            if (UEArchitectureStyles.Matches(search, "Save games", "Save", "Slots"))
            {
                DrawSaveGames_Internal();
            }
        }

        /// <summary>
        /// The settings asset, created at <see cref="DefaultAssetPath"/> when the project has none (the first value set on
        /// the page, or by a '+' picker, creates it).
        /// </summary>
        private static Object Materialize_Internal()
        {
            var existing = FrameworkIndex.Settings;

            return existing != null ? existing : CreateAsset();
        }

        private static UEArchitectureSettings Transient_Internal()
        {
            if (_transient == null)
            {
                _transient = ScriptableObject.CreateInstance<UEArchitectureSettings>();

                _transient.hideFlags = HideFlags.HideAndDontSave;
            }

            return _transient;
        }

        private static void CopyToAsset_Internal(Object from)
        {
            var asset = Materialize_Internal();

            var source = new SerializedObject(from);

            var destination = new SerializedObject(asset);

            var iterator = source.GetIterator();

            var enter = true;

            while (iterator.NextVisible(enter))
            {
                enter = false;

                if (iterator.propertyPath != "m_Script")
                {
                    destination.CopyFromSerializedProperty(iterator);
                }
            }

            destination.ApplyModifiedProperties();

            AssetDatabase.SaveAssetIfDirty(asset);

            if (_transient != null)
            {
                Object.DestroyImmediate(_transient);

                _transient = null;
            }
        }

        private static void DrawSaveGames_Internal()
        {
            UEArchitectureStyles.Section("Save games");

            UEArchitectureStyles.Row("Folder", SaveGames.Folder);

            var slots = SaveGames.Slots();

            UEArchitectureStyles.Row("Slots", slots.Length == 0 ? "none" : string.Join(", ", slots), slots.Length == 0);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Open folder", GUILayout.Width(110)))
                {
                    System.IO.Directory.CreateDirectory(SaveGames.Folder);

                    EditorUtility.RevealInFinder(SaveGames.Folder);
                }

                using (new EditorGUI.DisabledScope(slots.Length == 0))
                {
                    if (GUILayout.Button("Delete all", GUILayout.Width(110)) &&
                        EditorUtility.DisplayDialog("Delete every save game?", $"{slots.Length} slot(s) in {SaveGames.Folder} will be deleted.", "Delete", "Cancel"))
                    {
                        foreach (var slot in slots)
                        {
                            SaveGames.Delete(slot);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True for classes in test assemblies (their name ends with ".Tests"), which pickers and lists leave out.
        /// </summary>
        internal static bool IsTestType_Internal(Type type)
        {
            return type.Assembly.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal);
        }

        private static void EnsureFolder_Internal(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');

            EnsureFolder_Internal(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
