using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What a script or prefab used in a level file is, for the levels list.
    /// </summary>
    internal enum SceneObjectRole
    {
        Other,
        GameMode,
        WorldSettings,
        PlayerStart
    }

    /// <summary>
    /// What a saved level file says about the framework, read without opening it: a GameMode placed in it, the GameMode
    /// Override of its World Settings, and how many Player Starts it has. Approximate: objects inside nested prefabs and
    /// unsaved changes are not seen.
    /// </summary>
    internal sealed class LevelSummary
    {
        /// <summary>
        /// The GameMode placed in the level (a prefab or a script path), or empty.
        /// </summary>
        public string PlacedGameMode = string.Empty;

        /// <summary>
        /// The World Settings GameMode Override (a prefab path, or "an object of this level"), or empty.
        /// </summary>
        public string OverrideGameMode = string.Empty;

        public bool HasWorldSettings;

        /// <summary>
        /// How many Player Starts it has; -1 when the file could not be read as text.
        /// </summary>
        public int PlayerStarts;

        /// <summary>
        /// True when read from the level's text; false when only its list of used assets was available (a binary file).
        /// </summary>
        public bool FromText = true;
    }

    /// <summary>
    /// Reads a level's YAML for the levels list (pure: the roles of scripts and prefabs come from outside; tested).
    /// </summary>
    internal static class SceneFileScan
    {
        private static readonly Regex Block = new(@"^--- !u!(\d+) &-?\d+( stripped)?", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex Script = new(@"m_Script: \{fileID: -?\d+, guid: ([0-9a-fA-F]{32})", RegexOptions.Compiled);

        private static readonly Regex SourcePrefab = new(@"m_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-fA-F]{32})", RegexOptions.Compiled);

        private static readonly Regex Override = new(@"gameModeOverride: \{fileID: (-?\d+)(?:, guid: ([0-9a-fA-F]{32}))?", RegexOptions.Compiled);

        private static readonly Regex OverrideModification = new(@"propertyPath: gameModeOverride\s+value:[^\n]*\n\s+objectReference: \{fileID: (-?\d+)(?:, guid: ([0-9a-fA-F]{32}))?",
                                                                 RegexOptions.Compiled);

        public const string InThisLevel = "an object of this level";

        /// <summary>
        /// The summary of a level from its text. <paramref name="roleOf"/> says what the script or prefab of a GUID is;
        /// <paramref name="pathOf"/> gives an asset's path from its GUID; <paramref name="prefabOverrideOf"/> gives the
        /// GameMode Override a World Settings prefab has itself (a path, <see cref="InThisLevel"/>, or empty), used when
        /// the level does not override it on its copy. Text that is not YAML gives an empty summary with
        /// <see cref="LevelSummary.FromText"/> false.
        /// </summary>
        public static LevelSummary Parse(string text, Func<string, SceneObjectRole> roleOf, Func<string, string> pathOf, Func<string, string> prefabOverrideOf = null)
        {
            var summary = new LevelSummary();

            if (string.IsNullOrEmpty(text) || !text.StartsWith("%YAML", StringComparison.Ordinal))
            {
                summary.FromText = false;

                summary.PlayerStarts = -1;

                return summary;
            }

            var blocks = Block.Matches(text);

            for (var i = 0; i < blocks.Count; i++)
            {
                var start = blocks[i].Index;

                var end = i + 1 < blocks.Count ? blocks[i + 1].Index : text.Length;

                var body = text.Substring(start, end - start);

                var classId = blocks[i].Groups[1].Value;

                var stripped = blocks[i].Groups[2].Success;

                if (classId == "114" && !stripped)
                {
                    var script = Script.Match(body);

                    if (script.Success)
                    {
                        Count_Internal(summary, roleOf(script.Groups[1].Value.ToLowerInvariant()), pathOf(script.Groups[1].Value.ToLowerInvariant()));
                    }

                    ReadOverride_Internal(summary, Override.Match(body), pathOf);
                }
                else if (classId == "1001")
                {
                    var source = SourcePrefab.Match(body);

                    var role = SceneObjectRole.Other;

                    var guid = source.Success ? source.Groups[1].Value.ToLowerInvariant() : string.Empty;

                    if (source.Success)
                    {
                        role = roleOf(guid);

                        Count_Internal(summary, role, pathOf(guid));
                    }

                    var modification = OverrideModification.Match(body);

                    if (modification.Success)
                    {
                        // The level's own value wins, even a cleared one.
                        ReadOverride_Internal(summary, modification, pathOf);
                    }
                    else if (role == SceneObjectRole.WorldSettings && prefabOverrideOf != null)
                    {
                        var own = prefabOverrideOf(guid);

                        if (!string.IsNullOrEmpty(own))
                        {
                            summary.OverrideGameMode = own;
                        }
                    }
                }
            }

            return summary;
        }

        private static void Count_Internal(LevelSummary summary, SceneObjectRole role, string path)
        {
            switch (role)
            {
                case SceneObjectRole.GameMode:
                    if (summary.PlacedGameMode.Length == 0)
                    {
                        summary.PlacedGameMode = path ?? string.Empty;
                    }

                    break;

                case SceneObjectRole.WorldSettings:
                    summary.HasWorldSettings = true;

                    break;

                case SceneObjectRole.PlayerStart:
                    summary.PlayerStarts++;

                    break;
            }
        }

        private static void ReadOverride_Internal(LevelSummary summary, Match match, Func<string, string> pathOf)
        {
            if (!match.Success || match.Groups[1].Value == "0")
            {
                return;
            }

            summary.OverrideGameMode = match.Groups[2].Success ? pathOf(match.Groups[2].Value.ToLowerInvariant()) ?? string.Empty : InThisLevel;
        }
    }

    /// <summary>
    /// One row of the levels list in Project Settings > UE Architecture.
    /// </summary>
    internal sealed class LevelRow
    {
        public string Path = string.Empty;

        public BuildScenes.Status Build = BuildScenes.Status.Missing;

        /// <summary>
        /// The index SceneManager.LoadScene uses (enabled build scenes only), or -1.
        /// </summary>
        public int BuildIndex = -1;

        /// <summary>
        /// Its place in the Build Settings list, enabled or not, or -1.
        /// </summary>
        public int BuildPosition = -1;

        /// <summary>
        /// True for a Build Settings entry whose file is gone.
        /// </summary>
        public bool FileMissing;

        public GameModeSource Source = GameModeSource.None;

        /// <summary>
        /// The GameMode the level uses ("ArenaGameMode.prefab"), or empty.
        /// </summary>
        public string GameMode = string.Empty;

        public bool HasWorldSettings;

        public int PlayerStarts;

        public bool Approximate = true;

        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        public bool CanAddToBuild => !FileMissing && Build != BuildScenes.Status.InBuild;

        public bool CanBeFirst => !FileMissing && BuildIndex != 0;

        /// <summary>
        /// True when World Settings has a GameMode Override (even when a placed GameMode wins over it).
        /// </summary>
        public bool HasOverride;

        /// <summary>
        /// "Assets/Scenes/Boss.unity  build 1  GameMode: BossMode.prefab (World Settings override)  World Settings: yes  starts: 2".
        /// </summary>
        public string Describe()
        {
            var build = FileMissing ? "in Build Settings, file missing" : BuildIndex >= 0 ? $"build {BuildIndex}" : Build == BuildScenes.Status.Disabled ? "disabled in build" : "not in build";

            var gameMode = Source == GameModeSource.None ? "none" : $"{GameMode} ({GameModeInfo.Describe(Source)})";

            var starts = PlayerStarts < 0 ? "?" : PlayerStarts.ToString();

            return $"{Path}  {build}  GameMode: {gameMode}  World Settings: {(HasWorldSettings ? "yes" : "no")}  starts: {starts}";
        }
    }

    /// <summary>
    /// The levels list: the Build Settings scenes in their order, then the other levels of the project by path (pure:
    /// the build list, the levels and their summaries come from outside; tested).
    /// </summary>
    internal static class LevelList
    {
        public static List<LevelRow> Rows(IReadOnlyList<KeyValuePair<string, bool>> build, IEnumerable<string> projectScenes, Func<string, LevelSummary> summaryOf,
                                          Func<string, bool> exists, string defaultGameMode)
        {
            var rows = new List<LevelRow>();

            var seen = new HashSet<string>(StringComparer.Ordinal);

            var enabledIndex = 0;

            for (var i = 0; i < (build?.Count ?? 0); i++)
            {
                var path = build[i].Key;

                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                {
                    continue;
                }

                var row = new LevelRow { Path = path, BuildPosition = i, FileMissing = !exists(path) };

                row.Build = build[i].Value ? BuildScenes.Status.InBuild : BuildScenes.Status.Disabled;

                if (build[i].Value && !row.FileMissing)
                {
                    row.BuildIndex = enabledIndex++;
                }

                Fill_Internal(row, row.FileMissing ? null : summaryOf(path), defaultGameMode);

                rows.Add(row);
            }

            foreach (var path in (projectScenes ?? Array.Empty<string>()).Where(path => !seen.Contains(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var row = new LevelRow { Path = path, Build = BuildScenes.Status.Missing };

                Fill_Internal(row, summaryOf(path), defaultGameMode);

                rows.Add(row);
            }

            return rows;
        }

        private static void Fill_Internal(LevelRow row, LevelSummary summary, string defaultGameMode)
        {
            if (summary == null)
            {
                row.PlayerStarts = -1;

                return;
            }

            row.HasWorldSettings = summary.HasWorldSettings;

            row.PlayerStarts = summary.PlayerStarts;

            row.HasOverride = summary.OverrideGameMode.Length > 0;

            if (summary.PlacedGameMode.Length > 0)
            {
                row.Source = GameModeSource.Placed;

                row.GameMode = Path.GetFileName(summary.PlacedGameMode);
            }
            else if (summary.OverrideGameMode.Length > 0)
            {
                row.Source = GameModeSource.WorldSettings;

                row.GameMode = summary.OverrideGameMode == SceneFileScan.InThisLevel ? summary.OverrideGameMode : Path.GetFileName(summary.OverrideGameMode);
            }
            else if (!string.IsNullOrEmpty(defaultGameMode))
            {
                row.Source = GameModeSource.ProjectDefault;

                row.GameMode = Path.GetFileName(defaultGameMode);
            }
        }
    }

    /// <summary>
    /// One row of the subsystems list: the class, Game or World, and when it ticks.
    /// </summary>
    internal readonly struct SubsystemRow
    {
        public readonly Type Type;

        public readonly bool World;

        /// <summary>
        /// "Tick: LateUpdate, order 10, unscaled", or null for a subsystem that does not tick.
        /// </summary>
        public readonly string Tick;

        public SubsystemRow(Type type, bool world, string tick)
        {
            Type = type;

            World = world;

            Tick = tick;
        }

        public string Kind => World ? "World" : "Game";
    }

    /// <summary>
    /// The subsystems list of Project Settings > UE Architecture (the model, tested).
    /// </summary>
    internal static class SubsystemList
    {
        private static List<SubsystemRow> _project;

        /// <summary>
        /// The project's subsystems (not abstract, not generic, not in a test assembly), Game first, then by name. Cached:
        /// the classes only change with a compile, which starts a new domain.
        /// </summary>
        public static IReadOnlyList<SubsystemRow> Project()
        {
            return _project ??= Rows(UnityEditor.TypeCache.GetTypesDerivedFrom<Subsystem>().Where(type => !UEArchitectureSettingsProvider.IsTestType_Internal(type)));
        }

        public static List<SubsystemRow> Rows(IEnumerable<Type> types)
        {
            return (types ?? Type.EmptyTypes).Where(type => type != null && !type.IsAbstract && !type.ContainsGenericParameters &&
                                                            (typeof(GameInstanceSubsystem).IsAssignableFrom(type) || typeof(WorldSubsystem).IsAssignableFrom(type)))
                                             .Distinct()
                                             .Select(type => new SubsystemRow(type, typeof(WorldSubsystem).IsAssignableFrom(type), TickText(type)))
                                             .OrderBy(row => row.World)
                                             .ThenBy(row => row.Type.Name, StringComparer.OrdinalIgnoreCase)
                                             .ToList();
        }

        /// <summary>
        /// "Tick: Update" for a tickable subsystem without [Tick]; "Tick: LateUpdate, order 10, unscaled" with it; null
        /// when the subsystem does not tick (no ITickable).
        /// </summary>
        public static string TickText(Type type)
        {
            if (type == null || !typeof(ITickable).IsAssignableFrom(type))
            {
                return null;
            }

            var tick = (TickAttribute)Attribute.GetCustomAttribute(type, typeof(TickAttribute), true);

            var group = tick?.Group ?? TickGroup.Update;

            var text = "Tick: " + group;

            if (tick != null && tick.Order != 0)
            {
                text += ", order " + tick.Order;
            }

            if (tick != null && tick.Unscaled)
            {
                text += ", unscaled";
            }

            return text;
        }
    }
}
