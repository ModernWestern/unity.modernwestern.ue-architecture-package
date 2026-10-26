using NUnit.Framework;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// In Play mode the Pawn actions leave the objects of the level alone and say so (Play mode throws their changes away).
    /// </summary>
    public class PawnPlayModeGuardTests
    {
        [UnityTest]
        public IEnumerator PawnActions_LeaveLevelObjectsAloneInPlayMode()
        {
            yield return new EnterPlayMode();

            var holder = new GameObject("UEA Play mode pawn");

            try
            {
                var pawn = holder.AddComponent<TestPawn>();

                var made = UEArchitectureActions.MakeAIPawn(pawn);

                Assert.IsFalse(made.Ok, "Refused.");

                StringAssert.Contains("Leave Play mode first", made.Problems[0]);

                Assert.AreEqual(AutoPossessAI.Disabled, pawn.AutoPossessAI, "Nothing changed.");

                Assert.IsFalse(UEArchitectureActions.SetAutoPossess(pawn, AutoPossessAI.Spawned).Ok);

                Assert.IsFalse(UEArchitectureActions.AddPathFollowing(new Object[] { pawn }).Ok);

                Assert.IsFalse(UEArchitectureActions.SnapToNavMesh(pawn).Ok);
            }
            finally
            {
                Object.Destroy(holder);
            }

            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }
    }
}
