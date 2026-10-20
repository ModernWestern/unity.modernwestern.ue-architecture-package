using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// How a timer counts time and how long it lives.
    /// </summary>
    [Flags]
    public enum TimerFlags
    {
        None = 0,

        /// <summary>
        /// Ignores Time.timeScale: keeps running while the game is paused (menus, UI).
        /// </summary>
        Unscaled = 1,

        /// <summary>
        /// Survives level changes. By default timers are cleared when the level ends, like Unreal's world timers.
        /// </summary>
        Persistent = 2
    }

    /// <summary>
    /// Identifies a timer. A default handle is "no timer"; a handle whose timer finished or was cleared is just inactive.
    /// </summary>
    public readonly struct TimerHandle : IEquatable<TimerHandle>
    {
        internal readonly int Slot;

        internal readonly int Version;

        internal TimerHandle(int slot, int version)
        {
            Slot = slot + 1;

            Version = version;
        }

        /// <summary>
        /// True while the timer is waiting to fire (also while paused).
        /// </summary>
        public bool IsActive => Timers.IsActive(this);

        public bool IsPaused => Timers.IsPaused(this);

        /// <summary>
        /// Seconds until the next time it fires, 0 if inactive.
        /// </summary>
        public float Remaining => Timers.Remaining(this);

        public bool Equals(TimerHandle other)
        {
            return Slot == other.Slot && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is TimerHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Slot * 397) ^ Version;
        }
    }

    /// <summary>
    /// Calls a method after a delay or every few seconds, like Unreal's FTimerManager. Replaces Invoke (which looks
    /// methods up by name) and coroutines (which allocate), and allocates nothing once warmed up.
    /// <code>
    /// _spawn = Timers.Set(this, 2f, static self => self.SpawnEnemy(), loop: true);
    /// Timers.Clear(ref _spawn);
    /// </code>
    /// Use a <c>static</c> lambda and receive the owner as its parameter, so nothing is captured. If the owner is a Unity
    /// object that gets destroyed, its timers stop by themselves.
    /// </summary>
    public static class Timers
    {
        private delegate void Invoker(object owner, Delegate callback);

        private static class Thunk<TOwner> where TOwner : class
        {
            public static readonly Invoker Invoke = static (owner, callback) => ((Action<TOwner>)callback)((TOwner)owner);
        }

        private struct Timer
        {
            public object Owner;

            public Delegate Callback;

            public Invoker Invoke;

            public double FireAt;

            public double PausedRemaining;

            public float Interval;

            public int Version;

            public int ArmedFrame;

            public int NextFree;

            public TimerFlags Flags;

            public bool Loop;

            public bool Paused;

            public bool OwnerIsUnityObject;
        }

        private const int MaxCatchUpCalls = 16;

        private static Timer[] _timers = new Timer[16];

        private static int _used;

        private static int _freeSlot = -1;

        private static int _lastVersion;

        private static double _lastScaledNow;

        private static double _lastUnscaledNow;

        /// <summary>
        /// Tests drive time by hand: "now" is the time of the last tick instead of Unity's clock.
        /// </summary>
        internal static bool ManualClock_Internal;

        /// <summary>
        /// Timers waiting to fire.
        /// </summary>
        public static int Count { get; private set; }

        /// <summary>
        /// Calls <paramref name="callback"/> with <paramref name="owner"/> after <paramref name="seconds"/>, or every
        /// <paramref name="seconds"/> if <paramref name="loop"/> is true.
        /// </summary>
        public static TimerHandle Set<TOwner>(TOwner owner, float seconds, Action<TOwner> callback, bool loop = false, TimerFlags flags = TimerFlags.None) where TOwner : class
        {
            return Set_Internal(owner, callback, Thunk<TOwner>.Invoke, seconds, seconds, loop, flags);
        }

        /// <summary>
        /// Like <see cref="Set{TOwner}"/> with a different first delay, e.g. first after 0.5 s, then every 2 s.
        /// </summary>
        public static TimerHandle SetLooping<TOwner>(TOwner owner, float firstDelay, float interval, Action<TOwner> callback, TimerFlags flags = TimerFlags.None) where TOwner : class
        {
            return Set_Internal(owner, callback, Thunk<TOwner>.Invoke, firstDelay, interval, true, flags);
        }

        /// <summary>
        /// Calls <paramref name="callback"/> on the next frame. Like Unreal's SetTimerForNextTick.
        /// </summary>
        public static TimerHandle NextFrame<TOwner>(TOwner owner, Action<TOwner> callback, TimerFlags flags = TimerFlags.None) where TOwner : class
        {
            return Set_Internal(owner, callback, Thunk<TOwner>.Invoke, 0f, 0f, false, flags);
        }

        /// <summary>
        /// Stops the timer and resets the handle.
        /// </summary>
        public static void Clear(ref TimerHandle handle)
        {
            if (TryGet_Internal(handle, out var slot))
            {
                Release_Internal(slot);
            }

            handle = default;
        }

        /// <summary>
        /// Stops every timer of an owner (e.g. in OnDestroy, if the owner is not a Unity object).
        /// </summary>
        public static void ClearAll(object owner)
        {
            for (var i = 0; i < _used; i++)
            {
                if (_timers[i].Callback != null && ReferenceEquals(_timers[i].Owner, owner))
                {
                    Release_Internal(i);
                }
            }
        }

        public static void Pause(TimerHandle handle)
        {
            if (!TryGet_Internal(handle, out var slot) || _timers[slot].Paused)
            {
                return;
            }

            ref var timer = ref _timers[slot];

            timer.PausedRemaining = Math.Max(0d, timer.FireAt - Now_Internal(timer.Flags));

            timer.Paused = true;
        }

        public static void Resume(TimerHandle handle)
        {
            if (!TryGet_Internal(handle, out var slot) || !_timers[slot].Paused)
            {
                return;
            }

            ref var timer = ref _timers[slot];

            timer.FireAt = Now_Internal(timer.Flags) + timer.PausedRemaining;

            timer.Paused = false;
        }

        public static bool IsActive(TimerHandle handle)
        {
            return TryGet_Internal(handle, out _);
        }

        public static bool IsPaused(TimerHandle handle)
        {
            return TryGet_Internal(handle, out var slot) && _timers[slot].Paused;
        }

        public static float Remaining(TimerHandle handle)
        {
            if (!TryGet_Internal(handle, out var slot))
            {
                return 0f;
            }

            ref var timer = ref _timers[slot];

            return (float)(timer.Paused ? timer.PausedRemaining : Math.Max(0d, timer.FireAt - Now_Internal(timer.Flags)));
        }

        /// <summary>
        /// Runs every due timer. Called once per frame by the framework, with Time.timeAsDouble, Time.unscaledTimeAsDouble
        /// and Time.frameCount.
        /// </summary>
        internal static void Tick_Internal(double scaledNow, double unscaledNow, int frame)
        {
            _lastScaledNow = scaledNow;

            _lastUnscaledNow = unscaledNow;

            var used = _used;

            for (var i = 0; i < used; i++)
            {
                ref var timer = ref _timers[i];

                // Free slots, paused timers and timers set during this frame (a timer never fires in the frame it was set).
                if (timer.Callback == null || timer.Paused || timer.ArmedFrame == frame)
                {
                    continue;
                }

                var now = (timer.Flags & TimerFlags.Unscaled) != 0 ? unscaledNow : scaledNow;

                if (now < timer.FireAt)
                {
                    continue;
                }

                if (timer.OwnerIsUnityObject && (Object)timer.Owner == null)
                {
                    Release_Internal(i);

                    continue;
                }

                if (!timer.Loop)
                {
                    var owner = timer.Owner;

                    var callback = timer.Callback;

                    var invoke = timer.Invoke;

                    // Free it first, so the callback can set the same timer again.
                    Release_Internal(i);

                    Call_Internal(invoke, owner, callback);

                    continue;
                }

                var version = timer.Version;

                for (var calls = 0; calls < MaxCatchUpCalls; calls++)
                {
                    Call_Internal(timer.Invoke, timer.Owner, timer.Callback);

                    // The callback may have cleared or replaced this timer, or the array may have grown.
                    timer = ref _timers[i];

                    if (timer.Version != version || timer.Callback == null)
                    {
                        break;
                    }

                    if (timer.Interval <= 0f)
                    {
                        // Interval 0: once per frame.
                        timer.FireAt = now;

                        timer.ArmedFrame = frame;

                        break;
                    }

                    timer.FireAt += timer.Interval;

                    if (now < timer.FireAt)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Clears every timer that is not <see cref="TimerFlags.Persistent"/> (the level ended).
        /// </summary>
        internal static void ClearLevelTimers_Internal()
        {
            for (var i = 0; i < _used; i++)
            {
                if (_timers[i].Callback != null && (_timers[i].Flags & TimerFlags.Persistent) == 0)
                {
                    Release_Internal(i);
                }
            }
        }

        internal static void Reset_Internal()
        {
            Array.Clear(_timers, 0, _timers.Length);

            _used = 0;

            _freeSlot = -1;

            Count = 0;
        }

        private static TimerHandle Set_Internal(object owner, Delegate callback, Invoker invoke, float firstDelay, float interval, bool loop, TimerFlags flags)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            int slot;

            if (_freeSlot >= 0)
            {
                slot = _freeSlot;

                _freeSlot = _timers[slot].NextFree;
            }
            else
            {
                if (_used == _timers.Length)
                {
                    Array.Resize(ref _timers, _timers.Length * 2);
                }

                slot = _used++;
            }

            var version = ++_lastVersion;

            _timers[slot] = new Timer
            {
                Owner = owner,
                Callback = callback,
                Invoke = invoke,
                FireAt = Now_Internal(flags) + Math.Max(0f, firstDelay),
                Interval = Math.Max(0f, interval),
                Loop = loop,
                Flags = flags,
                Version = version,
                ArmedFrame = Time.frameCount,
                NextFree = -1,
                OwnerIsUnityObject = owner is Object
            };

            Count++;

            return new TimerHandle(slot, version);
        }

        private static bool TryGet_Internal(TimerHandle handle, out int slot)
        {
            slot = handle.Slot - 1;

            return (uint)slot < (uint)_used && _timers[slot].Version == handle.Version && _timers[slot].Callback != null;
        }

        private static void Release_Internal(int slot)
        {
            _timers[slot] = new Timer
            {
                Version = _timers[slot].Version,
                NextFree = _freeSlot
            };

            _freeSlot = slot;

            Count--;
        }

        private static void Call_Internal(Invoker invoke, object owner, Delegate callback)
        {
            try
            {
                invoke(owner, callback);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, owner as Object);
            }
        }

        private static double Now_Internal(TimerFlags flags)
        {
            var unscaled = (flags & TimerFlags.Unscaled) != 0;

            if (ManualClock_Internal)
            {
                return unscaled ? _lastUnscaledNow : _lastScaledNow;
            }

            return unscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        }
    }
}
