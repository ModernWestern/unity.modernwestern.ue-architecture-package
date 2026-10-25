using System;
using System.Linq;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Changes to the project's tag list (the "gameplayTags" array of the settings asset), as pure functions on the list:
    /// add, rename and remove a tag with its children. They never touch assets; the actions do that.
    /// </summary>
    internal static class GameplayTagEdits
    {
        /// <summary>
        /// Null when <paramref name="tag"/> is a valid tag name, otherwise what is wrong with it, in plain words.
        /// </summary>
        public static string NameProblem(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return "Type a tag name, like \"Status.Burning\".";
            }

            return GameplayTagRegistry.IsValidName(tag) ? null : $"\"{tag}\" is not a valid tag: use letters, digits and underscores separated by dots, like \"Status.Burning\".";
        }

        /// <summary>
        /// True when the list has the tag, listed or implied by a child ("Status" when "Status.Burning" is listed).
        /// </summary>
        public static bool IsKnown(IEnumerable<string> declared, string tag)
        {
            return !string.IsNullOrEmpty(tag) && (declared ?? Array.Empty<string>()).Any(listed => GameplayTagRegistry.IsValidName(listed) && GameplayTagTree.IsSameOrChildOf(listed, tag));
        }

        /// <summary>
        /// The listed tags that are <paramref name="tag"/> or below it.
        /// </summary>
        public static List<string> Subtree(IEnumerable<string> declared, string tag)
        {
            return (declared ?? Array.Empty<string>()).Where(listed => GameplayTagTree.IsSameOrChildOf(listed, tag)).Distinct().ToList();
        }

        /// <summary>
        /// The list with <paramref name="tag"/> added at the end. Unchanged when the tag is already there (listed or
        /// implied) or invalid (<paramref name="problem"/> says why).
        /// </summary>
        public static string[] Add(IReadOnlyList<string> declared, string tag, out bool changed, out string problem)
        {
            var list = (declared ?? Array.Empty<string>()).ToList();

            changed = false;

            tag = (tag ?? string.Empty).Trim();

            problem = NameProblem(tag);

            if (problem != null || IsKnown(list, tag))
            {
                return list.ToArray();
            }

            list.Add(tag);

            changed = true;

            return list.ToArray();
        }

        /// <summary>
        /// The new name of a tag value when <paramref name="from"/> becomes <paramref name="to"/>: "Status.Burning" gives
        /// "Condition.Burning" for Status to Condition. Null when the value is not <paramref name="from"/> or below it.
        /// </summary>
        public static string RenameValue(string value, string from, string to)
        {
            if (!GameplayTagTree.IsSameOrChildOf(value, from))
            {
                return null;
            }

            return to + value.Substring(from.Length);
        }

        /// <summary>
        /// Null when <paramref name="from"/> can become <paramref name="to"/> in this list, otherwise why not.
        /// </summary>
        public static string RenameProblem(IReadOnlyList<string> declared, string from, string to)
        {
            var problem = NameProblem(from) ?? NameProblem(to);

            if (problem != null)
            {
                return problem;
            }

            if (from == to)
            {
                return $"\"{to}\" is already its name.";
            }

            if (GameplayTagTree.IsChildOf(to, from))
            {
                return $"\"{to}\" is inside \"{from}\": a tag cannot move under itself.";
            }

            if (IsKnown(declared, from) && IsKnown(declared, to))
            {
                return $"\"{to}\" is already a project tag. Pick a new name (merging two tags is not supported).";
            }

            return null;
        }

        /// <summary>
        /// The list with <paramref name="from"/> and every tag below it renamed (Status.Burning to Condition.Burning for
        /// Status to Condition). <paramref name="renamed"/> counts the listed tags that changed; unchanged when the rename
        /// is not possible (<paramref name="problem"/>).
        /// </summary>
        public static string[] Rename(IReadOnlyList<string> declared, string from, string to, out int renamed, out string problem)
        {
            var list = (declared ?? Array.Empty<string>()).ToList();

            renamed = 0;

            problem = RenameProblem(list, from, to);

            if (problem != null)
            {
                return list.ToArray();
            }

            var result = new List<string>(list.Count);

            foreach (var tag in list)
            {
                var renamedTag = RenameValue(tag, from, to);

                if (renamedTag != null)
                {
                    renamed++;
                }

                var value = renamedTag ?? tag;

                if (!result.Contains(value))
                {
                    result.Add(value);
                }
            }

            // Renaming a parent nobody listed ("Status", implied by "Status.Burning") changes its children only.
            return result.ToArray();
        }

        /// <summary>
        /// The list without <paramref name="tag"/> and the tags below it (<paramref name="removed"/> counts the listed tags
        /// that went). Its parent stays even when nothing else implied it: deleting "Status.Burning" keeps "Status".
        /// </summary>
        public static string[] Remove(IReadOnlyList<string> declared, string tag, out int removed)
        {
            var list = (declared ?? Array.Empty<string>()).ToList();

            removed = 0;

            if (string.IsNullOrEmpty(tag) || !IsKnown(list, tag))
            {
                return list.ToArray();
            }

            var kept = list.Where(listed => !GameplayTagTree.IsSameOrChildOf(listed, tag)).ToList();

            removed = list.Count - kept.Count;

            var parent = GameplayTagTree.ParentOf(tag);

            if (parent.Length > 0 && !IsKnown(kept, parent))
            {
                kept.Add(parent);
            }

            return kept.ToArray();
        }
    }
}
