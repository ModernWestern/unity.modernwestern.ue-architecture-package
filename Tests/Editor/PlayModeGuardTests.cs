using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// In Play mode the actions leave the objects of the level alone (Play mode throws their changes away), report it as
    /// a problem instead of throwing, and the UI locks the class fields of those objects.
    /// </summary>
    public class PlayModeGuardTests
    {
        [UnityTest]
        public IEnumerator Actions_RefuseLevelObjectsInPlayModeWithoutThrowing()
        {
            yield return new EnterPlayMode();

            Assert.IsTrue(Application.isPlaying);

            var holder = new GameObject("UEA Play mode probe");

            try
            {
                var start = holder.AddComponent<PlayerStart>();

                var assign = UEArchitectureActions.Assign(start, "startTag", "Red");

                Assert.IsFalse(assign.Ok, "Refused.");

                StringAssert.Contains("Leave Play mode first", assign.Problems[0]);

                Assert.IsTrue(string.IsNullOrEmpty(start.StartTag), "Nothing changed.");

                var fill = UEArchitectureActions.FillSceneReferences(start);

                Assert.IsFalse(fill.Ok);

                var clear = UEArchitectureActions.AssignClass(holder.AddComponent<WorldSettings>(), "gameModeOverride");

                Assert.IsFalse(clear.Ok, "Class slots of level objects too.");

                Assert.IsTrue(ClassField.IsLocked(new Object[] { start }), "The '+' picker and drops are off.");

                Assert.IsFalse(UEArchitectureActions.ChangeComponentClass(start, typeof(HUD), false).Ok, "Change Class of a level object is refused.");

                var pawn = holder.AddComponent<Pawn>();

                Assert.IsFalse(UEArchitectureActions.SetAutoPossessPlayer(pawn, 0).Ok, "Auto Possess Player of a level object is refused.");

                var convert = UEArchitectureActions.ConvertToFrameworkClass(UnityEditor.MonoScript.FromMonoBehaviour(start), typeof(Pawn), dryRun: false);

                Assert.IsFalse(convert.Ok, "Script edits wait for Edit mode.");

                StringAssert.Contains("Leave Play mode first", convert.Problems[0]);

                Exception thrown = null;

                try
                {
                    using (var op = Scaffold.Begin("Touch a level object in Play mode"))
                    {
                        op.Touch(start);
                    }
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }

                Assert.IsNull(thrown, "Ending a gesture in Play mode does not throw (scenes cannot be marked dirty then).");
            }
            finally
            {
                Object.Destroy(holder);
            }

            yield return new ExitPlayMode();

            Assert.IsFalse(Application.isPlaying);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }
    }
}
