using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Project Settings > UE Architecture: the project's GameInstance, default GameMode and loading screen, like Unreal's
    /// Maps &amp; Modes. The settings live in an asset inside a Resources folder so players can read them.
    /// </summary>
    internal static class UEArchitectureSettingsProvider
    {
        public const string SettingsPath = "Project/UE Architecture";

        public const string DefaultAssetPath = "Assets/Resources/" + UEArchitectureSettings.ResourcesName + ".asset";

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "UE Architecture",
                guiHandler = Draw_Internal,
                keywords = new[] { "GameInstance", "GameMode", "Subsystem", "Loading Screen", "Unreal" }
            };
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
            EditorGUIUtility.labelWidth = 200f;

            var all = FindAll();

            if (all.Length == 0)
            {
                EditorGUILayout.Space(6);

                if (UEArchitectureStyles.Notice("The project uses the defaults: a plain GameInstance, no default GameMode and no loading screen. " +
                                                "Create the settings asset to change them.", MessageType.Info, "Create settings"))
                {
                    Selection.activeObject = CreateAsset();
                }

                DrawSubsystems_Internal();

                return;
            }

            var settings = all[0];

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
            }

            var serialized = new SerializedObject(settings);

            serialized.Update();

            UEArchitectureStyles.Section("Game");

            EditorGUILayout.PropertyField(serialized.FindProperty("gameInstance"), new GUIContent("Game Instance", "Prefab with your GameInstance component. Empty = a plain GameInstance."));

            EditorGUILayout.PropertyField(serialized.FindProperty("startAutomatically"), new GUIContent("Start Automatically", "Start before the first scene loads, from any scene you press Play in."));

            UEArchitectureStyles.Section("Levels");

            EditorGUILayout.PropertyField(serialized.FindProperty("defaultGameMode"), new GUIContent("Default GameMode", "Used by levels whose World Settings do not pick one."));

            EditorGUILayout.PropertyField(serialized.FindProperty("loadingScreen"), new GUIContent("Loading Screen", "Shown by Levels.Open while a level loads."));

            serialized.ApplyModifiedProperties();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(path, UEArchitectureStyles.MutedLabel);

            DrawSubsystems_Internal();
        }

        private static void DrawSubsystems_Internal()
        {
            UEArchitectureStyles.Section("Subsystems in the project");

            DrawTypes_Internal("Game (whole game)", typeof(GameInstanceSubsystem));

            DrawTypes_Internal("World (each level)", typeof(WorldSubsystem));

            EditorGUILayout.LabelField("Created by themselves: derive from GameInstanceSubsystem or WorldSubsystem (Assets > Create > UE Architecture).", UEArchitectureStyles.MutedLabel);
        }

        private static void DrawTypes_Internal(string label, Type baseType)
        {
            var found = TypeCache.GetTypesDerivedFrom(baseType).Where(type => !type.IsAbstract && !IsTestType_Internal(type)).ToArray();

            var types = found.Select(type => UEArchitectureStyles.TypeName(type, found)).OrderBy(name => name).ToArray();

            UEArchitectureStyles.Row(label, types.Length == 0 ? "none" : string.Join(", ", types), types.Length == 0);
        }

        private static bool IsTestType_Internal(Type type)
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
