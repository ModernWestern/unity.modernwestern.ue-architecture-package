using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// An assembly reference a new script needs and the asmdef of its folder lacks.
    /// </summary>
    internal readonly struct MissingReference
    {
        public readonly string AsmdefPath;

        public readonly string Assembly;

        public readonly string Problem;

        public MissingReference(string asmdefPath, string assembly, string problem)
        {
            AsmdefPath = asmdefPath;

            Assembly = assembly;

            Problem = problem;
        }
    }

    /// <summary>
    /// Adds assembly references to an asmdef, the fix for "Game.asmdef does not reference UEArchitecture.Runtime". The
    /// JSON patch is pure (tested without files): it keeps the file's style, "GUID:..." entries when the asmdef uses
    /// them (Unity's default) or names otherwise, and touches nothing else. Asked for in the UI; headless only with
    /// fix-references=true.
    /// </summary>
    internal static class AsmdefReferences
    {
        private static readonly Regex ReferencesKey = new("\"references\"\\s*:\\s*\\[", RegexOptions.Compiled);

        private static readonly Regex Entry = new("\"([^\"]*)\"", RegexOptions.Compiled);

        /// <summary>
        /// The asmdef JSON with <paramref name="assemblies"/> added to its "references" (those it already has, by name or
        /// by GUID, are skipped). <paramref name="guidOf"/> gives an assembly's asmdef GUID (empty when unknown: then the
        /// name is written).
        /// </summary>
        public static string Add(string asmdefJson, IEnumerable<string> assemblies, Func<string, string> guidOf)
        {
            var json = asmdefJson ?? string.Empty;

            var wanted = (assemblies ?? Array.Empty<string>()).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList();

            if (wanted.Count == 0)
            {
                return json;
            }

            var newline = json.Contains("\r\n") ? "\r\n" : "\n";

            var key = ReferencesKey.Match(json);

            if (!key.Success)
            {
                // No "references" yet: add it after the opening brace, in the GUID style Unity writes by default.
                var brace = json.IndexOf('{');

                if (brace < 0)
                {
                    return json;
                }

                var items = wanted.Select(name => Format_Internal(name, guidOf, true)).ToList();

                var insert = newline + "    \"references\": [" + newline + string.Join("," + newline, items.Select(item => "        \"" + item + "\"")) + newline + "    ],";

                return json.Substring(0, brace + 1) + insert + json.Substring(brace + 1);
            }

            var open = key.Index + key.Length - 1;

            var close = json.IndexOf(']', open);

            if (close < 0)
            {
                return json;
            }

            var inner = json.Substring(open + 1, close - open - 1);

            var existing = Entry.Matches(inner).Cast<Match>().Select(match => match.Groups[1].Value).ToList();

            var useGuids = existing.Count == 0 || existing.Any(item => item.StartsWith("GUID:", StringComparison.Ordinal));

            var added = new List<string>();

            foreach (var name in wanted)
            {
                var guid = guidOf?.Invoke(name) ?? string.Empty;

                if (existing.Contains(name) || guid.Length > 0 && existing.Contains("GUID:" + guid))
                {
                    continue;
                }

                added.Add(Format_Internal(name, guidOf, useGuids));
            }

            if (added.Count == 0)
            {
                return json;
            }

            var lineStart = json.LastIndexOf('\n', key.Index) + 1;

            var indent = json.Substring(lineStart, key.Index - lineStart);

            var itemIndent = indent + "    ";

            var all = existing.Concat(added).Select(item => itemIndent + "\"" + item + "\"");

            var array = "[" + newline + string.Join("," + newline, all) + newline + indent + "]";

            return json.Substring(0, open) + array + json.Substring(close + 1);
        }

        /// <summary>
        /// Writes the missing references into their asmdef files (no refresh: the script writer refreshes once after
        /// writing the scripts, so there is one compile). Returns what changed.
        /// </summary>
        public static ActionResult Apply(IEnumerable<MissingReference> missing)
        {
            var result = new ActionResult("fix-references");

            foreach (var group in (missing ?? Array.Empty<MissingReference>()).GroupBy(item => item.AsmdefPath))
            {
                if (string.IsNullOrEmpty(group.Key) || !File.Exists(group.Key))
                {
                    result.AddProblem($"Cannot find the asmdef {group.Key} to add {string.Join(", ", group.Select(item => item.Assembly))}.");

                    continue;
                }

                if (!group.Key.StartsWith("Assets/", StringComparison.Ordinal) && !ScriptWriter.IsEmbeddedPackageFolder(GeneratorNaming.FolderOf(group.Key)))
                {
                    result.AddProblem($"{group.Key} is in a read-only package: add {string.Join(", ", group.Select(item => item.Assembly))} to it by hand, or pick another folder.");

                    continue;
                }

                var before = File.ReadAllText(group.Key);

                var after = Add(before, group.Select(item => item.Assembly), GuidOf);

                if (after == before)
                {
                    continue;
                }

                File.WriteAllText(group.Key, after, new UTF8Encoding(false));

                result.AddChanged($"{group.Key}: references (+ {string.Join(", ", group.Select(item => item.Assembly))})");
            }

            return result;
        }

        /// <summary>
        /// The GUID of an assembly's asmdef, or empty (an assembly without an asmdef, or unknown).
        /// </summary>
        public static string GuidOf(string assembly)
        {
            var path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly);

            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path) ?? string.Empty;
        }

        private static string Format_Internal(string name, Func<string, string> guidOf, bool useGuids)
        {
            var guid = useGuids ? guidOf?.Invoke(name) ?? string.Empty : string.Empty;

            return guid.Length > 0 ? "GUID:" + guid : name;
        }
    }
}
