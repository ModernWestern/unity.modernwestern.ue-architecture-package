using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    internal static partial class FrameworkIndex
    {
        private static readonly Dictionary<string, CachedLevel> _levelSummaries = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, SceneObjectRole> _roles = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> _prefabOverrides = new(StringComparer.Ordinal);

        /// <summary>
        /// How many level files were read for their summary, for tests (a cached summary is not read again).
        /// </summary>
        internal static int LevelReads_Internal { get; private set; }

        private static List<LevelRow> _levelRows;

        private static string _levelRowsKey;

        /// <summary>
        /// The saved levels under Assets/, by path (cached until a level is added, moved or deleted).
        /// </summary>
        public static IReadOnlyList<string> ProjectScenes
        {
            get
            {
                _sceneGuids ??= AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });

                return _sceneGuids.Select(AssetDatabase.GUIDToAssetPath).Where(path => !string.IsNullOrEmpty(path)).Distinct().ToList();
            }
        }

        /// <summary>
        /// What a saved level's file says about its GameMode, World Settings and Player Starts, read without opening it.
        /// Cached by the file's write time; a script or prefab it uses changing (or any asset moving or going) reads it
        /// again. Approximate (see <see cref="LevelSummary"/>).
        /// </summary>
        public static LevelSummary LevelSummaryOf(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
            {
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(scenePath).Ticks;

            if (_levelSummaries.TryGetValue(scenePath, out var cached) && cached.Stamp == stamp)
            {
                return cached.Summary;
            }

            LevelReads_Internal++;

            // The GUIDs the summary depends on: when one of them changes, only the levels that use it are read again.
            var guids = new HashSet<string>(StringComparer.Ordinal);

            SceneObjectRole Role(string guid)
            {
                guids.Add(guid ?? string.Empty);

                return RoleOf_Internal(guid);
            }

            string PrefabOverride(string guid)
            {
                guids.Add(guid ?? string.Empty);

                return PrefabOverrideOf_Internal(guid);
            }

            string text;

            try
            {
                text = File.ReadAllText(scenePath);
            }
            catch (IOException)
            {
                text = string.Empty;
            }

            var summary = SceneFileScan.Parse(text, Role, AssetDatabase.GUIDToAssetPath, PrefabOverride);

            if (!summary.FromText)
            {
                // A binary level: only the assets it uses are known (a GameMode prefab, World Settings), not how many starts.
                foreach (var dependency in AssetDatabase.GetDependencies(scenePath, false))
                {
                    var role = Role(AssetDatabase.AssetPathToGUID(dependency));

                    if (role == SceneObjectRole.WorldSettings)
                    {
                        summary.HasWorldSettings = true;
                    }
                    else if (role == SceneObjectRole.GameMode && summary.PlacedGameMode.Length == 0)
                    {
                        summary.PlacedGameMode = dependency;
                    }
                }

                if (summary.HasWorldSettings && summary.PlacedGameMode.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    summary.OverrideGameMode = summary.PlacedGameMode;

                    summary.PlacedGameMode = string.Empty;
                }
            }

            _levelSummaries[scenePath] = new CachedLevel(stamp, summary, guids);

            return summary;
        }

        /// <summary>
        /// The rows of the levels list: Build Settings first, then the other levels. Cached until assets, Build Settings
        /// or the default GameMode change, so the settings page does not scan anything on repaint.
        /// </summary>
        public static IReadOnlyList<LevelRow> LevelRows()
        {
            var build = EditorBuildSettings.scenes.Select(scene => new KeyValuePair<string, bool>(scene.path, scene.enabled)).ToList();

            var settings = Settings;

            var defaultGameMode = settings != null && settings.DefaultGameMode != null ? AssetDatabase.GetAssetPath(settings.DefaultGameMode) : string.Empty;

            var key = Version + "|" + defaultGameMode + "|" + string.Join(";", build.Select(scene => (scene.Value ? "+" : "-") + scene.Key));

            if (_levelRows != null && _levelRowsKey == key)
            {
                return _levelRows;
            }

            _levelRows = LevelList.Rows(build, ProjectScenes, LevelSummaryOf, File.Exists, defaultGameMode);

            _levelRowsKey = key;

            return _levelRows;
        }

        /// <summary>
        /// Assets changed: what a changed script or prefab is (a GameMode, a Player Start...) is read again, with the levels
        /// that use it. A move or a deletion changes paths the summaries hold, so everything is read again then.
        /// </summary>
        internal static void LevelAssetsChanged_Internal(IEnumerable<string> imported, bool movedOrDeleted)
        {
            if (movedOrDeleted)
            {
                ClearLevelCaches_Internal();

                return;
            }

            foreach (var path in imported ?? Array.Empty<string>())
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    ForgetGuid_Internal(AssetDatabase.AssetPathToGUID(path));
                }
            }
        }

        internal static void ClearLevelCaches_Internal()
        {
            _roles.Clear();

            _prefabOverrides.Clear();

            _levelSummaries.Clear();
        }

        private static void ForgetGuid_Internal(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            _roles.Remove(guid);

            _prefabOverrides.Remove(guid);

            foreach (var scene in _levelSummaries.Where(pair => pair.Value.Guids.Contains(guid)).Select(pair => pair.Key).ToList())
            {
                _levelSummaries.Remove(scene);
            }
        }

        /// <summary>
        /// The GameMode Override a World Settings prefab has itself: a path, <see cref="SceneFileScan.InThisLevel"/> for an
        /// object inside that prefab, or empty.
        /// </summary>
        private static string PrefabOverrideOf_Internal(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return string.Empty;
            }

            if (_prefabOverrides.TryGetValue(guid, out var known))
            {
                return known;
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);

            var root = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;

            var worldSettings = root != null ? root.GetComponent<WorldSettings>() : null;

            var gameMode = worldSettings != null ? worldSettings.GameModeOverride : null;

            var overridePath = gameMode != null ? AssetDatabase.GetAssetPath(gameMode) : string.Empty;

            var result = gameMode == null ? string.Empty : string.IsNullOrEmpty(overridePath) || overridePath == path ? SceneFileScan.InThisLevel : overridePath;

            _prefabOverrides[guid] = result;

            return result;
        }

        private static SceneObjectRole RoleOf_Internal(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return SceneObjectRole.Other;
            }

            if (_roles.TryGetValue(guid, out var known))
            {
                return known;
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);

            var role = SceneObjectRole.Other;

            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                role = RoleOfType_Internal(AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass());
            }
            else if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (root != null)
                {
                    role = root.GetComponent<GameMode>() != null ? SceneObjectRole.GameMode :
                           root.GetComponent<WorldSettings>() != null ? SceneObjectRole.WorldSettings :
                           root.GetComponent<PlayerStart>() != null ? SceneObjectRole.PlayerStart : SceneObjectRole.Other;
                }
            }

            _roles[guid] = role;

            return role;
        }

        private static SceneObjectRole RoleOfType_Internal(Type type)
        {
            if (type == null)
            {
                return SceneObjectRole.Other;
            }

            if (typeof(GameMode).IsAssignableFrom(type))
            {
                return SceneObjectRole.GameMode;
            }

            if (typeof(WorldSettings).IsAssignableFrom(type))
            {
                return SceneObjectRole.WorldSettings;
            }

            return typeof(PlayerStart).IsAssignableFrom(type) ? SceneObjectRole.PlayerStart : SceneObjectRole.Other;
        }

        private sealed class CachedLevel
        {
            public CachedLevel(long stamp, LevelSummary summary, HashSet<string> guids)
            {
                Stamp = stamp;

                Summary = summary;

                Guids = guids;
            }

            public long Stamp { get; }

            public LevelSummary Summary { get; }

            public HashSet<string> Guids { get; }
        }
    }
}
