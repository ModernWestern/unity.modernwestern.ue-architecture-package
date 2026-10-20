using System;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// A set of gameplay tags, like Unreal's FGameplayTagContainer: the states of a character, the tags an ability needs.
    /// Editable in the Inspector. Queries match parents: a container with "Status.Burning" has "Status".
    /// <code>
    /// if (pawnTags.HasTag(Stunned)) return;
    /// if (pawnTags.HasAll(ability.RequiredTags) &amp;&amp; !pawnTags.HasAny(ability.BlockedTags)) Activate();
    /// </code>
    /// Queries, adding and removing allocate nothing once the container has room.
    /// </summary>
    [Serializable]
    public sealed class GameplayTagContainer : ISerializationCallbackReceiver
    {
        [SerializeField] private List<string> tags = new();

        [NonSerialized] private List<int> _indices;

        public GameplayTagContainer()
        {
        }

        public GameplayTagContainer(params GameplayTag[] initial)
        {
            foreach (var tag in initial)
            {
                AddTag(tag);
            }
        }

        public int Count => Indices_Internal.Count;

        public GameplayTag this[int index] => GameplayTag.FromIndex_Internal(Indices_Internal[index]);

        private List<int> Indices_Internal
        {
            get
            {
                if (_indices == null)
                {
                    Resolve_Internal();
                }

                return _indices;
            }
        }

        /// <summary>
        /// Adds a tag. Does nothing if it is already there or invalid.
        /// </summary>
        public void AddTag(GameplayTag tag)
        {
            var index = tag.Index;

            if (index <= 0 || Indices_Internal.Contains(index))
            {
                return;
            }

            _indices.Add(index);

            tags.Add(GameplayTagRegistry.NameOf(index));
        }

        public bool RemoveTag(GameplayTag tag)
        {
            var at = Indices_Internal.IndexOf(tag.Index);

            if (at < 0)
            {
                return false;
            }

            _indices.RemoveAt(at);

            tags.RemoveAt(at);

            return true;
        }

        public void Clear()
        {
            Indices_Internal.Clear();

            tags.Clear();
        }

        /// <summary>
        /// True if any tag in it is <paramref name="tag"/> or one of its children.
        /// </summary>
        public bool HasTag(GameplayTag tag)
        {
            var query = tag.Index;

            var indices = Indices_Internal;

            for (var i = 0; i < indices.Count; i++)
            {
                if (GameplayTagRegistry.IsSameOrChild(indices[i], query))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True only if it holds exactly <paramref name="tag"/>.
        /// </summary>
        public bool HasTagExact(GameplayTag tag)
        {
            var query = tag.Index;

            return query > 0 && Indices_Internal.Contains(query);
        }

        /// <summary>
        /// True if it has at least one of the tags of <paramref name="other"/> (parents match). False when other is empty.
        /// </summary>
        public bool HasAny(GameplayTagContainer other)
        {
            if (other == null)
            {
                return false;
            }

            var queries = other.Indices_Internal;

            for (var i = 0; i < queries.Count; i++)
            {
                if (HasIndex_Internal(queries[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if it has every tag of <paramref name="other"/> (parents match). True when other is empty.
        /// </summary>
        public bool HasAll(GameplayTagContainer other)
        {
            if (other == null)
            {
                return true;
            }

            var queries = other.Indices_Internal;

            for (var i = 0; i < queries.Count; i++)
            {
                if (!HasIndex_Internal(queries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString()
        {
            return tags.Count == 0 ? "(no tags)" : string.Join(", ", tags);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Resolved on first use (deserialization can run on a loading thread).
            _indices = null;
        }

        private bool HasIndex_Internal(int query)
        {
            var indices = _indices ?? Indices_Internal;

            for (var i = 0; i < indices.Count; i++)
            {
                if (GameplayTagRegistry.IsSameOrChild(indices[i], query))
                {
                    return true;
                }
            }

            return false;
        }

        private void Resolve_Internal()
        {
            _indices = new List<int>(Math.Max(4, tags.Count));

            // Drop empty, invalid and repeated names left in the serialized list.
            var kept = 0;

            for (var i = 0; i < tags.Count; i++)
            {
                var index = GameplayTagRegistry.Resolve(tags[i]);

                if (index <= 0 || _indices.Contains(index))
                {
                    continue;
                }

                _indices.Add(index);

                tags[kept++] = GameplayTagRegistry.NameOf(index);
            }

            tags.RemoveRange(kept, tags.Count - kept);
        }
    }
}
