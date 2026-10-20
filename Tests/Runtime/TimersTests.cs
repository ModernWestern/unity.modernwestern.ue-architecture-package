using NUnit.Framework;
using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    // Timers are driven by hand here (time and frame), so the tests do not depend on the real frame rate.
    public class TimersTests
    {
        private double _scaled;

        private double _unscaled;

        private int _frame;

        [SetUp]
        public void StartClock()
        {
            Timers.Reset_Internal();

            _scaled = Time.timeAsDouble;

            _unscaled = Time.unscaledTimeAsDouble;

            _frame = Time.frameCount;

            Timers.ManualClock_Internal = true;

            Timers.Tick_Internal(_scaled, _unscaled, _frame);
        }

        [TearDown]
        public void ClearTimers()
        {
            Timers.ManualClock_Internal = false;

            Timers.Reset_Internal();
        }

        private void Advance(double seconds, double unscaledSeconds = -1)
        {
            _scaled += seconds;

            _unscaled += unscaledSeconds < 0 ? seconds : unscaledSeconds;

            _frame++;

            Timers.Tick_Internal(_scaled, _unscaled, _frame);
        }

        [Test]
        public void Once_FiresAfterTheDelay_OnlyOnce()
        {
            var counter = new Counter();

            var handle = Timers.Set(counter, 1f, static c => c.Calls++);

            Assert.IsTrue(handle.IsActive);

            Advance(0.5);

            Assert.AreEqual(0, counter.Calls);

            Assert.AreEqual(0.5f, handle.Remaining, 0.001f);

            Advance(0.6);

            Assert.AreEqual(1, counter.Calls);

            Assert.IsFalse(handle.IsActive);

            Advance(5);

            Assert.AreEqual(1, counter.Calls);
        }

        [Test]
        public void Loop_FiresEveryInterval_AndCatchesUpAfterALongFrame()
        {
            var counter = new Counter();

            Timers.Set(counter, 1f, static c => c.Calls++, loop: true);

            Advance(1.0);

            Assert.AreEqual(1, counter.Calls);

            Advance(3.5);

            Assert.AreEqual(4, counter.Calls, "A long frame fires every missed interval.");
        }

        [Test]
        public void SetLooping_UsesAFirstDelay()
        {
            var counter = new Counter();

            Timers.SetLooping(counter, 0.25f, 2f, static c => c.Calls++);

            Advance(0.3);

            Assert.AreEqual(1, counter.Calls);

            Advance(1.0);

            Assert.AreEqual(1, counter.Calls);

            Advance(1.0);

            Assert.AreEqual(2, counter.Calls);
        }

        [Test]
        public void NextFrame_FiresOnTheNextFrame_NotThisOne()
        {
            var counter = new Counter();

            Timers.NextFrame(counter, static c => c.Calls++);

            Timers.Tick_Internal(_scaled, _unscaled, Time.frameCount);

            Assert.AreEqual(0, counter.Calls, "Never in the frame it was set.");

            Advance(0);

            Assert.AreEqual(1, counter.Calls);
        }

        [Test]
        public void Clear_StopsIt_AndResetsTheHandle()
        {
            var counter = new Counter();

            var handle = Timers.Set(counter, 1f, static c => c.Calls++);

            Timers.Clear(ref handle);

            Assert.AreEqual(default(TimerHandle), handle);

            Advance(2);

            Assert.AreEqual(0, counter.Calls);

            Assert.AreEqual(0, Timers.Count);
        }

        [Test]
        public void PauseAndResume_KeepTheRemainingTime()
        {
            var counter = new Counter();

            var handle = Timers.Set(counter, 2f, static c => c.Calls++);

            Advance(0.5);

            Timers.Pause(handle);

            Assert.IsTrue(handle.IsPaused);

            Advance(10);

            Assert.AreEqual(0, counter.Calls);

            Assert.AreEqual(1.5f, handle.Remaining, 0.001f);

            Timers.Resume(handle);

            Advance(1.4);

            Assert.AreEqual(0, counter.Calls);

            Advance(0.2);

            Assert.AreEqual(1, counter.Calls);
        }

        [Test]
        public void ADestroyedUnityOwner_StopsItsTimers()
        {
            var owner = new GameObject("Owner").transform;

            var handle = Timers.Set(owner, 1f, static t => t.name = "Fired", loop: true);

            Object.DestroyImmediate(owner.gameObject);

            Advance(2);

            Assert.IsFalse(handle.IsActive);
        }

        [Test]
        public void TheEndOfALevel_ClearsLevelTimers_ButNotPersistentOnes()
        {
            var level = new Counter();

            var persistent = new Counter();

            var levelHandle = Timers.Set(level, 1f, static c => c.Calls++);

            var persistentHandle = Timers.Set(persistent, 1f, static c => c.Calls++, flags: TimerFlags.Persistent);

            Timers.ClearLevelTimers_Internal();

            Advance(2);

            Assert.IsFalse(levelHandle.IsActive);

            Assert.AreEqual(0, level.Calls);

            Assert.AreEqual(1, persistent.Calls);

            Assert.IsFalse(persistentHandle.IsActive);
        }

        [Test]
        public void UnscaledTimers_KeepRunningWhileThePausedGameStandsStill()
        {
            var scaled = new Counter();

            var unscaled = new Counter();

            Timers.Set(scaled, 1f, static c => c.Calls++);

            Timers.Set(unscaled, 1f, static c => c.Calls++, flags: TimerFlags.Unscaled);

            Advance(0, unscaledSeconds: 2);

            Assert.AreEqual(0, scaled.Calls);

            Assert.AreEqual(1, unscaled.Calls);
        }

        [Test]
        public void ACallback_CanSetItsTimerAgain()
        {
            var counter = new Counter();

            Timers.Set(counter, 1f, static c =>
            {
                c.Calls++;

                if (c.Calls < 3)
                {
                    Timers.Set(c, 1f, static again => again.Calls++);
                }
            });

            Advance(1);

            Assert.AreEqual(1, counter.Calls);

            Advance(1);

            Assert.AreEqual(2, counter.Calls);
        }

        [Test]
        public void ClearAll_StopsEveryTimerOfAnOwner()
        {
            var owner = new Counter();

            var other = new Counter();

            Timers.Set(owner, 1f, static c => c.Calls++);

            Timers.Set(owner, 2f, static c => c.Calls++, loop: true);

            Timers.Set(other, 1f, static c => c.Calls++);

            Timers.ClearAll(owner);

            Advance(3);

            Assert.AreEqual(0, owner.Calls);

            Assert.AreEqual(1, other.Calls);
        }
    }
}
