using System;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Globalization;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where a pending step stands.
    /// </summary>
    internal enum PendingStatus
    {
        /// <summary>
        /// Waits for its scripts to compile (or for the steps it depends on).
        /// </summary>
        Waiting,

        /// <summary>
        /// Needs the user: a compile error, a class that is not where it should be, a deleted file...
        /// </summary>
        Blocked,

        /// <summary>
        /// Finished.
        /// </summary>
        Done
    }

    /// <summary>
    /// One key and value of a pending step's arguments.
    /// </summary>
    [Serializable]
    internal struct PendingArg
    {
        public string key;

        public string value;
    }

    /// <summary>
    /// An object a pending step will change once it runs, named by a GlobalObjectId string, and the field to fill.
    /// </summary>
    [Serializable]
    internal struct PendingTarget
    {
        public string objectKey;

        public string propertyPath;
    }

    /// <summary>
    /// Work that finishes after the next script compile, e.g. "build ArenaPawn.prefab and put it in Arena Mode's Default
    /// Pawn once ArenaPawn.cs compiles". Stored as JSON in Library/, so it survives domain reloads, Editor restarts and
    /// separate command-line runs.
    /// </summary>
    [Serializable]
    internal sealed class PendingStep
    {
        public string id = string.Empty;

        public string kind = string.Empty;

        public int sequence;

        public string[] dependsOn = Array.Empty<string>();

        public string[] scriptPaths = Array.Empty<string>();

        public string[] expectedClasses = Array.Empty<string>();

        public List<PendingArg> args = new();

        public List<PendingTarget> targets = new();

        public PendingStatus status = PendingStatus.Waiting;

        public string message = string.Empty;

        public string createdUtc = string.Empty;

        public static PendingStep Create(string kind)
        {
            return new PendingStep
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 12),
                kind = kind ?? string.Empty,
                createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// When the step was made (UTC), or DateTime.MinValue when unreadable.
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return DateTime.TryParse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created)
                           ? created.ToUniversalTime()
                           : DateTime.MinValue;
            }
            set => createdUtc = value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        public string Arg(string key, string fallback = null)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg.key, key, StringComparison.Ordinal))
                {
                    return arg.value;
                }
            }

            return fallback;
        }

        public PendingStep WithArg(string key, string value)
        {
            args.RemoveAll(arg => string.Equals(arg.key, key, StringComparison.Ordinal));

            args.Add(new PendingArg { key = key, value = value ?? string.Empty });

            return this;
        }

        public PendingStep WithScript(string scriptPath, string expectedClass)
        {
            scriptPaths = scriptPaths.Append(scriptPath).ToArray();

            expectedClasses = expectedClasses.Append(expectedClass).ToArray();

            return this;
        }

        public PendingStep After(params string[] stepIds)
        {
            dependsOn = dependsOn.Concat(stepIds.Where(other => !string.IsNullOrEmpty(other))).Distinct().ToArray();

            return this;
        }

        /// <summary>
        /// "Waiting for compile: ArenaPawn.cs", for chips and lists.
        /// </summary>
        public string Describe()
        {
            var scripts = string.Join(", ", scriptPaths.Select(Path.GetFileName));

            switch (status)
            {
                case PendingStatus.Waiting:
                    return string.IsNullOrEmpty(message) ? $"Waiting for compile: {scripts}" : message;

                case PendingStatus.Blocked:
                    return string.IsNullOrEmpty(message) ? $"Blocked: {scripts}" : message;

                default:
                    return "Done";
            }
        }
    }

    /// <summary>
    /// The saved list of pending steps: a JSON file (Library/UEArchitecture/PendingSteps.json by default).
    /// </summary>
    internal sealed class PendingStepStore
    {
        public const string DefaultPath = "Library/UEArchitecture/PendingSteps.json";

        /// <summary>
        /// Steps older than this are dropped.
        /// </summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        public PendingStepStore(string path)
        {
            Path = path;
        }

        public string Path { get; }

        private IReadOnlyList<PendingStep> _snapshot = Array.Empty<PendingStep>();

        private long _snapshotStamp = -1;

        public bool Exists => File.Exists(Path);

        /// <summary>
        /// The stored steps, read again only when the file changed (for the UI; do not modify them).
        /// </summary>
        public IReadOnlyList<PendingStep> Snapshot()
        {
            var stamp = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path).Ticks : 0L;

            if (stamp != _snapshotStamp)
            {
                _snapshot = stamp == 0L ? Array.Empty<PendingStep>() : Load();

                _snapshotStamp = stamp;
            }

            return _snapshot;
        }

        public List<PendingStep> Load()
        {
            if (!File.Exists(Path))
            {
                return new List<PendingStep>();
            }

            try
            {
                return FromJson(File.ReadAllText(Path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UE Architecture: could not read {Path} ({exception.Message}); its pending steps are dropped.");

                return new List<PendingStep>();
            }
        }

        /// <summary>
        /// Writes the steps, or deletes the file when there are none.
        /// </summary>
        public void Save(List<PendingStep> steps)
        {
            _snapshotStamp = -1;

            if (steps == null || steps.Count == 0)
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }

                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");

            File.WriteAllText(Path, ToJson(steps));
        }

        public static string ToJson(List<PendingStep> steps)
        {
            return JsonUtility.ToJson(new StoreFile { version = 1, steps = steps ?? new List<PendingStep>() }, true);
        }

        public static List<PendingStep> FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<PendingStep>();
            }

            var file = JsonUtility.FromJson<StoreFile>(json);

            return file?.steps?.Where(step => step != null && !string.IsNullOrEmpty(step.id)).ToList() ?? new List<PendingStep>();
        }

        /// <summary>
        /// Removes the steps older than <paramref name="maxAge"/> (or with an unreadable date) and returns them.
        /// </summary>
        public static List<PendingStep> Expire(List<PendingStep> steps, DateTime nowUtc, TimeSpan maxAge)
        {
            var expired = steps.Where(step => step.CreatedUtc == DateTime.MinValue || nowUtc - step.CreatedUtc > maxAge).ToList();

            steps.RemoveAll(expired.Contains);

            return expired;
        }

        /// <summary>
        /// The steps in the order they must run: every step after the steps it depends on, then by sequence. Steps caught
        /// in a dependency loop come last.
        /// </summary>
        public static List<PendingStep> Order(IEnumerable<PendingStep> steps, out List<PendingStep> inLoops)
        {
            var remaining = steps.OrderBy(step => step.sequence).ThenBy(step => step.CreatedUtc).ToList();

            var ids = new HashSet<string>(remaining.Select(step => step.id), StringComparer.Ordinal);

            var placed = new HashSet<string>(StringComparer.Ordinal);

            var ordered = new List<PendingStep>();

            var progress = true;

            while (remaining.Count > 0 && progress)
            {
                progress = false;

                for (var i = 0; i < remaining.Count; i++)
                {
                    var step = remaining[i];

                    // A dependency that is no longer stored has finished.
                    if (step.dependsOn.Any(dependency => ids.Contains(dependency) && !placed.Contains(dependency)))
                    {
                        continue;
                    }

                    ordered.Add(step);

                    placed.Add(step.id);

                    remaining.RemoveAt(i);

                    progress = true;

                    break;
                }
            }

            inLoops = remaining;

            ordered.AddRange(remaining);

            return ordered;
        }

        [Serializable]
        private sealed class StoreFile
        {
            public int version = 1;

            public List<PendingStep> steps = new();
        }
    }
}
