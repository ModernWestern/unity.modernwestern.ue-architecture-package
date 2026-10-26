using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where a tag (or a tag below it) is used: GameplayTag and GameplayTagContainer fields of prefabs and other native
    /// assets (rewritten by a rename), levels (listed, never changed: they would have to be opened one by one) and string
    /// literals in scripts (listed, never changed; the generated constants file is left out).
    /// </summary>
    internal sealed class TagUsageReport
    {
        public TagUsageReport(string tag)
        {
            Tag = tag ?? string.Empty;
        }

        public string Tag { get; }

        /// <summary>
        /// Prefabs and other assets with tag fields that hold the tag, with how many fields.
        /// </summary>
        public List<KeyValuePair<string, int>> Assets { get; } = new();

        /// <summary>
        /// Levels with tag fields (or prefab overrides of them) that hold the tag, with how many values.
        /// </summary>
        public List<KeyValuePair<string, int>> Scenes { get; } = new();

        /// <summary>
        /// Scripts with the tag in a string literal ("Status.Burning").
        /// </summary>
        public List<KeyValuePair<string, int>> Scripts { get; } = new();

        public int AssetReferences => Assets.Sum(pair => pair.Value);

        public bool IsEmpty => Assets.Count + Scenes.Count + Scripts.Count == 0;

        /// <summary>
        /// True when the search was stopped before the end: the lists are incomplete.
        /// </summary>
        public bool Cancelled { get; set; }

        /// <summary>
        /// "14 references in 6 assets; 2 levels and 1 script mention it", or "Not used by any asset, level or script.".
        /// </summary>
        public string Summary()
        {
            if (IsEmpty)
            {
                return "Not used by any asset, level or script.";
            }

            var parts = new List<string>();

            if (Assets.Count > 0)
            {
                parts.Add($"{Plural_Internal(AssetReferences, "reference")} in {Plural_Internal(Assets.Count, "asset")}");
            }

            var others = new List<string>();

            if (Scenes.Count > 0)
            {
                others.Add(Plural_Internal(Scenes.Count, "level"));
            }

            if (Scripts.Count > 0)
            {
                others.Add(Plural_Internal(Scripts.Count, "script"));
            }

            if (others.Count > 0)
            {
                parts.Add(string.Join(" and ", others) + (Scenes.Count + Scripts.Count == 1 ? " mentions it" : " mention it"));
            }

            return string.Join("; ", parts) + ".";
        }

        /// <summary>
        /// What a rename does: "Update 14 references in 6 assets; 2 levels listed, not changed; 1 script listed, not changed."
        /// </summary>
        public string RenameSummary()
        {
            var parts = new List<string>();

            parts.Add(Assets.Count > 0 ? $"Update {Plural_Internal(AssetReferences, "reference")} in {Plural_Internal(Assets.Count, "asset")}" : "No asset to update");

            if (Scenes.Count > 0)
            {
                parts.Add($"{Plural_Internal(Scenes.Count, "level")} listed, not changed");
            }

            if (Scripts.Count > 0)
            {
                parts.Add($"{Plural_Internal(Scripts.Count, "script")} listed, not changed");
            }

            return string.Join("; ", parts) + ".";
        }

        internal static string Plural_Internal(int count, string word)
        {
            return count == 1 ? $"1 {word}" : $"{count} {word}s";
        }
    }

    internal static class GameplayTagUsages
    {
        private const string TagTypeName = nameof(GameplayTag);

        private const string ContainerTypeName = nameof(GameplayTagContainer);

        private static readonly Regex YamlEscape = new(@"\\(?:x([0-9A-Fa-f]{2})|u([0-9A-Fa-f]{4})|U([0-9A-Fa-f]{8})|\r?\n[ \t]*)", RegexOptions.Compiled);

        private static readonly Regex TagListEntry = new(@"(?:^|\.)tags\.Array\.data\[\d+\]$", RegexOptions.Compiled);

        /// <summary>
        /// Finds where <paramref name="tag"/> and the tags below it are used under <paramref name="root"/> (Assets by
        /// default): every native asset (prefabs, ScriptableObjects, Animator Controllers, Timelines, signals, presets and
        /// the rest, text or binary), levels and scripts. Reads files, so it runs when asked (Rename..., Delete..., the
        /// command line), never on repaint. <paramref name="progress"/> gets how far it is (0 to 1) and the file, and
        /// returns true to stop: the report is then <see cref="TagUsageReport.Cancelled"/> and incomplete.
        /// </summary>
        public static TagUsageReport Find(string tag, string root = "Assets", Func<float, string, bool> progress = null)
        {
            var report = new TagUsageReport(tag);

            if (string.IsNullOrEmpty(tag) || !Directory.Exists(root))
            {
                return report;
            }

            // Folders ending in '~' and hidden ones are not imported: nothing there is an asset.
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                                 .Select(file => file.Replace('\\', '/'))
                                 .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                                 .Where(path => !path.Split('/').Any(part => part.EndsWith("~", StringComparison.Ordinal) || part.StartsWith(".", StringComparison.Ordinal)))
                                 .ToList();

            var tagBytes = Encoding.UTF8.GetBytes(tag);

            for (var i = 0; i < files.Count; i++)
            {
                var path = files[i];

                if (progress != null && progress((float)i / files.Count, path))
                {
                    report.Cancelled = true;

                    return report;
                }

                try
                {
                    Scan_Internal(report, path, tag, tagBytes);
                }
                catch (IOException)
                {
                    // A file that cannot be read (locked, gone since the listing) is skipped.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return report;
        }

        /// <summary>
        /// <see cref="Find"/> with a progress bar in the Editor (none in batch mode). With <paramref name="cancelable"/>,
        /// Cancel stops it and the result is null.
        /// </summary>
        public static TagUsageReport FindWithProgress(string tag, bool cancelable)
        {
            if (Application.isBatchMode)
            {
                return Find(tag);
            }

            var title = $"Finding where \"{tag}\" is used";

            var shown = 0.0;

            try
            {
                var report = Find(tag, "Assets", (done, path) =>
                {
                    // The bar is redrawn a few times a second, not once per file.
                    if (EditorApplication.timeSinceStartup - shown < 0.1)
                    {
                        return false;
                    }

                    shown = EditorApplication.timeSinceStartup;

                    if (cancelable)
                    {
                        return EditorUtility.DisplayCancelableProgressBar(title, path, done);
                    }

                    EditorUtility.DisplayProgressBar(title, path, done);

                    return false;
                });

                return report.Cancelled ? null : report;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Scan_Internal(TagUsageReport report, string path, string tag, byte[] tagBytes)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            switch (extension)
            {
                case ".cs":
                {
                    var text = File.ReadAllText(path);

                    // The generated constants name every tag: they are made again, never edited by hand.
                    var count = GameplayTagConstants.IsGenerated(text) ? 0 : CountLiterals(text, tag);

                    if (count > 0)
                    {
                        report.Scripts.Add(new KeyValuePair<string, int>(path, count));
                    }

                    return;
                }

                case ".unity":
                {
                    var text = File.ReadAllText(path);

                    var count = IsText_Internal(text) ? CountTagValues(DecodeYaml(text), tag) : CountMentions(text, tag);

                    if (count > 0)
                    {
                        report.Scenes.Add(new KeyValuePair<string, int>(path, count));
                    }

                    return;
                }

                default:
                {
                    if (!MayHold_Internal(path, tag, tagBytes))
                    {
                        return;
                    }

                    var count = Visit(path, tag, null);

                    if (count > 0)
                    {
                        report.Assets.Add(new KeyValuePair<string, int>(path, count));
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Renames the tag fields of one asset that hold <paramref name="from"/> or a tag below it (Undo-able, through
        /// SerializedObject). Values that the asset only inherits from its base prefab are left to that prefab. Returns how
        /// many fields changed.
        /// </summary>
        public static int Rewrite(ScaffoldOp op, string path, string from, string to)
        {
            return Visit(path, from, (owner, property) =>
            {
                var renamed = GameplayTagEdits.RenameValue(property.stringValue, from, to);

                if (renamed == null)
                {
                    return false;
                }

                property.stringValue = renamed;

                op.Touch(owner);

                return true;
            }, to);
        }

        /// <summary>
        /// How many times a file's text names the tag or a tag below it as a whole name: "Status" counts in "Status" and
        /// "Status.Burning", not in "StatusEffect" or "Old.Status".
        /// </summary>
        public static int CountMentions(string text, string tag)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tag))
            {
                return 0;
            }

            return Regex.Matches(text, @"(?<![\w.])" + Regex.Escape(tag) + @"(?:\.\w+)*(?![\w.])").Count;
        }

        /// <summary>
        /// How many C# string literals are exactly the tag or a tag below it: "Status.Burning".
        /// </summary>
        public static int CountLiterals(string text, string tag)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tag))
            {
                return 0;
            }

            return Regex.Matches(text, "\"" + Regex.Escape(tag) + @"(?:\.\w+)*""").Count;
        }

        /// <summary>
        /// Unity's YAML with its escapes decoded: a name with letters outside ASCII is saved as "Estado.Quemándose",
        /// and a long string can go on over a line that ends in '\'.
        /// </summary>
        public static string DecodeYaml(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
            {
                return text ?? string.Empty;
            }

            return YamlEscape.Replace(text, match =>
            {
                if (match.Groups[1].Success || match.Groups[2].Success)
                {
                    // \x and \u are one UTF-16 unit each; a character outside the BMP comes as two \u in a row.
                    return ((char)Convert.ToInt32(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, 16)).ToString();
                }

                if (match.Groups[3].Success)
                {
                    var code = Convert.ToInt32(match.Groups[3].Value, 16);

                    return code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF) ? char.ConvertFromUtf32(code) : match.Value;
                }

                // An escaped line break joins the two lines.
                return string.Empty;
            });
        }

        /// <summary>
        /// How many tag values of a level's YAML (decoded, see <see cref="DecodeYaml"/>) are the tag or below it: the
        /// "name:" of GameplayTag fields, the entries of GameplayTagContainer "tags:" lists, and the values of prefab
        /// overrides whose property path ends in ".name" or "tags.Array.data[n]". Other text (a GameObject called
        /// "Team") does not count.
        /// </summary>
        public static int CountTagValues(string yaml, string tag)
        {
            if (string.IsNullOrEmpty(yaml) || string.IsNullOrEmpty(tag))
            {
                return 0;
            }

            var count = 0;

            var listIndent = -1;

            string propertyPath = null;

            foreach (var raw in yaml.Split('\n'))
            {
                var line = raw.TrimEnd('\r');

                var trimmed = line.TrimStart(' ');

                var indent = line.Length - trimmed.Length;

                if (listIndent >= 0)
                {
                    // Unity writes the entries of a list at the indent of its key.
                    if (trimmed.StartsWith("- ", StringComparison.Ordinal) && (indent == listIndent || indent == listIndent + 2))
                    {
                        count += IsTagValue_Internal(trimmed.Substring(2), tag) ? 1 : 0;

                        continue;
                    }

                    listIndent = -1;
                }

                var entry = trimmed.StartsWith("- ", StringComparison.Ordinal) ? trimmed.Substring(2) : trimmed;

                if (entry == "tags:")
                {
                    listIndent = indent;
                }
                else if (entry.StartsWith("name: ", StringComparison.Ordinal))
                {
                    count += IsTagValue_Internal(entry.Substring(6), tag) ? 1 : 0;
                }
                else if (entry.StartsWith("propertyPath: ", StringComparison.Ordinal))
                {
                    propertyPath = entry.Substring(14).Trim();
                }
                else if (entry.StartsWith("value: ", StringComparison.Ordinal) && propertyPath != null)
                {
                    if (IsTagPath_Internal(propertyPath))
                    {
                        count += IsTagValue_Internal(entry.Substring(7), tag) ? 1 : 0;
                    }

                    propertyPath = null;
                }
            }

            return count;
        }

        /// <summary>
        /// Goes over every tag value of the objects of an asset (GameplayTag fields, GameplayTagContainer entries) that is
        /// <paramref name="tag"/> or below it, and counts them; with <paramref name="edit"/>, changes them and saves.
        /// <paramref name="renamedTo"/> drops container entries a rename made twice.
        /// </summary>
        private static int Visit(string path, string tag, Func<Object, SerializedProperty, bool> edit, string renamedTo = null)
        {
            var count = 0;

            foreach (var owner in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                // Missing scripts load as null; GameObjects and Transforms hold no tags.
                if (owner == null || owner is UnityEngine.GameObject || owner is UnityEngine.Transform)
                {
                    continue;
                }

                var serialized = new SerializedObject(owner);

                if (owner is UnityEditor.Presets.Preset)
                {
                    count += VisitPreset_Internal(serialized, owner, tag, edit);

                    continue;
                }

                var inherits = PrefabUtility.GetCorrespondingObjectFromSource(owner) != null;

                var changed = false;

                var renamedLists = new List<string>();

                var iterator = serialized.GetIterator();

                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.Generic)
                    {
                        continue;
                    }

                    if (iterator.type == TagTypeName)
                    {
                        var name = iterator.FindPropertyRelative("name");

                        if (Matches_Internal(name, tag, inherits))
                        {
                            count++;

                            changed |= edit != null && edit(owner, name);
                        }
                    }
                    else if (iterator.type == ContainerTypeName)
                    {
                        var list = iterator.FindPropertyRelative("tags");

                        if (list == null || !list.isArray)
                        {
                            continue;
                        }

                        for (var i = 0; i < list.arraySize; i++)
                        {
                            var entry = list.GetArrayElementAtIndex(i);

                            if (!Matches_Internal(entry, tag, inherits))
                            {
                                continue;
                            }

                            count++;

                            if (edit == null || !edit(owner, entry))
                            {
                                continue;
                            }

                            changed = true;

                            if (renamedTo != null && !renamedLists.Contains(list.propertyPath))
                            {
                                renamedLists.Add(list.propertyPath);
                            }
                        }
                    }
                }

                // A rename can give a container the same tag twice: the copies go once the walk over the object is done.
                foreach (var listPath in renamedLists)
                {
                    RemoveDuplicates_Internal(serialized.FindProperty(listPath));
                }

                if (changed)
                {
                    serialized.ApplyModifiedProperties();
                }
            }

            return count;
        }

        /// <summary>
        /// A Preset keeps its values as property modifications (path and text), not as tag fields: the values of paths that
        /// end in ".name" or "tags.Array.data[n]" are the tags.
        /// </summary>
        private static int VisitPreset_Internal(SerializedObject serialized, Object owner, string tag, Func<Object, SerializedProperty, bool> edit)
        {
            var properties = serialized.FindProperty("m_Properties");

            if (properties == null || !properties.isArray)
            {
                return 0;
            }

            var count = 0;

            var changed = false;

            for (var i = 0; i < properties.arraySize; i++)
            {
                var entry = properties.GetArrayElementAtIndex(i);

                var path = entry.FindPropertyRelative("propertyPath");

                var value = entry.FindPropertyRelative("value");

                if (path == null || value == null || value.propertyType != SerializedPropertyType.String || !IsTagPath_Internal(path.stringValue) ||
                    !GameplayTagTree.IsSameOrChildOf(value.stringValue, tag))
                {
                    continue;
                }

                count++;

                changed |= edit != null && edit(owner, value);
            }

            if (changed)
            {
                serialized.ApplyModifiedProperties();
            }

            return count;
        }

        private static bool IsTagPath_Internal(string propertyPath)
        {
            return !string.IsNullOrEmpty(propertyPath) && (propertyPath.EndsWith(".name", StringComparison.Ordinal) || TagListEntry.IsMatch(propertyPath));
        }

        private static bool Matches_Internal(SerializedProperty name, string tag, bool inherits)
        {
            if (name == null || name.propertyType != SerializedPropertyType.String || !GameplayTagTree.IsSameOrChildOf(name.stringValue, tag))
            {
                return false;
            }

            // A value the asset only inherits from its base prefab belongs to that prefab, which is visited on its own.
            return !inherits || name.prefabOverride;
        }

        private static void RemoveDuplicates_Internal(SerializedProperty list)
        {
            if (list == null || !list.isArray)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < list.arraySize; i++)
            {
                if (!seen.Add(list.GetArrayElementAtIndex(i).stringValue))
                {
                    list.DeleteArrayElementAtIndex(i);

                    i--;
                }
            }
        }

        /// <summary>
        /// Whether an asset can hold the tag, without loading it: a native asset (text YAML or binary; source files such as
        /// textures, models and audio hold no tag fields) whose file has the tag's name in it. Binary assets keep strings
        /// as plain UTF-8, so a large one without the name (lighting data, terrain) is never loaded.
        /// </summary>
        private static bool MayHold_Internal(string path, string tag, byte[] tagBytes)
        {
            if (StartsWithYaml_Internal(path))
            {
                return CountMentions(DecodeYaml(File.ReadAllText(path)), tag) > 0;
            }

            var importer = AssetImporter.GetAtPath(path);

            var importerName = importer != null ? importer.GetType().Name : string.Empty;

            if (importerName != "NativeFormatImporter" && importerName != "PrefabImporter")
            {
                return false;
            }

            return Contains_Internal(File.ReadAllBytes(path), tagBytes);
        }

        private static bool StartsWithYaml_Internal(string path)
        {
            var head = new byte[5];

            using (var stream = File.OpenRead(path))
            {
                return stream.Read(head, 0, head.Length) == head.Length && Encoding.ASCII.GetString(head) == "%YAML";
            }
        }

        private static bool Contains_Internal(byte[] data, byte[] value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            for (var i = Array.IndexOf(data, value[0]); i >= 0 && i <= data.Length - value.Length; i = Array.IndexOf(data, value[0], i + 1))
            {
                var j = 1;

                while (j < value.Length && data[i + j] == value[j])
                {
                    j++;
                }

                if (j == value.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTagValue_Internal(string value, string tag)
        {
            value = value.Trim();

            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
            {
                value = value.Substring(1, value.Length - 2);
            }

            return GameplayTagTree.IsSameOrChildOf(value, tag);
        }

        private static bool IsText_Internal(string text)
        {
            return text.StartsWith("%YAML", StringComparison.Ordinal);
        }
    }
}
