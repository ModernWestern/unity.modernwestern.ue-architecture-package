using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.Tests
{
    // Tick probes: created only while a tick test opens this gate, and logging to their own list.
    public static class TickGate
    {
        public static bool Enabled;

        public static readonly List<string> Log = new();

        public static void Add(string entry)
        {
            Log.Add(entry + "@" + Time.frameCount);
        }
    }

    [Tick(TickGroup.EarlyUpdate)]
    public sealed class EarlyTicker : WorldSubsystem, ITickable
    {
        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime) => TickGate.Add("Early");
    }

    public sealed class DefaultTicker : WorldSubsystem, ITickable
    {
        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime) => TickGate.Add("Update");
    }

    [Tick(TickGroup.LateUpdate, Order = 10)]
    public sealed class LateTickerAfter : WorldSubsystem, ITickable
    {
        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime) => TickGate.Add("Late+10");
    }

    // Created after LateTickerAfter (names sort later) but runs before it: Order wins.
    [Tick(TickGroup.LateUpdate, Order = -10)]
    public sealed class LateTickerBefore : WorldSubsystem, ITickable
    {
        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime) => TickGate.Add("Late-10");
    }

    [Tick(TickGroup.FixedUpdate)]
    public sealed class FixedTicker : WorldSubsystem, ITickable
    {
        public float LastDeltaTime;

        public int Ticks;

        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime)
        {
            LastDeltaTime = deltaTime;

            Ticks++;
        }
    }

    [Tick(Unscaled = true)]
    public sealed class UnscaledTicker : GameInstanceSubsystem, ITickable
    {
        public float LastDeltaTime = -1f;

        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime) => LastDeltaTime = deltaTime;
    }

    public sealed class ScaledTicker : GameInstanceSubsystem, ITickable
    {
        public float LastDeltaTime = -1f;

        protected override bool ShouldCreate() => TickGate.Enabled;

        public void Tick(float deltaTime) => LastDeltaTime = deltaTime;
    }
}
