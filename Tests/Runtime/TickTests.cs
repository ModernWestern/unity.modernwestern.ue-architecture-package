using System.Linq;
using NUnit.Framework;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;

namespace ModernWestern.UEArchitecture.Tests
{
    public class TickTests : FrameworkTestBase
    {
        [SetUp]
        public void OpenTickGate()
        {
            TickGate.Enabled = true;

            TickGate.Log.Clear();
        }

        [TearDown]
        public void CloseTickGate()
        {
            TickGate.Enabled = false;

            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator EachGroup_RunsAtItsPlaceInTheFrame_AndOrderSortsWithinAGroup()
        {
            OpenLevel(Create<TickProbe>("Probe"));

            yield return null;

            TickGate.Log.Clear();

            yield return null;

            yield return null;

            // One full frame, in the order things ran.
            var frame = TickGate.Log.Where(entry => entry.EndsWith("@" + (Time.frameCount - 1))).Select(entry => entry.Substring(0, entry.IndexOf('@'))).ToArray();

            CollectionAssert.AreEqual(new[] { "Early", "MB.Update", "Update", "MB.LateUpdate", "Late-10", "Late+10" }, frame, string.Join(", ", frame));
        }

        [UnityTest]
        public IEnumerator FixedUpdateTickables_GetTheFixedTimeStep()
        {
            OpenLevel();

            var fixedTicker = World.Get<FixedTicker>();

            var timeout = Time.realtimeSinceStartup + 2f;

            while (fixedTicker.Ticks == 0 && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.Greater(fixedTicker.Ticks, 0);

            Assert.AreEqual(Time.fixedDeltaTime, fixedTicker.LastDeltaTime, 1e-5f);
        }

        [UnityTest]
        public IEnumerator UnscaledTickables_KeepTickingWhileThePausedGameStandsStill()
        {
            Relaunch();

            var unscaled = GameInstance.Get<UnscaledTicker>();

            var scaled = GameInstance.Get<ScaledTicker>();

            Time.timeScale = 0f;

            yield return null;

            yield return null;

            Assert.AreEqual(0f, scaled.LastDeltaTime, "Scaled time stops with the game.");

            Assert.Greater(unscaled.LastDeltaTime, 0f, "Unscaled time keeps going.");
        }

        [UnityTest]
        public IEnumerator TheEndOfTheLevel_StopsItsTickables()
        {
            OpenLevel();

            yield return null;

            World.End_Internal();

            TickGate.Log.Clear();

            yield return null;

            yield return null;

            Assert.IsFalse(TickGate.Log.Any(entry => entry.StartsWith("Update") || entry.StartsWith("Late") || entry.StartsWith("Early")), string.Join(", ", TickGate.Log));
        }
    }
}
