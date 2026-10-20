using System;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Gives every subsystem type a small number, so <c>Get&lt;T&gt;()</c> is an array read: no dictionary, no allocation.
    /// </summary>
    internal static class SubsystemIndex
    {
        private static readonly Dictionary<Type, int> Indices = new();

        public static int Count => Indices.Count;

        public static int Of(Type type)
        {
            lock (Indices)
            {
                if (!Indices.TryGetValue(type, out var index))
                {
                    index = Indices.Count;

                    Indices.Add(type, index);
                }

                return index;
            }
        }
    }

    internal static class SubsystemIndex<T> where T : Subsystem
    {
        public static readonly int Value = SubsystemIndex.Of(typeof(T));
    }

    /// <summary>
    /// The subsystems of one owner (the game or the current level): creates them, initializes them in order, ticks them
    /// and tears them down in reverse order.
    /// </summary>
    internal sealed class SubsystemCollection<TBase> where TBase : Subsystem
    {
        private readonly List<TBase> _ordered = new();

        private Subsystem[] _byIndex = Array.Empty<Subsystem>();

        private bool _initializing;

        public ListView<TBase> All => new(_ordered);

        public int Count => _ordered.Count;

        public void Create()
        {
            Clear();

            foreach (var type in FrameworkTypes.Concrete(typeof(TBase)))
            {
                TBase subsystem;

                try
                {
                    subsystem = (TBase)Activator.CreateInstance(type);

                    if (!subsystem.ShouldCreate_Internal())
                    {
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(new InvalidOperationException($"[UEArchitecture] Could not create {type.Name}. Subsystems need a public parameterless constructor.", exception));

                    continue;
                }

                var index = SubsystemIndex.Of(type);

                if (index >= _byIndex.Length)
                {
                    Array.Resize(ref _byIndex, Math.Max(index + 1, SubsystemIndex.Count));
                }

                _byIndex[index] = subsystem;

                _ordered.Add(subsystem);
            }

            _initializing = true;

            try
            {
                foreach (var subsystem in _ordered)
                {
                    Initialize_Internal(subsystem);
                }
            }
            finally
            {
                _initializing = false;
            }

            // Initialization order follows dependencies (Get inside Initialize), so teardown can use the reverse of it.
            _ordered.Sort(static (a, b) => a.InitializationOrder.CompareTo(b.InitializationOrder));

            // Tickables run by tick group and order; equal orders keep the initialization order.
            foreach (var subsystem in _ordered)
            {
                if (subsystem is ITickable tickable)
                {
                    TickRegistry.Add(tickable);
                }
            }
        }

        public T Get<T>() where T : TBase
        {
            var index = SubsystemIndex<T>.Value;

            if ((uint)index >= (uint)_byIndex.Length)
            {
                return null;
            }

            var subsystem = _byIndex[index];

            if (subsystem != null && _initializing && !subsystem.IsInitialized)
            {
                Initialize_Internal(subsystem);
            }

            return (T)subsystem;
        }

        public Subsystem Get(Type type)
        {
            var index = SubsystemIndex.Of(type);

            return index < _byIndex.Length ? _byIndex[index] : null;
        }

        /// <summary>
        /// Deinitializes every subsystem (reverse order). Instances stay reachable through Get until <see cref="Clear"/>,
        /// so scripts destroyed while quitting do not run into nulls.
        /// </summary>
        public void Deinitialize()
        {
            foreach (var subsystem in _ordered)
            {
                if (subsystem is ITickable tickable)
                {
                    TickRegistry.Remove(tickable);
                }
            }

            for (var i = _ordered.Count - 1; i >= 0; i--)
            {
                try
                {
                    _ordered[i].Deinitialize_Internal();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        public void Clear()
        {
            _ordered.Clear();

            Array.Clear(_byIndex, 0, _byIndex.Length);
        }

        private static int _initializationCounter;

        private static void Initialize_Internal(Subsystem subsystem)
        {
            // Already done (it was a dependency of an earlier one) or being done (a dependency cycle): nothing to do.
            if (subsystem.IsInitialized || subsystem.IsInitializing)
            {
                return;
            }

            try
            {
                subsystem.Initialize_Internal();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            subsystem.InitializationOrder = ++_initializationCounter;
        }
    }
}
