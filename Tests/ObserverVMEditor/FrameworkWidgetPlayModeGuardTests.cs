using NUnit.Framework;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// In Play mode the integration's actions leave the objects of the level alone and say so.
    /// </summary>
    public class FrameworkWidgetPlayModeGuardTests
    {
        [UnityTest]
        public IEnumerator TheActions_LeaveLevelObjectsAloneInPlayMode()
        {
            yield return new EnterPlayMode();

            var canvas = new GameObject("UEA Play mode canvas", typeof(RectTransform), typeof(Canvas));

            try
            {
                var widget = new GameObject("Coins", typeof(RectTransform)).AddComponent<ScoreWidget>();

                widget.transform.SetParent(canvas.transform, false);

                var single = FrameworkWidgetActions.AddFrameworkBinder(widget);

                Assert.IsFalse(single.Ok, "Refused.");

                StringAssert.Contains("Leave Play mode first", single.Problems[0]);

                Assert.IsNull(widget.GetComponent<FrameworkBinder>(), "Nothing changed.");

                Assert.IsFalse(FrameworkWidgetActions.AddFrameworkBinders(canvas).Ok);

                var hud = new GameObject("UEA Play mode HUD", typeof(RectTransform)).AddComponent<TestHUD>();

                Assert.IsFalse(FrameworkWidgetActions.AddHudWidget(hud, typeof(TestPlayerState)).Ok);

                Object.Destroy(hud.gameObject);
            }
            finally
            {
                Object.Destroy(canvas);
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
