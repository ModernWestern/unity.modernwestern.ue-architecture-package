using System;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// When in the frame a tickable subsystem runs, like Unreal's tick groups (PrePhysics ≈ FixedUpdate, PostUpdateWork ≈ LateUpdate).
    /// </summary>
    public enum TickGroup
    {
        /// <summary>
        /// Before every MonoBehaviour.Update (read input, prepare the frame).
        /// </summary>
        EarlyUpdate,

        /// <summary>
        /// After every MonoBehaviour.Update. The default.
        /// </summary>
        Update,

        /// <summary>
        /// After every MonoBehaviour.LateUpdate (cameras, UI that follows the world).
        /// </summary>
        LateUpdate,

        /// <summary>
        /// On every physics step, after MonoBehaviour.FixedUpdate, with the fixed time step.
        /// </summary>
        FixedUpdate
    }

    /// <summary>
    /// Sets when an <see cref="ITickable"/> subsystem runs:
    /// <code>[Tick(TickGroup.LateUpdate, Order = -10)] public sealed class CameraRig : WorldSubsystem, ITickable { }</code>
    /// Without it, a tickable runs in <see cref="TickGroup.Update"/> with order 0.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class TickAttribute : Attribute
    {
        public TickAttribute(TickGroup group = TickGroup.Update)
        {
            Group = group;
        }

        public TickGroup Group { get; }

        /// <summary>
        /// Lower runs first within the group (like Script Execution Order). Same order: creation order.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Receive unscaled time, so it keeps ticking while the game is paused with Time.timeScale = 0
        /// (like Unreal's tickable when paused).
        /// </summary>
        public bool Unscaled { get; set; }
    }

    /// <summary>
    /// Every tickable subsystem, per tick group, sorted by order. Looping over it allocates nothing.
    /// </summary>
    internal static class TickRegistry
    {
        private struct Entry
        {
            public ITickable Tickable;

            public int Order;

            public int Sequence;

            public bool Unscaled;
        }

        private static readonly List<Entry>[] Groups = { new(), new(), new(), new() };

        private static int _sequence;

        private static int _running;

        private static bool _hasRemovals;

        public static int Count(TickGroup group)
        {
            var count = 0;

            foreach (var entry in Groups[(int)group])
            {
                if (entry.Tickable != null)
                {
                    count++;
                }
            }

            return count;
        }

        public static void Add(ITickable tickable)
        {
            var settings = (TickAttribute)Attribute.GetCustomAttribute(tickable.GetType(), typeof(TickAttribute)) ?? new TickAttribute();

            var entry = new Entry
            {
                Tickable = tickable,
                Order = settings.Order,
                Sequence = ++_sequence,
                Unscaled = settings.Unscaled
            };

            var list = Groups[(int)settings.Group];

            var index = list.Count;

            while (index > 0 && list[index - 1].Order > entry.Order)
            {
                index--;
            }

            list.Insert(index, entry);
        }

        public static void Remove(ITickable tickable)
        {
            foreach (var list in Groups)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (!ReferenceEquals(list[i].Tickable, tickable))
                    {
                        continue;
                    }

                    if (_running > 0)
                    {
                        // Removing while a group runs would shift the entries being visited: blank it, compact later.
                        list[i] = new Entry { Order = list[i].Order, Sequence = list[i].Sequence };

                        _hasRemovals = true;
                    }
                    else
                    {
                        list.RemoveAt(i);
                    }

                    return;
                }
            }
        }

        public static void Run(TickGroup group, float scaledDeltaTime, float unscaledDeltaTime)
        {
            var list = Groups[(int)group];

            _running++;

            try
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var entry = list[i];

                    if (entry.Tickable == null)
                    {
                        continue;
                    }

                    try
                    {
                        entry.Tickable.Tick(entry.Unscaled ? unscaledDeltaTime : scaledDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                if (--_running == 0 && _hasRemovals)
                {
                    _hasRemovals = false;

                    foreach (var each in Groups)
                    {
                        each.RemoveAll(static entry => entry.Tickable == null);
                    }
                }
            }
        }

        public static void Clear()
        {
            foreach (var list in Groups)
            {
                list.Clear();
            }

            _running = 0;

            _hasRemovals = false;
        }
    }
}
