using System;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// A hierarchical name, like Unreal's FGameplayTag: "Status.Burning" is a child of "Status", so it matches it.
    /// Compare tags instead of strings or enums: states, damage types, teams, abilities, input contexts.
    /// <code>
    /// static readonly GameplayTag Burning = GameplayTag.Request("Status.Burning");
    /// [SerializeField] private GameplayTag team;           // picked in the Inspector from Project Settings
    /// Burning.MatchesTag(GameplayTag.Request("Status"));   // true
    /// </code>
    /// Comparing and matching allocate nothing. Request a tag once and keep it in a static field.
    /// </summary>
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        [SerializeField] private string name;

        [NonSerialized] private int _index;

        private GameplayTag(string tagName, int index)
        {
            name = tagName;

            _index = index;
        }

        /// <summary>
        /// No tag.
        /// </summary>
        public static GameplayTag None => default;

        /// <summary>
        /// The tag with this name ("Status.Burning"), registering it and its parents the first time. Letters, digits and
        /// underscores, separated by dots. An invalid name logs a warning and returns <see cref="None"/>.
        /// </summary>
        public static GameplayTag Request(string tagName)
        {
            var index = GameplayTagRegistry.Register(tagName);

            return index > 0 ? new GameplayTag(GameplayTagRegistry.NameOf(index), index) : None;
        }

        internal static GameplayTag FromIndex_Internal(int index)
        {
            return index > 0 ? new GameplayTag(GameplayTagRegistry.NameOf(index), index) : None;
        }

        public string Name => name ?? string.Empty;

        public bool IsValid => Index > 0;

        /// <summary>
        /// "Status" for "Status.Burning"; <see cref="None"/> for a top-level tag.
        /// </summary>
        public GameplayTag Parent
        {
            get
            {
                return FromIndex_Internal(GameplayTagRegistry.ParentOf(Index));
            }
        }

        // Tags made with Request carry their index; tags from the Inspector look theirs up (no allocation either way).
        internal int Index => _index != 0 ? _index : GameplayTagRegistry.Resolve(name);

        /// <summary>
        /// True if this tag is <paramref name="other"/> or one of its children: "Status.Burning".MatchesTag("Status").
        /// </summary>
        public bool MatchesTag(GameplayTag other)
        {
            return GameplayTagRegistry.IsSameOrChild(Index, other.Index);
        }

        /// <summary>
        /// True only if both are the same tag.
        /// </summary>
        public bool MatchesTagExact(GameplayTag other)
        {
            var index = Index;

            return index > 0 && index == other.Index;
        }

        public bool Equals(GameplayTag other)
        {
            return Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            return obj is GameplayTag other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Index;
        }

        public override string ToString()
        {
            return IsValid ? Name : "None";
        }

        public static bool operator ==(GameplayTag a, GameplayTag b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(GameplayTag a, GameplayTag b)
        {
            return !a.Equals(b);
        }
    }

    /// <summary>
    /// Every tag known to the game, as a tree. Index 0 is "no tag".
    /// </summary>
    internal static class GameplayTagRegistry
    {
        private struct Node
        {
            public string Name;

            public int Parent;

            public int Depth;
        }

        private static readonly List<Node> Nodes = new() { default };

        private static readonly Dictionary<string, int> ByName = new(StringComparer.Ordinal);

        public static int Count
        {
            get
            {
                lock (Nodes)
                {
                    return Nodes.Count - 1;
                }
            }
        }

        public static int Register(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return 0;
            }

            lock (Nodes)
            {
                if (ByName.TryGetValue(tagName, out var existing))
                {
                    return existing;
                }

                if (!IsValidName(tagName))
                {
                    Debug.LogWarning($"[UEArchitecture] \"{tagName}\" is not a valid gameplay tag: use letters, digits and underscores separated by dots, like \"Status.Burning\".");

                    return 0;
                }

                var dot = tagName.LastIndexOf('.');

                var parent = dot > 0 ? Register(tagName.Substring(0, dot)) : 0;

                Nodes.Add(new Node { Name = tagName, Parent = parent, Depth = parent > 0 ? Nodes[parent].Depth + 1 : 0 });

                var index = Nodes.Count - 1;

                ByName.Add(tagName, index);

                return index;
            }
        }

        /// <summary>
        /// The index of a tag name; an unknown but valid name (e.g. typed in a serialized field) gets registered.
        /// </summary>
        public static int Resolve(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return 0;
            }

            lock (Nodes)
            {
                return ByName.TryGetValue(tagName, out var index) ? index : Register(tagName);
            }
        }

        public static string NameOf(int index)
        {
            lock (Nodes)
            {
                return index > 0 && index < Nodes.Count ? Nodes[index].Name : string.Empty;
            }
        }

        public static int ParentOf(int index)
        {
            lock (Nodes)
            {
                return index > 0 && index < Nodes.Count ? Nodes[index].Parent : 0;
            }
        }

        public static bool IsSameOrChild(int tag, int ancestor)
        {
            if (tag <= 0 || ancestor <= 0)
            {
                return false;
            }

            lock (Nodes)
            {
                var ancestorDepth = Nodes[ancestor].Depth;

                while (tag > 0 && Nodes[tag].Depth >= ancestorDepth)
                {
                    if (tag == ancestor)
                    {
                        return true;
                    }

                    tag = Nodes[tag].Parent;
                }

                return false;
            }
        }

        public static bool IsValidName(string tagName)
        {
            if (string.IsNullOrEmpty(tagName) || tagName[0] == '.' || tagName[tagName.Length - 1] == '.')
            {
                return false;
            }

            for (var i = 0; i < tagName.Length; i++)
            {
                var character = tagName[i];

                if (character == '.')
                {
                    if (tagName[i - 1] == '.')
                    {
                        return false;
                    }

                    continue;
                }

                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
