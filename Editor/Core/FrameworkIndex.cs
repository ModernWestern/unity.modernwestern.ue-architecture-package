using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Cached project lookups for the editor UI, so nothing scans the project on every repaint: the settings asset, the
    /// gameplay tags, the framework prefabs by their root component, which GameModes use a prefab and which levels
    /// reference an asset. The prefab index is built with one project scan on the first lookup; after that only the
    /// prefabs an AssetPostprocessor reports as changed are read again.
    /// </summary>
    [InitializeOnLoad]
    internal static partial class FrameworkIndex
    {
        /// <summary>
        /// The framework classes whose prefabs are indexed (a prefab counts when its root has one of them, or a subclass).
        /// </summary>
        public static readonly Type[] IndexedTypes = { typeof(GameMode), typeof(Controller), typeof(Pawn), typeof(HUD), typeof(GameInstance), typeof(LoadingScreen) };

        private static readonly Dictionary<string, SceneEntry> _scenes = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, bool> _frameworkScripts = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, bool> _gameModeScripts = new(StringComparer.Ordinal);

        private static readonly HashSet<string> _changedPrefabs = new(StringComparer.Ordinal);

        private static Dictionary<GameObject, HashSet<string>> _usage;

        private static Dictionary<string, List<string>> _referencing;

        private static int _usageVersion = -1;

        private static UEArchitectureSettings[] _settings;

        private static Dictionary<string, PrefabEntry> _prefabs;

        private static Dictionary<GameObject, List<GameMode>> _gameModesUsing;

        private static string[] _sceneGuids;

        private static UEArchitectureSettings _tagSource;

        private static int _tagFingerprint;

        private static string[] _tags;

        private static HashSet<string> _tagSet;

        static FrameworkIndex()
        {
            // Asset changes come through FrameworkIndexPostprocessor, path by path; this only catches what it cannot see
            // (a settings asset made or removed by a package), and drops nothing expensive.
            EditorApplication.projectChanged -= OnProjectChanged_Internal;

            EditorApplication.projectChanged += OnProjectChanged_Internal;
        }

        /// <summary>
        /// Goes up by one every time something indexed changes, so callers can drop their own derived caches.
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>
        /// Every UEArchitectureSettings asset in the project (normally one).
        /// </summary>
        public static UEArchitectureSettings[] AllSettings
        {
            get
            {
                if (_settings == null || HasDestroyed_Internal(_settings))
                {
                    _settings = UEArchitectureSettingsProvider.FindAll();
                }

                return _settings;
            }
        }

        /// <summary>
        /// The project's settings asset, or null when the project has none.
        /// </summary>
        public static UEArchitectureSettings Settings
        {
            get
            {
                var all = AllSettings;

                return all.Length > 0 ? all[0] : null;
            }
        }

        /// <summary>
        /// The project's gameplay tags with their parents ("Status.Burning" also gives "Status"), sorted.
        /// </summary>
        public static string[] Tags
        {
            get
            {
                var settings = Settings;

                var source = settings != null ? settings.GameplayTags : Array.Empty<string>();

                var fingerprint = Fingerprint_Internal(source);

                if (_tags == null || !ReferenceEquals(settings, _tagSource) || fingerprint != _tagFingerprint)
                {
                    _tags = ProjectTags.Build(source);

                    _tagSet = new HashSet<string>(_tags, StringComparer.Ordinal);

                    _tagSource = settings;

                    _tagFingerprint = fingerprint;
                }

                return _tags;
            }
        }

        public static bool IsKnownTag(string tag)
        {
            _ = Tags;

            return string.IsNullOrEmpty(tag) || _tagSet.Contains(tag);
        }

        /// <summary>
        /// Prefab assets whose root has a component of this type or a subclass, e.g. every Pawn prefab, sorted by path. The
        /// type must be one of <see cref="IndexedTypes"/> or derive from one.
        /// </summary>
        public static IReadOnlyList<GameObject> PrefabsWith(Type componentType)
        {
            var found = new List<GameObject>();

            if (componentType == null)
            {
                return found;
            }

            foreach (var entry in Prefabs_Internal())
            {
                if (entry.Types.Any(componentType.IsAssignableFrom) && !found.Contains(entry.Asset))
                {
                    found.Add(entry.Asset);
                }
            }

            return found;
        }

        /// <summary>
        /// The prefab of a class: the one whose root has exactly this class (not a subclass), preferring the conventional
        /// path, then plain prefabs over variants, then the first by path. A level's own copies (prefabs in a level's
        /// override folder, e.g. Assets/Prefabs/Modes/Boss) never count. Null when there is none.
        /// </summary>
        public static GameObject PrefabWithExactly(Type componentType)
        {
            if (componentType == null)
            {
                return null;
            }

            var conventions = UEArchitectureEditorSettings.Current;

            var conventional = GeneratorNaming.PrefabPath(componentType, conventions);

            GameObject best = null;

            var bestRank = int.MaxValue;

            foreach (var entry in Prefabs_Internal())
            {
                if (!entry.Types.Contains(componentType) || GeneratorNaming.SceneOfOverrideFolder(GeneratorNaming.FolderOf(entry.Path), conventions) != null)
                {
                    continue;
                }

                var rank = entry.Path == conventional ? 0 : PrefabUtility.GetPrefabAssetType(entry.Asset) == PrefabAssetType.Variant ? 2 : 1;

                if (rank < bestRank)
                {
                    best = entry.Asset;

                    bestRank = rank;
                }
            }

            return best;
        }

        /// <summary>
        /// The GameMode prefabs whose PlayerController, Pawn or HUD is this prefab.
        /// </summary>
        public static IReadOnlyList<GameMode> GameModesUsing(GameObject prefab)
        {
            if (prefab == null)
            {
                return Array.Empty<GameMode>();
            }

            if (_gameModesUsing == null)
            {
                // The prefab scan first: rebuilding the prefab index clears this cache.
                var gameModePrefabs = PrefabsWith(typeof(GameMode));

                var usage = new Dictionary<GameObject, List<GameMode>>();

                foreach (var gameModePrefab in gameModePrefabs)
                {
                    var gameMode = gameModePrefab != null ? gameModePrefab.GetComponent<GameMode>() : null;

                    if (gameMode == null)
                    {
                        continue;
                    }

                    AddUsage_Internal(usage, gameMode.PlayerControllerPrefab, gameMode);

                    AddUsage_Internal(usage, gameMode.DefaultPawnPrefab, gameMode);

                    AddUsage_Internal(usage, gameMode.HUDPrefab, gameMode);
                }

                _gameModesUsing = usage;
            }

            return _gameModesUsing.TryGetValue(prefab, out var users) ? users : (IReadOnlyList<GameMode>)Array.Empty<GameMode>();
        }

        /// <summary>
        /// The saved scenes under Assets/ that reference an asset directly (e.g. a GameMode prefab in their World
        /// Settings), read from the asset database's dependency list: a file scan, cached until assets change. Unsaved
        /// changes of open levels are not in it (see <see cref="LevelsReferencing"/>).
        /// </summary>
        public static IReadOnlyList<string> ScenesReferencing(Object asset)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);

            if (string.IsNullOrEmpty(assetPath))
            {
                return Array.Empty<string>();
            }

            CheckUsageVersion_Internal();

            if (_referencing.TryGetValue(assetPath, out var cached))
            {
                return cached;
            }

            var found = new List<string>();

            foreach (var scene in Scenes_Internal())
            {
                if (scene.Value.Dependencies.Contains(assetPath))
                {
                    found.Add(scene.Key);
                }
            }

            _referencing[assetPath] = found;

            return found;
        }

        /// <summary>
        /// The levels that pick a GameMode prefab directly (World Settings, or a placed copy): the saved files, with the
        /// open levels that have unsaved changes read as they are now.
        /// </summary>
        public static IReadOnlyList<string> LevelsReferencing(GameObject gameModePrefab)
        {
            var found = new List<string>();

            if (gameModePrefab == null)
            {
                return found;
            }

            var dirty = DirtyLevels_Internal();

            foreach (var scenePath in ScenesReferencing(gameModePrefab))
            {
                if (!dirty.ContainsKey(scenePath))
                {
                    found.Add(scenePath);
                }
            }

            foreach (var level in dirty)
            {
                if (ReferencesLive_Internal(level.Value, gameModePrefab))
                {
                    found.Add(level.Key);
                }
            }

            return found;
        }

        /// <summary>
        /// How many levels (scenes under Assets/) use a GameMode prefab: the ones that pick it directly (World Settings or
        /// a placed copy) and, for the project default, the ones without a GameMode of their own. Saved levels are read
        /// from their files (cached until assets change); open levels with unsaved changes are read as they are now.
        /// </summary>
        public static int LevelsUsing(GameObject gameModePrefab)
        {
            if (gameModePrefab == null)
            {
                return 0;
            }

            CheckUsageVersion_Internal();

            var settings = Settings;

            var isDefault = settings != null && settings.DefaultGameMode != null && settings.DefaultGameMode.gameObject == gameModePrefab;

            if (!_usage.TryGetValue(gameModePrefab, out var users))
            {
                users = new HashSet<string>(StringComparer.Ordinal);

                var assetPath = AssetDatabase.GetAssetPath(gameModePrefab);

                var gameModePrefabs = new HashSet<string>(PrefabsWith(typeof(GameMode)).Select(AssetDatabase.GetAssetPath), StringComparer.Ordinal);

                foreach (var scene in Scenes_Internal())
                {
                    var dependencies = scene.Value.Dependencies;

                    if (dependencies.Contains(assetPath) || isDefault && !dependencies.Any(path => gameModePrefabs.Contains(path) || IsGameModeScript_Internal(path)))
                    {
                        users.Add(scene.Key);
                    }
                }

                _usage[gameModePrefab] = users;
            }

            var dirty = DirtyLevels_Internal();

            var count = users.Count(scenePath => !dirty.ContainsKey(scenePath));

            foreach (var level in dirty.Values)
            {
                if (ReferencesLive_Internal(level, gameModePrefab) || isDefault && GameModeInfo.Find<GameMode>(level) == null && !HasOverride_Internal(level))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// "ArenaGameMode.prefab is used by 3 levels.", for the GameMode panels.
        /// </summary>
        public static string UsageLine(GameObject gameModePrefab)
        {
            if (gameModePrefab == null)
            {
                return string.Empty;
            }

            var count = LevelsUsing(gameModePrefab);

            var file = Path.GetFileName(AssetDatabase.GetAssetPath(gameModePrefab));

            return count == 0 ? $"{file} is not used by any level yet." : count == 1 ? $"{file} is used by 1 level." : $"{file} is used by {count} levels.";
        }

        /// <summary>
        /// "Shared prefab: ArenaGameMode.prefab, used by 3 levels. Changes here apply to all of them.", for a level that
        /// shows a GameMode it does not own.
        /// </summary>
        public static string SharedLine(GameObject gameModePrefab)
        {
            if (gameModePrefab == null)
            {
                return string.Empty;
            }

            var count = LevelsUsing(gameModePrefab);

            var file = Path.GetFileName(AssetDatabase.GetAssetPath(gameModePrefab));

            return count <= 1
                       ? $"Shared prefab: {file}. Changes here change the prefab itself, so every level that uses it later gets them too."
                       : $"Shared prefab: {file}, used by {count} levels. Changes here apply to all of them.";
        }

        /// <summary>
        /// Clears every cache; the next lookup rebuilds what it needs (the prefab index with one project scan).
        /// </summary>
        public static void Invalidate()
        {
            _settings = null;

            _prefabs = null;

            _changedPrefabs.Clear();

            _gameModesUsing = null;

            _sceneGuids = null;

            _tags = null;

            ClearLevelCaches_Internal();

            Version++;
        }

        /// <summary>
        /// A tool saved a prefab: only that prefab is read again on the next lookup.
        /// </summary>
        internal static void PrefabSaved_Internal(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _changedPrefabs.Add(path);

            _gameModesUsing = null;

            LevelAssetsChanged_Internal(new[] { path }, false);

            Version++;
        }

        internal static void OnAssetsChanged_Internal(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var any = false;

            // The levels list keeps what it read until something it used changes (see LevelSummaryOf).
            LevelAssetsChanged_Internal(imported, deleted.Length + moved.Length > 0);

            foreach (var path in imported.Concat(deleted).Concat(moved).Concat(movedFrom))
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    // Only the changed prefabs are read again (the full scan happens once, on the first lookup).
                    _changedPrefabs.Add(path);

                    _gameModesUsing = null;

                    any = true;
                }
                else if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    // A script change compiles and reloads the domain, which starts every cache again anyway.
                    _frameworkScripts.Remove(path);

                    _gameModeScripts.Remove(path);

                    any = true;
                }
                else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    _settings = null;

                    _tags = null;

                    any = true;
                }
                else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    _sceneGuids = null;

                    _scenes.Remove(path);

                    any = true;
                }
            }

            if (any)
            {
                Version++;
            }
        }

        private static void OnProjectChanged_Internal()
        {
            _settings = null;
        }

        private static void CheckUsageVersion_Internal()
        {
            if (_usage != null && _referencing != null && _usageVersion == Version)
            {
                return;
            }

            _usage = new Dictionary<GameObject, HashSet<string>>();

            _referencing = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            _usageVersion = Version;
        }

        /// <summary>
        /// The open levels (saved scenes under Assets/, not preview scenes) with unsaved changes, by path: their files are
        /// out of date, so they are read as they are now.
        /// </summary>
        private static Dictionary<string, Scene> DirtyLevels_Internal()
        {
            var dirty = new Dictionary<string, Scene>(StringComparer.Ordinal);

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                if (scene.isLoaded && scene.isDirty && !string.IsNullOrEmpty(scene.path) && scene.path.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !EditorSceneManager.IsPreviewScene(scene))
                {
                    dirty[scene.path] = scene;
                }
            }

            return dirty;
        }

        private static bool ReferencesLive_Internal(Scene scene, GameObject gameModePrefab)
        {
            var placed = GameModeInfo.Find<GameMode>(scene);

            if (placed != null && PrefabUtility.IsPartOfPrefabInstance(placed))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(placed);

                if (source != null && source.transform.root.gameObject == gameModePrefab)
                {
                    return true;
                }
            }

            var worldSettings = GameModeInfo.Find<WorldSettings>(scene);

            return worldSettings != null && worldSettings.GameModeOverride != null && worldSettings.GameModeOverride.transform.root.gameObject == gameModePrefab;
        }

        private static bool HasOverride_Internal(Scene scene)
        {
            var worldSettings = GameModeInfo.Find<WorldSettings>(scene);

            return worldSettings != null && worldSettings.GameModeOverride != null;
        }

        private static IEnumerable<KeyValuePair<string, SceneEntry>> Scenes_Internal()
        {
            _sceneGuids ??= AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });

            foreach (var guid in _sceneGuids)
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(scenePath))
                {
                    continue;
                }

                var stamp = File.Exists(scenePath) ? File.GetLastWriteTimeUtc(scenePath).Ticks : 0L;

                if (!_scenes.TryGetValue(scenePath, out var entry) || entry.Stamp != stamp)
                {
                    entry = new SceneEntry(stamp, new HashSet<string>(AssetDatabase.GetDependencies(scenePath, false), StringComparer.Ordinal));

                    _scenes[scenePath] = entry;
                }

                yield return new KeyValuePair<string, SceneEntry>(scenePath, entry);
            }
        }

        private static bool IsGameModeScript_Internal(string path)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_gameModeScripts.TryGetValue(path, out var known))
            {
                return known;
            }

            var type = AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass();

            var gameMode = type != null && typeof(GameMode).IsAssignableFrom(type);

            _gameModeScripts[path] = gameMode;

            return gameMode;
        }

        private static void AddUsage_Internal(Dictionary<GameObject, List<GameMode>> usage, Component prefab, GameMode user)
        {
            if (prefab == null)
            {
                return;
            }

            if (!usage.TryGetValue(prefab.gameObject, out var users))
            {
                users = new List<GameMode>();

                usage[prefab.gameObject] = users;
            }

            if (!users.Contains(user))
            {
                users.Add(user);
            }
        }

        private static IEnumerable<PrefabEntry> Prefabs_Internal()
        {
            if (_prefabs == null)
            {
                _prefabs = new Dictionary<string, PrefabEntry>(StringComparer.Ordinal);

                _changedPrefabs.Clear();

                _gameModesUsing = null;

                var memo = new Dictionary<string, bool>(StringComparer.Ordinal);

                foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
                {
                    Index_Internal(AssetDatabase.GUIDToAssetPath(guid), memo);
                }
            }
            else if (_changedPrefabs.Count > 0)
            {
                var memo = new Dictionary<string, bool>(StringComparer.Ordinal);

                foreach (var path in _changedPrefabs)
                {
                    _prefabs.Remove(path);

                    Index_Internal(path, memo);
                }

                _changedPrefabs.Clear();

                _gameModesUsing = null;
            }

            List<string> gone = null;

            foreach (var entry in _prefabs.Values)
            {
                if (entry.Asset == null)
                {
                    (gone ??= new List<string>()).Add(entry.Path);
                }
            }

            if (gone != null)
            {
                foreach (var path in gone)
                {
                    _prefabs.Remove(path);
                }
            }

            return _prefabs.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal);
        }

        private static void Index_Internal(string path, Dictionary<string, bool> memo)
        {
            // Only prefabs that use a framework script, directly or through the prefab they are a variant of, are loaded.
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !UsesFrameworkScript_Internal(path, memo, 0))
            {
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (asset == null)
            {
                return;
            }

            var types = asset.GetComponents<MonoBehaviour>()
                             .Where(component => component != null)
                             .Select(component => component.GetType())
                             .Where(type => IndexedTypes.Any(indexed => indexed.IsAssignableFrom(type)))
                             .Distinct()
                             .ToArray();

            if (types.Length > 0)
            {
                _prefabs[path] = new PrefabEntry(path, asset, types);
            }
        }

        /// <summary>
        /// True when the prefab's own file uses a framework script, or a prefab it is made from (its base, for a variant)
        /// does. Only direct dependencies are read, so textures and meshes deep below are never listed.
        /// </summary>
        private static bool UsesFrameworkScript_Internal(string path, Dictionary<string, bool> memo, int depth)
        {
            if (memo.TryGetValue(path, out var known))
            {
                return known;
            }

            // A loop of prefabs (never valid, but cheap to guard) ends here.
            memo[path] = false;

            var found = false;

            foreach (var dependency in AssetDatabase.GetDependencies(path, false))
            {
                if (dependency == path)
                {
                    continue;
                }

                if (IsFrameworkScript_Internal(dependency) ||
                    depth < 8 && dependency.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && UsesFrameworkScript_Internal(dependency, memo, depth + 1))
                {
                    found = true;

                    break;
                }
            }

            memo[path] = found;

            return found;
        }

        private static bool IsFrameworkScript_Internal(string path)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_frameworkScripts.TryGetValue(path, out var known))
            {
                return known;
            }

            var type = AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass();

            var framework = type != null && IndexedTypes.Any(indexed => indexed.IsAssignableFrom(type));

            _frameworkScripts[path] = framework;

            return framework;
        }

        private static bool HasDestroyed_Internal(UEArchitectureSettings[] all)
        {
            foreach (var settings in all)
            {
                if (settings == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Fingerprint_Internal(string[] tags)
        {
            unchecked
            {
                var hash = 17 + tags.Length;

                foreach (var tag in tags)
                {
                    hash = hash * 31 + (tag?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }

        private readonly struct PrefabEntry
        {
            public readonly string Path;

            public readonly GameObject Asset;

            public readonly Type[] Types;

            public PrefabEntry(string path, GameObject asset, Type[] types)
            {
                Path = path;

                Asset = asset;

                Types = types;
            }
        }

        private readonly struct SceneEntry
        {
            public readonly long Stamp;

            public readonly HashSet<string> Dependencies;

            public SceneEntry(long stamp, HashSet<string> dependencies)
            {
                Stamp = stamp;

                Dependencies = dependencies;
            }
        }
    }

    /// <summary>
    /// Tells <see cref="FrameworkIndex"/> which prefabs, scripts, assets and scenes were imported, moved or deleted.
    /// </summary>
    internal sealed class FrameworkIndexPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            FrameworkIndex.OnAssetsChanged_Internal(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
        }
    }
}
