using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where a script is used: the prefabs that have its component (themselves, or through a nested prefab or a variant)
    /// and the scenes that have it (on their own objects, or through one of those prefabs).
    /// </summary>
    internal sealed class ScriptUsages
    {
        public readonly List<string> Scenes = new();

        public readonly List<string> Prefabs = new();

        /// <summary>
        /// For a usage through another prefab: the prefab it comes from.
        /// </summary>
        public readonly Dictionary<string, string> Through = new(StringComparer.Ordinal);

        /// <summary>
        /// True when the search was stopped before the end (the lists are partial).
        /// </summary>
        public bool Cancelled;

        public int Count => Scenes.Count + Prefabs.Count;

        /// <summary>
        /// "Used in 2 scenes, 1 prefab".
        /// </summary>
        public string Summary()
        {
            if (Count == 0)
            {
                return Cancelled ? "Search stopped: no usage found so far." : "Not used in any scene or prefab.";
            }

            var text = $"Used in {Plural_Internal(Scenes.Count, "scene")}, {Plural_Internal(Prefabs.Count, "prefab")}";

            return Cancelled ? text + " (search stopped before the end)." : text + ".";
        }

        public IEnumerable<string> Lines()
        {
            foreach (var path in Scenes.Concat(Prefabs))
            {
                yield return Through.TryGetValue(path, out var via) ? $"{path} (through {via})" : path;
            }
        }

        private static string Plural_Internal(int count, string word)
        {
            return count == 1 ? "1 " + word : $"{count} {word}s";
        }
    }

    /// <summary>
    /// Find Usages: which scenes and prefabs use a script, read from AssetDatabase.GetDependencies (the files are never
    /// opened). Shows a progress bar that can be cancelled in the Editor.
    /// </summary>
    internal static class FrameworkUsages
    {
        /// <summary>
        /// The scenes and prefabs under <paramref name="folders"/> (Assets/ when null) that use <paramref name="script"/>.
        /// </summary>
        public static ScriptUsages Find(MonoScript script, string[] folders = null, bool showProgress = true)
        {
            var path = AssetDatabase.GetAssetPath(script);

            if (string.IsNullOrEmpty(path))
            {
                return new ScriptUsages();
            }

            folders ??= new[] { "Assets" };

            var scenes = AssetDatabase.FindAssets("t:Scene", folders).Select(AssetDatabase.GUIDToAssetPath).Where(scene => scene.EndsWith(".unity", StringComparison.Ordinal)).Distinct();

            var prefabs = AssetDatabase.FindAssets("t:Prefab", folders).Select(AssetDatabase.GUIDToAssetPath).Where(prefab => prefab.EndsWith(".prefab", StringComparison.Ordinal)).Distinct();

            var progress = showProgress && !Application.isBatchMode;

            try
            {
                return Find(path, scenes.ToList(), prefabs.ToList(), asset => AssetDatabase.GetDependencies(asset, false),
                            progress ? (Func<int, int, string, bool>)((done, total, asset) =>
                                EditorUtility.DisplayCancelableProgressBar("Find Usages", $"{System.IO.Path.GetFileName(path)}: {asset}", total == 0 ? 1f : done / (float)total)) : null);
            }
            finally
            {
                if (progress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        /// <summary>
        /// The usages of the script at <paramref name="scriptPath"/> among <paramref name="scenes"/> and
        /// <paramref name="prefabs"/>, with each file's direct dependencies from <paramref name="dependencies"/>. Pure:
        /// <paramref name="progress"/> (done, total, current file) returns true to stop.
        /// </summary>
        public static ScriptUsages Find(string scriptPath, IReadOnlyList<string> scenes, IReadOnlyList<string> prefabs, Func<string, string[]> dependencies,
                                        Func<int, int, string, bool> progress = null)
        {
            var usages = new ScriptUsages();

            var total = scenes.Count + prefabs.Count;

            var done = 0;

            var direct = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var prefab in prefabs)
            {
                if (progress != null && progress(done, total, prefab))
                {
                    usages.Cancelled = true;

                    return usages;
                }

                direct[prefab] = dependencies(prefab) ?? Array.Empty<string>();

                done++;
            }

            // Prefabs with the component, then the prefabs that nest them or are their variants, until nothing is added.
            var withScript = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var prefab in prefabs.Where(prefab => direct[prefab].Contains(scriptPath)))
            {
                withScript[prefab] = null;
            }

            var grew = true;

            while (grew)
            {
                grew = false;

                foreach (var prefab in prefabs)
                {
                    if (withScript.ContainsKey(prefab))
                    {
                        continue;
                    }

                    var via = direct[prefab].FirstOrDefault(withScript.ContainsKey);

                    if (via != null)
                    {
                        withScript[prefab] = via;

                        grew = true;
                    }
                }
            }

            foreach (var prefab in prefabs.Where(withScript.ContainsKey))
            {
                usages.Prefabs.Add(prefab);

                if (withScript[prefab] != null)
                {
                    usages.Through[prefab] = withScript[prefab];
                }
            }

            foreach (var scene in scenes)
            {
                if (progress != null && progress(done, total, scene))
                {
                    usages.Cancelled = true;

                    return usages;
                }

                done++;

                var sceneDependencies = dependencies(scene) ?? Array.Empty<string>();

                if (sceneDependencies.Contains(scriptPath))
                {
                    usages.Scenes.Add(scene);

                    continue;
                }

                var via = sceneDependencies.FirstOrDefault(withScript.ContainsKey);

                if (via != null)
                {
                    usages.Scenes.Add(scene);

                    usages.Through[scene] = via;
                }
            }

            return usages;
        }
    }
}
