using System;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Stops listening when disposed. A default value listens to nothing.
    /// </summary>
    public readonly struct MessageListener : IDisposable
    {
        private readonly IMessageChannel _channel;

        private readonly int _slot;

        private readonly int _version;

        internal MessageListener(IMessageChannel channel, int slot, int version)
        {
            _channel = channel;

            _slot = slot;

            _version = version;
        }

        public bool IsListening => _channel != null && _channel.IsListening(_slot, _version);

        public void Dispose()
        {
            _channel?.Remove(_slot, _version);
        }
    }

    internal interface IMessageChannel
    {
        Type MessageType { get; }

        int Count { get; }

        bool IsListening(int slot, int version);

        void Remove(int slot, int version);

        void Clear();
    }

    /// <summary>
    /// Typed messages between systems that do not know each other, like Lyra's GameplayMessageSubsystem.
    /// A message is a struct: broadcasting it allocates nothing.
    /// <code>
    /// public struct PlayerDied { public PlayerState Player; }
    ///
    /// Messages.Broadcast(new PlayerDied { Player = player });
    ///
    /// _listener = Messages&lt;PlayerDied&gt;.Listen(this, static (self, message) => self.ShowDeath(message.Player));
    /// _listener.Dispose();   // or let it go: listeners of destroyed Unity objects are removed by themselves
    /// </code>
    /// </summary>
    public static class Messages
    {
        internal static readonly List<IMessageChannel> Channels = new();

        /// <summary>
        /// Sends a message to everyone listening to its type, right now. Do not rely on the order of the listeners.
        /// </summary>
        public static void Broadcast<T>(in T message) where T : struct
        {
            Messages<T>.Broadcast(in message);
        }

        /// <summary>
        /// Listens to messages of type <typeparamref name="T"/>. Prefer <c>Messages&lt;T&gt;.Listen(this, static ...)</c>,
        /// which captures nothing.
        /// </summary>
        public static MessageListener Listen<T>(Action<T> callback) where T : struct
        {
            return Messages<T>.Listen(callback);
        }

        /// <summary>
        /// Removes every listener of every message (domain reload disabled, tests).
        /// </summary>
        internal static void Reset_Internal()
        {
            lock (Channels)
            {
                foreach (var channel in Channels)
                {
                    channel.Clear();
                }
            }
        }
    }

    /// <summary>
    /// The listeners of one message type. See <see cref="Messages"/>.
    /// </summary>
    public static class Messages<T> where T : struct
    {
        private delegate void Invoker(object owner, Delegate callback, in T message);

        private static class Thunk<TOwner> where TOwner : class
        {
            public static readonly Invoker Invoke = static (object owner, Delegate callback, in T message) => ((Action<TOwner, T>)callback)((TOwner)owner, message);
        }

        private static class Plain
        {
            public static readonly Invoker Invoke = static (object _, Delegate callback, in T message) => ((Action<T>)callback)(message);
        }

        private struct Slot
        {
            public object Owner;

            public Delegate Callback;

            public Invoker Invoke;

            public int Version;

            public int Stamp;

            public int NextFree;

            public bool OwnerIsUnityObject;
        }

        private sealed class Channel : IMessageChannel
        {
            public Type MessageType => typeof(T);

            public int Count => _count;

            public bool IsListening(int slot, int version)
            {
                return (uint)slot < (uint)_used && _slots[slot].Version == version && _slots[slot].Callback != null;
            }

            public void Remove(int slot, int version)
            {
                if (IsListening(slot, version))
                {
                    Release_Internal(slot);
                }
            }

            public void Clear()
            {
                Array.Clear(_slots, 0, _slots.Length);

                _used = 0;

                _count = 0;

                _freeSlot = -1;

                _pendingFree = -1;
            }
        }

        private static readonly Channel Instance = new();

        private static Slot[] _slots = new Slot[4];

        private static int _used;

        private static int _count;

        private static int _freeSlot = -1;

        // Slots freed during a broadcast are recycled only after it, so a listener added meanwhile is never visited twice.
        private static int _pendingFree = -1;

        private static int _depth;

        private static int _stamp;

        private static int _lastVersion;

        static Messages()
        {
            lock (Messages.Channels)
            {
                Messages.Channels.Add(Instance);
            }
        }

        /// <summary>
        /// Listeners of this message type.
        /// </summary>
        public static int ListenerCount => _count;

        /// <summary>
        /// Listens with an owner and a <c>static</c> callback that receives it, so nothing is captured or allocated:
        /// <c>Messages&lt;PlayerDied&gt;.Listen(this, static (self, message) => self.ShowDeath(message.Player))</c>.
        /// If the owner is a Unity object that gets destroyed, it stops listening by itself.
        /// </summary>
        public static MessageListener Listen<TOwner>(TOwner owner, Action<TOwner, T> callback) where TOwner : class
        {
            return Add_Internal(owner, callback, Thunk<TOwner>.Invoke);
        }

        /// <summary>
        /// Listens with a plain callback (a lambda that captures variables allocates once, when created).
        /// </summary>
        public static MessageListener Listen(Action<T> callback)
        {
            return Add_Internal(null, callback, Plain.Invoke);
        }

        public static void Broadcast(in T message)
        {
            if (_count == 0)
            {
                return;
            }

            var stamp = ++_stamp;

            var used = _used;

            _depth++;

            try
            {
                for (var i = 0; i < used; i++)
                {
                    ref var slot = ref _slots[i];

                    // Free slots and listeners added during this broadcast.
                    if (slot.Callback == null || slot.Stamp == stamp)
                    {
                        continue;
                    }

                    if (slot.OwnerIsUnityObject && (Object)slot.Owner == null)
                    {
                        Release_Internal(i);

                        continue;
                    }

                    try
                    {
                        slot.Invoke(slot.Owner, slot.Callback, in message);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, _slots[i].Owner as Object);
                    }
                }
            }
            finally
            {
                if (--_depth == 0)
                {
                    RecyclePending_Internal();
                }
            }
        }

        private static MessageListener Add_Internal(object owner, Delegate callback, Invoker invoke)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            int slot;

            if (_freeSlot >= 0)
            {
                slot = _freeSlot;

                _freeSlot = _slots[slot].NextFree;
            }
            else
            {
                if (_used == _slots.Length)
                {
                    Array.Resize(ref _slots, _slots.Length * 2);
                }

                slot = _used++;
            }

            var version = ++_lastVersion;

            _slots[slot] = new Slot
            {
                Owner = owner,
                Callback = callback,
                Invoke = invoke,
                Version = version,
                Stamp = _depth > 0 ? _stamp : 0,
                NextFree = -1,
                OwnerIsUnityObject = owner is Object
            };

            _count++;

            return new MessageListener(Instance, slot, version);
        }

        private static void Release_Internal(int slot)
        {
            _slots[slot] = new Slot
            {
                Version = _slots[slot].Version
            };

            _count--;

            if (_depth > 0)
            {
                _slots[slot].NextFree = _pendingFree;

                _pendingFree = slot;
            }
            else
            {
                _slots[slot].NextFree = _freeSlot;

                _freeSlot = slot;
            }
        }

        private static void RecyclePending_Internal()
        {
            while (_pendingFree >= 0)
            {
                var slot = _pendingFree;

                _pendingFree = _slots[slot].NextFree;

                _slots[slot].NextFree = _freeSlot;

                _freeSlot = slot;
            }
        }
    }
}
