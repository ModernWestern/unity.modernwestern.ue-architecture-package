using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What happens when Play starts in a level that has no GameMode.
    /// </summary>
    internal enum PlayStartPolicy
    {
        /// <summary>
        /// Nothing.
        /// </summary>
        Ignore,

        /// <summary>
        /// One warning in the Console per level and session: "Animations.unity has no GameMode: players get no input".
        /// </summary>
        Warn
    }

    /// <summary>
    /// What a new Pawn prefab of the Game Mode Kit gets on its root.
    /// </summary>
    public enum PawnPreset
    {
        /// <summary>
        /// The Pawn component only.
        /// </summary>
        Empty,

        /// <summary>
        /// A capsule you can see (no collider), 2 m tall, standing on the root.
        /// </summary>
        Capsule,

        /// <summary>
        /// The capsule plus a NavMeshAgent and Path Following, ready for MoveTo (needs Unity's AI module).
        /// </summary>
        CapsuleNavAgent
    }

    /// <summary>
    /// Project Settings > UE Architecture > Editor: where the editor tools put the scripts and prefabs they generate and
    /// what they call them. Shared by the team in ProjectSettings/ (editor only, never in builds).
    /// </summary>
    [FilePath("ProjectSettings/UEArchitectureEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UEArchitectureEditorSettings : ScriptableSingleton<UEArchitectureEditorSettings>
    {
        public const string SettingsPath = UEArchitectureSettingsProvider.SettingsPath + "/Editor";

        [SerializeField, Tooltip("Where new scripts go when the tool has no better place (like next to the script that uses them).")]
        private string scriptsFolder = GeneratorConventions.DefaultScriptsFolder;

        [SerializeField, Tooltip("The folder that holds the generated prefabs ({Root}).")]
        private string prefabRoot = GeneratorConventions.DefaultPrefabRoot;

        [SerializeField, Tooltip("{Area} for GameModes, Player Controllers, AI Controllers, Pawns and the Game Instance.")]
        private string gameplayArea = GeneratorConventions.DefaultGameplayArea;

        [SerializeField, Tooltip("{Area} for HUDs, loading screens and widgets.")]
        private string uiArea = GeneratorConventions.DefaultUIArea;

        [SerializeField, Tooltip("Folder of a class's prefab. Tokens: {Root}, {Area}, {Class}.")]
        private string prefabFolderPattern = GeneratorConventions.DefaultPrefabFolderPattern;

        [SerializeField, Tooltip("A prefab's file name. {Class} is the class name: \"SitizensPlayerController\".")]
        private string prefabNamePattern = GeneratorConventions.DefaultPrefabNamePattern;

        [SerializeField, Tooltip("Folder of a level's overrides (Unreal's GameMode Override for one level), central for every level. Tokens: {Root}, {Scene}.")]
        private string overrideFolderPattern = GeneratorConventions.DefaultOverrideFolderPattern;

        [SerializeField, Tooltip("New GameMode scripts put their GameState and PlayerState in their own files (ArenaGameState.cs, ArenaPlayerState.cs) instead of in the GameMode's file.")]
        private bool splitStateFiles;

        [SerializeField, Tooltip("What a new Pawn prefab of the Game Mode Kit gets: nothing, a capsule you can see, or the capsule with a NavMeshAgent.")]
        private PawnPreset pawnPreset = PawnPreset.Capsule;

        [SerializeField, Tooltip("The screen size new HUD and loading screen Canvases are laid out for (Canvas Scaler, Scale With Screen Size).")]
        private Vector2Int referenceResolution = new(1920, 1080);

        [SerializeField, Tooltip("Pressing Play in a level without a GameMode: say it once in the Console, or say nothing.")]
        private PlayStartPolicy playStartCheck = PlayStartPolicy.Warn;

        /// <summary>
        /// The current conventions, as a plain value.
        /// </summary>
        public GeneratorConventions Conventions => new(Or_Internal(scriptsFolder, GeneratorConventions.DefaultScriptsFolder),
                                                       Or_Internal(prefabRoot, GeneratorConventions.DefaultPrefabRoot),
                                                       Or_Internal(gameplayArea, GeneratorConventions.DefaultGameplayArea),
                                                       Or_Internal(uiArea, GeneratorConventions.DefaultUIArea),
                                                       Or_Internal(prefabFolderPattern, GeneratorConventions.DefaultPrefabFolderPattern),
                                                       Or_Internal(prefabNamePattern, GeneratorConventions.DefaultPrefabNamePattern),
                                                       Or_Internal(overrideFolderPattern, GeneratorConventions.DefaultOverrideFolderPattern),
                                                       splitStateFiles);

        /// <summary>
        /// The conventions every tool uses.
        /// </summary>
        public static GeneratorConventions Current => instance.Conventions;

        /// <summary>
        /// What a new Pawn prefab of the Game Mode Kit gets by default.
        /// </summary>
        public PawnPreset DefaultPawnPreset => pawnPreset;

        /// <summary>
        /// The screen size of new HUD and loading screen Canvases (1920 x 1080 by default; never below 1 x 1).
        /// </summary>
        public Vector2Int ReferenceResolution => new(Mathf.Max(1, referenceResolution.x), Mathf.Max(1, referenceResolution.y));

        /// <summary>
        /// What pressing Play in a level without a GameMode does.
        /// </summary>
        public PlayStartPolicy PlayStartCheck
        {
            get => playStartCheck;
            internal set => playStartCheck = value;
        }

        /// <summary>
        /// Writes the settings to ProjectSettings/UEArchitectureEditorSettings.asset (text, for version control).
        /// </summary>
        public void SaveSettings()
        {
            Save(true);
        }

        /// <summary>
        /// Puts every field back to the defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            scriptsFolder = GeneratorConventions.DefaultScriptsFolder;

            prefabRoot = GeneratorConventions.DefaultPrefabRoot;

            gameplayArea = GeneratorConventions.DefaultGameplayArea;

            uiArea = GeneratorConventions.DefaultUIArea;

            prefabFolderPattern = GeneratorConventions.DefaultPrefabFolderPattern;

            prefabNamePattern = GeneratorConventions.DefaultPrefabNamePattern;

            overrideFolderPattern = GeneratorConventions.DefaultOverrideFolderPattern;

            splitStateFiles = false;

            pawnPreset = PawnPreset.Capsule;

            referenceResolution = new Vector2Int(1920, 1080);

            playStartCheck = PlayStartPolicy.Warn;
        }

        /// <summary>
        /// Uses other conventions until swapped back, without saving them (tests point generated files at a temporary
        /// folder this way). Returns the conventions in use before.
        /// </summary>
        internal GeneratorConventions Swap_Internal(GeneratorConventions conventions)
        {
            var previous = Conventions;

            scriptsFolder = conventions.ScriptsFolder;

            prefabRoot = conventions.PrefabRoot;

            gameplayArea = conventions.GameplayArea;

            uiArea = conventions.UIArea;

            prefabFolderPattern = conventions.PrefabFolderPattern;

            prefabNamePattern = conventions.PrefabNamePattern;

            overrideFolderPattern = conventions.OverrideFolderPattern;

            splitStateFiles = conventions.SplitStateFiles;

            return previous;
        }

        private static string Or_Internal(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    /// <summary>
    /// The page for <see cref="UEArchitectureEditorSettings"/>.
    /// </summary>
    internal static class UEArchitectureEditorSettingsProvider
    {
        private static SerializedObject _serialized;

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(UEArchitectureEditorSettings.SettingsPath, SettingsScope.Project)
            {
                label = "Editor",
                guiHandler = Draw_Internal,
                keywords = new[] { "Prefab", "Folder", "Scripts", "Naming", "Override", "Modes", "Generated", "Pawn", "Canvas", "Resolution", "Play" }
            };
        }

        private static void Draw_Internal(string search)
        {
            var settings = UEArchitectureEditorSettings.instance;

            settings.hideFlags &= ~HideFlags.NotEditable;

            if (_serialized == null || _serialized.targetObject != settings)
            {
                _serialized = new SerializedObject(settings);
            }

            var previousWidth = EditorGUIUtility.labelWidth;

            EditorGUIUtility.labelWidth = 190f;

            try
            {
                _serialized.Update();

                EditorGUILayout.LabelField("Where the '+' buttons, menus and the command line put the scripts and prefabs they make. Shared with the team in ProjectSettings/.", UEArchitectureStyles.MutedLabel);

                UEArchitectureStyles.Section("Scripts");

                FolderField_Internal("scriptsFolder", "Scripts Folder");

                EditorGUILayout.PropertyField(_serialized.FindProperty("splitStateFiles"), new GUIContent("Separate State Files", _serialized.FindProperty("splitStateFiles").tooltip));

                UEArchitectureStyles.Section("Prefabs");

                FolderField_Internal("prefabRoot", "Prefab Root");

                EditorGUILayout.PropertyField(_serialized.FindProperty("gameplayArea"), new GUIContent("Gameplay Area"));

                EditorGUILayout.PropertyField(_serialized.FindProperty("uiArea"), new GUIContent("UI Area"));

                EditorGUILayout.PropertyField(_serialized.FindProperty("prefabFolderPattern"), new GUIContent("Prefab Folder"));

                EditorGUILayout.PropertyField(_serialized.FindProperty("prefabNamePattern"), new GUIContent("Prefab Name"));

                UEArchitectureStyles.Section("Level overrides");

                EditorGUILayout.PropertyField(_serialized.FindProperty("overrideFolderPattern"), new GUIContent("Override Folder"));

                UEArchitectureStyles.Section("New prefabs and levels");

                EditorGUILayout.PropertyField(_serialized.FindProperty("pawnPreset"), new GUIContent("Pawn Preset", _serialized.FindProperty("pawnPreset").tooltip));

                EditorGUILayout.PropertyField(_serialized.FindProperty("referenceResolution"), new GUIContent("UI Reference Resolution", _serialized.FindProperty("referenceResolution").tooltip));

                EditorGUILayout.PropertyField(_serialized.FindProperty("playStartCheck"), new GUIContent("Play Without a GameMode", _serialized.FindProperty("playStartCheck").tooltip));

                if (_serialized.ApplyModifiedProperties())
                {
                    settings.SaveSettings();
                }

                var conventions = settings.Conventions;

                UEArchitectureStyles.Section("Examples");

                foreach (var (label, path) in Examples(conventions))
                {
                    UEArchitectureStyles.Row(label, path, true);
                }

                foreach (var folder in new[] { conventions.ScriptsFolder, GeneratorNaming.PrefabFolder(typeof(Pawn), conventions), GeneratorNaming.OverrideFolder("Boss", conventions) })
                {
                    var problem = GeneratorNaming.FolderProblem(folder);

                    if (problem.Length > 0)
                    {
                        EditorGUILayout.HelpBox(problem, MessageType.Warning);
                    }
                }

                EditorGUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(new GUIContent("Reset to defaults", "Assets/Scripts, Assets/Prefabs/Gameplay and UI, prefabs named after their class, overrides in Assets/Prefabs/Modes/<Scene>."), GUILayout.Width(140f)))
                    {
                        Undo.RecordObject(settings, "Reset UE Architecture editor settings");

                        settings.ResetToDefaults();

                        settings.SaveSettings();

                        _serialized.Update();
                    }
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousWidth;
            }
        }

        /// <summary>
        /// The rows under "Examples": where the tools would put a few files with these conventions, computed the way the
        /// tools compute them (so {Class} in a folder or name pattern shows as it will be written).
        /// </summary>
        internal static (string Label, string Path)[] Examples(GeneratorConventions conventions)
        {
            return new[]
            {
                ("Player Controller", GeneratorNaming.PrefabPath("MyPlayerController", typeof(PlayerController), conventions)),
                ("HUD", GeneratorNaming.PrefabPath("MyHUD", typeof(HUD), conventions)),
                ("GameMode for the Boss level", GeneratorNaming.OverridePath("Boss", "MyGameMode", conventions)),
                ("Script", GeneratorNaming.ScriptPath("MyPawn", conventions))
            };
        }

        private static void FolderField_Internal(string propertyName, string label)
        {
            var property = _serialized.FindProperty(propertyName);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));

                if (!GUILayout.Button(new GUIContent("…", "Pick a folder inside Assets/"), EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    return;
                }

                var picked = EditorUtility.OpenFolderPanel(label, property.stringValue, string.Empty);

                var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') + "/";

                picked = picked?.Replace('\\', '/');

                if (!string.IsNullOrEmpty(picked) && picked.StartsWith(projectRoot, System.StringComparison.OrdinalIgnoreCase))
                {
                    property.stringValue = picked.Substring(projectRoot.Length);

                    if (_serialized.ApplyModifiedProperties())
                    {
                        UEArchitectureEditorSettings.instance.SaveSettings();
                    }
                }

                GUIUtility.ExitGUI();
            }
        }
    }
}
