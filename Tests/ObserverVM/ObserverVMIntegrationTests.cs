using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernWestern.UEArchitecture.Tests
{
    // With ObserverVM installed, Widgets get their ViewModels from the framework by type, without code.
    public class ObserverVMIntegrationTests : FrameworkTestBase
    {
        private WorldSettings _worldSettings;

        private PlayerStart _start;

        private TestHUD _hudTemplate;

        [SetUp]
        public void BuildTemplates()
        {
            Relaunch();

            var mode = Template<TestGameMode>("Mode");

            Set(mode, "defaultPawn", Template<TestPawn>("Hero"));

            _hudTemplate = Template<TestHUD>("HUD");

            Child<ScoreWidget>(_hudTemplate, "Score");

            Child<PawnWidget>(_hudTemplate, "Health");

            Child<MatchWidget>(_hudTemplate, "Match");

            Child<FirstSubsystemWidget>(_hudTemplate, "Saves");

            Child<LevelSubsystemWidget>(_hudTemplate, "Objectives");

            Set(mode, "hud", _hudTemplate);

            _worldSettings = Create<WorldSettings>("World Settings");

            Set(_worldSettings, "gameModeOverride", mode);

            _start = Create<PlayerStart>("Start");
        }

        private static T Child<T>(Component parent, string name) where T : Component
        {
            var child = new GameObject(name).AddComponent<T>();

            child.transform.SetParent(parent.transform, false);

            return child;
        }

        [UnityTest]
        public IEnumerator WidgetsInTheHUD_AreBoundByType()
        {
            OpenLevel(_worldSettings, _start);

            yield return null;

            var hud = World.GetPlayer().HUD;

            Assert.AreSame(World.GetPlayerState<TestPlayerState>(), hud.GetComponentInChildren<ScoreWidget>().ViewModel);

            Assert.AreSame(World.GetPawn<TestPawn>(), hud.GetComponentInChildren<PawnWidget>().ViewModel);

            Assert.AreSame(World.GameState, hud.GetComponentInChildren<MatchWidget>().ViewModel);

            Assert.AreSame(GameInstance.Get<FirstSubsystem>(), hud.GetComponentInChildren<FirstSubsystemWidget>().ViewModel);

            Assert.AreSame(World.Get<LevelSubsystem>(), hud.GetComponentInChildren<LevelSubsystemWidget>().ViewModel);
        }

        [UnityTest]
        public IEnumerator APawnWidget_FollowsRespawns()
        {
            OpenLevel(_worldSettings, _start);

            yield return null;

            var player = World.GetPlayer();

            var widget = player.HUD.GetComponentInChildren<PawnWidget>();

            Object.Destroy(player.Pawn.gameObject);

            yield return null;

            Assert.IsNull(widget.ViewModel, "No Pawn, nothing to show.");

            World.GameMode.RestartPlayer(player);

            Assert.IsNotNull(widget.ViewModel);

            Assert.AreSame(player.Pawn, widget.ViewModel);
        }

        [UnityTest]
        public IEnumerator AWidgetBoundByItsParent_IsLeftAlone()
        {
            var parent = Child<ParentWidget>(_hudTemplate, "Parent");

            Set(parent, "child", Child<ScoreWidget>(parent, "Child Score"));

            OpenLevel(_worldSettings, _start);

            yield return null;

            var hud = World.GetPlayer().HUD;

            Assert.AreSame(World.GameState, hud.GetComponentInChildren<ParentWidget>().ViewModel);

            Assert.AreSame(ParentWidget.ChildViewModel, hud.transform.Find("Parent/Child Score").GetComponent<ScoreWidget>().ViewModel);
        }

        [UnityTest]
        public IEnumerator FrameworkBinder_BindsWidgetsOutsideTheHUD_AndFollowsLevels()
        {
            var overlay = Create<ScoreWidget>("Overlay");

            overlay.gameObject.AddComponent<FrameworkBinder>();

            Object.DontDestroyOnLoad(overlay.gameObject);

            OpenLevel(_worldSettings, _start);

            yield return null;

            var firstPlayerState = World.GetPlayerState<TestPlayerState>();

            Assert.AreSame(firstPlayerState, overlay.ViewModel);

            World.EndPlay_Internal();

            Assert.IsNull(overlay.ViewModel, "Lets go of the level when it ends.");

            World.Begin_Internal(World.Scene);

            yield return null;

            Assert.IsNotNull(overlay.ViewModel);

            Assert.AreNotSame(firstPlayerState, overlay.ViewModel, "Bound again to the new level's player.");
        }
    }
}
