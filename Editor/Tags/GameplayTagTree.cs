using System;
using System.Linq;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// One tag of the project's tag tree: "Status.Burning" (leaf "Burning", parent "Status", depth 1). A parent nobody
    /// listed ("Status" when only "Status.Burning" is in Project Settings) is implied: it exists, but is not declared.
    /// </summary>
    internal readonly struct GameplayTagNode
    {
        public readonly string Name;

        public readonly int Depth;

        public readonly bool Declared;

        public readonly bool HasChildren;

        public GameplayTagNode(string name, int depth, bool declared, bool hasChildren)
        {
            Name = name;

            Depth = depth;

            Declared = declared;

            HasChildren = hasChildren;
        }

        /// <summary>
        /// "Burning" for "Status.Burning".
        /// </summary>
        public string Leaf => GameplayTagTree.LeafOf(Name);

        /// <summary>
        /// "Status" for "Status.Burning"; empty for a top-level tag.
        /// </summary>
        public string Parent => GameplayTagTree.ParentOf(Name);

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The project's gameplay tags as a tree, from the list in Project Settings (pure: no Unity API, tested). Parents
    /// come by themselves: "Status.Burning" also gives "Status". Names that are not valid tags are left out (see
    /// <see cref="Invalid"/>).
    /// </summary>
    internal static class GameplayTagTree
    {
        /// <summary>
        /// Every valid tag with its implied parents, depth first: a parent, then its children in name order.
        /// </summary>
        public static List<GameplayTagNode> Build(IEnumerable<string> declared)
        {
            var listed = new HashSet<string>(StringComparer.Ordinal);

            var all = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var tag in declared ?? Array.Empty<string>())
            {
                if (!GameplayTagRegistry.IsValidName(tag))
                {
                    continue;
                }

                listed.Add(tag);

                for (var dot = tag.IndexOf('.'); dot > 0; dot = tag.IndexOf('.', dot + 1))
                {
                    all.Add(tag.Substring(0, dot));
                }

                all.Add(tag);
            }

            // Ordinal order is depth first: a valid name only holds letters, digits and underscores after its dots, which
            // all sort after '.', so "Status.*" comes right after "Status" and before "StatusEffect".
            var names = all.ToList();

            var nodes = new List<GameplayTagNode>(names.Count);

            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];

                var hasChildren = i + 1 < names.Count && IsChildOf(names[i + 1], name);

                nodes.Add(new GameplayTagNode(name, DepthOf(name), listed.Contains(name), hasChildren));
            }

            return nodes;
        }

        /// <summary>
        /// The names of the list that are not valid tags (shown as warnings; nothing offers them).
        /// </summary>
        public static List<string> Invalid(IEnumerable<string> declared)
        {
            return (declared ?? Array.Empty<string>()).Where(tag => !GameplayTagRegistry.IsValidName(tag)).Distinct().ToList();
        }

        /// <summary>
        /// The rows to show for a search: tags whose name contains <paramref name="search"/> (any case), with their parents
        /// so each match stays in its place in the tree. Everything for an empty search.
        /// </summary>
        public static List<GameplayTagNode> Filter(IReadOnlyList<GameplayTagNode> nodes, string search)
        {
            var query = (search ?? string.Empty).Trim();

            if (query.Length == 0)
            {
                return nodes.ToList();
            }

            var keep = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                if (node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                for (var name = node.Name; name.Length > 0; name = ParentOf(name))
                {
                    keep.Add(name);
                }
            }

            return nodes.Where(node => keep.Contains(node.Name)).ToList();
        }

        /// <summary>
        /// True when <paramref name="tag"/> is <paramref name="ancestor"/> or one of its children ("Status.Burning" and "Status").
        /// </summary>
        public static bool IsSameOrChildOf(string tag, string ancestor)
        {
            return !string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(ancestor) && (tag == ancestor || IsChildOf(tag, ancestor));
        }

        /// <summary>
        /// True when <paramref name="tag"/> is below <paramref name="ancestor"/> (at any depth), not the tag itself.
        /// </summary>
        public static bool IsChildOf(string tag, string ancestor)
        {
            return !string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(ancestor) && tag.Length > ancestor.Length + 1 && tag[ancestor.Length] == '.' &&
                   tag.StartsWith(ancestor, StringComparison.Ordinal);
        }

        public static string ParentOf(string tag)
        {
            var dot = string.IsNullOrEmpty(tag) ? -1 : tag.LastIndexOf('.');

            return dot > 0 ? tag.Substring(0, dot) : string.Empty;
        }

        public static string LeafOf(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return string.Empty;
            }

            var dot = tag.LastIndexOf('.');

            return dot >= 0 ? tag.Substring(dot + 1) : tag;
        }

        public static int DepthOf(string tag)
        {
            return string.IsNullOrEmpty(tag) ? 0 : tag.Count(character => character == '.');
        }
    }
}
