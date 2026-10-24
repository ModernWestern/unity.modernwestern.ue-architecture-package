using System;
using System.Text;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where generated scripts and prefabs go and what they are called, as set in Project Settings > UE Architecture >
    /// Editor. A plain value, so the naming rules are tested without touching the project's settings.
    /// </summary>
    internal readonly struct GeneratorConventions
    {
        public readonly string ScriptsFolder;

        public readonly string PrefabRoot;

        public readonly string GameplayArea;

        public readonly string UIArea;

        public readonly string PrefabFolderPattern;

        public readonly string PrefabNamePattern;

        public readonly string OverrideFolderPattern;

        /// <summary>
        /// A new GameMode script puts its GameState and PlayerState in their own files (ArenaGameState.cs,
        /// ArenaPlayerState.cs) instead of next to the GameMode in ArenaGameMode.cs.
        /// </summary>
        public readonly bool SplitStateFiles;

        public GeneratorConventions(string scriptsFolder, string prefabRoot, string gameplayArea, string uiArea, string prefabFolderPattern, string prefabNamePattern,
                                    string overrideFolderPattern, bool splitStateFiles = false)
        {
            ScriptsFolder = scriptsFolder;

            PrefabRoot = prefabRoot;

            GameplayArea = gameplayArea;

            UIArea = uiArea;

            PrefabFolderPattern = prefabFolderPattern;

            PrefabNamePattern = prefabNamePattern;

            OverrideFolderPattern = overrideFolderPattern;

            SplitStateFiles = splitStateFiles;
        }

        /// <summary>
        /// The defaults (the Sitizens layout): scripts in Assets/Scripts, prefabs named after their class in
        /// Assets/Prefabs/Gameplay or Assets/Prefabs/UI, and level overrides in Assets/Prefabs/Modes/&lt;Scene&gt;.
        /// </summary>
        public static GeneratorConventions Defaults => new(DefaultScriptsFolder, DefaultPrefabRoot, DefaultGameplayArea, DefaultUIArea, DefaultPrefabFolderPattern,
                                                           DefaultPrefabNamePattern, DefaultOverrideFolderPattern);

        public const string DefaultScriptsFolder = "Assets/Scripts";

        public const string DefaultPrefabRoot = "Assets/Prefabs";

        public const string DefaultGameplayArea = "Gameplay";

        public const string DefaultUIArea = "UI";

        public const string DefaultPrefabFolderPattern = "{Root}/{Area}";

        public const string DefaultPrefabNamePattern = "{Class}";

        public const string DefaultOverrideFolderPattern = "{Root}/Modes/{Scene}";
    }

    /// <summary>
    /// Pure naming rules for generated assets. Tokens: {Class} (the component's class name), {Scene} (the level's file
    /// name), {Root} (the prefab root folder) and {Area} (Gameplay or UI).
    /// </summary>
    internal static class GeneratorNaming
    {
        /// <summary>
        /// The area a class's prefab belongs to: UI for HUDs, loading screens and ObserverVM widgets, Gameplay otherwise.
        /// </summary>
        public static string AreaOf(Type type, GeneratorConventions conventions)
        {
            return IsUI(type) ? conventions.UIArea : conventions.GameplayArea;
        }

        public static bool IsUI(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (typeof(HUD).IsAssignableFrom(type) || typeof(LoadingScreen).IsAssignableFrom(type))
            {
                return true;
            }

            for (var current = type; current != null; current = current.BaseType)
            {
                // ObserverVM widgets, without a reference to ObserverVM.
                if (current.Name.StartsWith("Widget", StringComparison.Ordinal) && (current.Namespace ?? string.Empty).Contains("ObserverVM"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The folder a class's prefab goes to, e.g. "Assets/Prefabs/Gameplay".
        /// </summary>
        public static string PrefabFolder(Type type, GeneratorConventions conventions)
        {
            return CleanFolder(Expand(conventions.PrefabFolderPattern, type?.Name, null, conventions.PrefabRoot, AreaOf(type, conventions)));
        }

        /// <summary>
        /// A class's prefab file name without extension, e.g. "SitizensPlayerController".
        /// </summary>
        public static string PrefabName(Type type, GeneratorConventions conventions)
        {
            return PrefabName(type?.Name, conventions);
        }

        public static string PrefabName(string className, GeneratorConventions conventions)
        {
            var name = Expand(conventions.PrefabNamePattern, className, null, conventions.PrefabRoot, null).Trim();

            return CleanFileName(name.Length == 0 ? className : name);
        }

        /// <summary>
        /// Where a class's prefab goes, e.g. "Assets/Prefabs/Gameplay/SitizensPlayerController.prefab".
        /// </summary>
        public static string PrefabPath(Type type, GeneratorConventions conventions)
        {
            return $"{PrefabFolder(type, conventions)}/{PrefabName(type, conventions)}.prefab";
        }

        /// <summary>
        /// Where the prefab of a class that is not compiled yet goes: named after <paramref name="className"/>, in the
        /// area of <paramref name="parent"/> (the class it derives from).
        /// </summary>
        public static string PrefabPath(string className, Type parent, GeneratorConventions conventions)
        {
            var folder = CleanFolder(Expand(conventions.PrefabFolderPattern, className, null, conventions.PrefabRoot, AreaOf(parent, conventions)));

            return $"{folder}/{PrefabName(className, conventions)}.prefab";
        }

        /// <summary>
        /// The folder of a level's overrides, e.g. "Assets/Prefabs/Modes/Boss".
        /// </summary>
        public static string OverrideFolder(string sceneName, GeneratorConventions conventions)
        {
            return CleanFolder(Expand(conventions.OverrideFolderPattern, null, sceneName, conventions.PrefabRoot, conventions.GameplayArea));
        }

        /// <summary>
        /// The name a level is known by in override folders when another level has the same file name: the file name
        /// plus its folder's name, e.g. "Gameplay (Snow)" for Assets/Levels/Snow/Gameplay.unity.
        /// </summary>
        public static string DisambiguatedLevelName(string scenePath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(scenePath ?? string.Empty);

            var folder = FolderOf(scenePath);

            var parent = folder.Substring(folder.LastIndexOf('/') + 1);

            return parent.Length == 0 ? name : $"{name} ({parent})";
        }

        /// <summary>
        /// The folders a level's own copies can be in: its override folder ("Assets/Prefabs/Modes/Boss"), then the one
        /// used when another level has the same name ("Assets/Prefabs/Modes/Boss (Arena)").
        /// </summary>
        public static string[] LevelFolders(string scenePath, GeneratorConventions conventions)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(scenePath ?? string.Empty);

            var primary = OverrideFolder(name, conventions);

            var secondary = OverrideFolder(DisambiguatedLevelName(scenePath), conventions);

            return primary == secondary ? new[] { primary } : new[] { primary, secondary };
        }

        /// <summary>
        /// Where a level's override of a class goes, e.g. "Assets/Prefabs/Modes/Boss/GameplayGameMode.prefab".
        /// </summary>
        public static string OverridePath(string sceneName, string className, GeneratorConventions conventions)
        {
            return $"{OverrideFolder(sceneName, conventions)}/{PrefabName(className, conventions)}.prefab";
        }

        /// <summary>
        /// Where a new script goes when the flow has no better place, e.g. "Assets/Scripts/SitizensPawn.cs".
        /// </summary>
        public static string ScriptPath(string className, GeneratorConventions conventions)
        {
            return $"{CleanFolder(conventions.ScriptsFolder)}/{className}.cs";
        }

        /// <summary>
        /// The level whose override folder this is ("Assets/Prefabs/Modes/Boss" gives "Boss"), or null when the folder is
        /// not a level's override folder.
        /// </summary>
        public static string SceneOfOverrideFolder(string folder, GeneratorConventions conventions)
        {
            const string marker = "\u0001";

            var pattern = CleanFolder(Expand(conventions.OverrideFolderPattern, null, marker, conventions.PrefabRoot, conventions.GameplayArea));

            var at = pattern.IndexOf(marker, StringComparison.Ordinal);

            var clean = CleanFolder(folder);

            if (at < 0 || clean.Length == 0)
            {
                return null;
            }

            var prefix = pattern.Substring(0, at);

            var suffix = pattern.Substring(at + marker.Length);

            if (clean.Length <= prefix.Length + suffix.Length || !clean.StartsWith(prefix, StringComparison.Ordinal) || !clean.EndsWith(suffix, StringComparison.Ordinal))
            {
                return null;
            }

            var scene = clean.Substring(prefix.Length, clean.Length - prefix.Length - suffix.Length);

            return scene.Length == 0 || scene.Contains("/") ? null : scene;
        }

        /// <summary>
        /// Where "Variant of &lt;current&gt;" puts its prefab: in the level's override folder when there is a level
        /// (<paramref name="sceneName"/>, or the override folder <paramref name="ownerAssetPath"/> is in), named after the
        /// class ("Assets/Prefabs/Modes/Boss/ArenaPawn.prefab"); otherwise next to the current prefab
        /// ("Assets/Prefabs/Gameplay/ArenaPawnVariant.prefab"), or in <paramref name="fallbackFolder"/> when the current
        /// prefab is outside Assets/.
        /// </summary>
        public static string VariantPath(string currentAssetPath, string currentName, string className, string ownerAssetPath, string sceneName, string fallbackFolder,
                                         GeneratorConventions conventions)
        {
            var scene = !string.IsNullOrEmpty(sceneName) ? sceneName : SceneOfOverrideFolder(FolderOf(ownerAssetPath), conventions);

            if (!string.IsNullOrEmpty(scene))
            {
                return OverridePath(scene, className, conventions);
            }

            var folder = FolderOf(currentAssetPath);

            if (FolderProblem(folder).Length > 0)
            {
                folder = CleanFolder(fallbackFolder);
            }

            return $"{folder}/{PrefabName(currentName + "Variant", conventions)}.prefab";
        }

        /// <summary>
        /// The name offered for a new class in a field: the owner's name without its kind plus the field's class
        /// ("ArenaGameMode" and PlayerController give "ArenaPlayerController"), or the product name when the owner is a
        /// framework class ("My Game" and Pawn give "MyGamePawn").
        /// </summary>
        public static string InitialClassName(string ownerClassName, bool ownerIsFramework, string productName, string fieldClassName)
        {
            string prefix;

            if (!ownerIsFramework && !string.IsNullOrEmpty(ownerClassName))
            {
                prefix = ClassTemplates.BaseName(ownerClassName, "GameMode", "Mode", "PlayerController", "AIController", "Controller", "Pawn", "HUD", "GameInstance",
                                                 "LoadingScreen", "Settings");
            }
            else
            {
                prefix = CreateFrameworkScripts.ClassName(productName ?? string.Empty);

                if (prefix == "New")
                {
                    prefix = "My";
                }
            }

            return prefix.EndsWith(fieldClassName ?? string.Empty, StringComparison.Ordinal) ? prefix : prefix + fieldClassName;
        }

        /// <summary>
        /// Where a new script for a field goes: next to the owner's script when that is a project script under Assets/,
        /// otherwise the scripts folder of the conventions.
        /// </summary>
        public static string ScriptFolderFor(string ownerScriptPath, GeneratorConventions conventions)
        {
            var folder = FolderOf(ownerScriptPath);

            return folder.Length > 0 && FolderProblem(folder).Length == 0 ? folder : CleanFolder(conventions.ScriptsFolder);
        }

        /// <summary>
        /// The folder of an asset path ("Assets/Prefabs/Boss.prefab" gives "Assets/Prefabs"), or empty.
        /// </summary>
        public static string FolderOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            var clean = assetPath.Replace('\\', '/');

            var slash = clean.LastIndexOf('/');

            return slash <= 0 ? string.Empty : clean.Substring(0, slash);
        }

        /// <summary>
        /// Replaces the tokens of a pattern. A token without a value is left out.
        /// </summary>
        public static string Expand(string pattern, string className, string sceneName, string root, string area)
        {
            return (pattern ?? string.Empty).Replace("{Class}", className ?? string.Empty)
                                            .Replace("{Scene}", sceneName ?? string.Empty)
                                            .Replace("{Root}", root ?? string.Empty)
                                            .Replace("{Area}", area ?? string.Empty);
        }

        /// <summary>
        /// A project folder with forward slashes, no doubled or trailing slash: " Assets\Prefabs//UI/ " → "Assets/Prefabs/UI".
        /// </summary>
        public static string CleanFolder(string folder)
        {
            var parts = (folder ?? string.Empty).Replace('\\', '/').Split('/');

            var clean = new StringBuilder();

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (clean.Length > 0)
                {
                    clean.Append('/');
                }

                clean.Append(trimmed);
            }

            return clean.ToString();
        }

        /// <summary>
        /// Empty when generated files may go into the folder; otherwise why not (only Assets/ is writable: packages are read-only).
        /// </summary>
        public static string FolderProblem(string folder)
        {
            var clean = CleanFolder(folder);

            if (clean == "Assets" || clean.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return clean.Contains("..") ? $"\"{folder}\" cannot go up with \"..\"." : string.Empty;
            }

            return $"\"{folder}\" is not inside Assets/. Generated files must go under Assets/ (packages are read-only).";
        }

        private static string CleanFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();

            var clean = new StringBuilder(name.Length);

            foreach (var character in name)
            {
                clean.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            }

            return clean.ToString();
        }
    }
}
