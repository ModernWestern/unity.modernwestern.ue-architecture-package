using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernWestern.UEArchitecture.Tests
{
    public class GameModeTests : FrameworkTestBase
    {
        private TestGameMode _modeTemplate;

        private PlayerStart _start;

        private PlayerStart _secondStart;

        private WorldSettings _worldSettings;

        [SetUp]
        public void BuildTemplates()
        {
            _modeTemplate = Template<TestGameMode>("Arena Mode");

            Set(_modeTemplate, "playerController", Template<TestController>("Arena Controller"));

            Set(_modeTemplate, "defaultPawn", Template<TestPawn>("Hero"));

            Set(_modeTemplate, "hud", Template<TestHUD>("Arena HUD"));

            _worldSettings = Create<WorldSettings>("World Settings");

            Set(_worldSettings, "gameModeOverride", _modeTemplate);

            _start = Create<PlayerStart>("Start A");

            _start.transform.SetPositionAndRotation(new Vector3(3f, 0f, 4f), Quaternion.Euler(0f, 90f, 0f));

            _secondStart = Create<PlayerStart>("Start B");

            _secondStart.transform.position = new Vector3(-3f, 0f, 0f);
        }

        [UnityTest]
        public IEnumerator TheGameModeOfWorldSettings_SpawnsThePlayer()
        {
            var level = OpenLevel(_worldSettings, _start, _secondStart);

            yield return null;

            var gameMode = World.GetGameMode<TestGameMode>();

            Assert.IsNotNull(gameMode);

            Assert.AreEqual(level, gameMode.gameObject.scene, "Spawned into the level, so it goes away with it.");

            Assert.IsNotNull(World.GetGameState<TestGameState>());

            Assert.AreSame(gameMode, World.GameState.GameMode);

            Assert.AreEqual(1, World.Players.Count);

            var player = World.GetPlayer();

            Assert.IsInstanceOf<TestController>(player);

            var playerState = World.GetPlayerState<TestPlayerState>();

            Assert.IsNotNull(playerState);

            Assert.AreEqual("Player 1", playerState.PlayerName);

            Assert.AreEqual(1, playerState.PlayerId);

            Assert.AreSame(player, playerState.Controller);

            Assert.AreEqual(1, World.GameState.Players.Count);

            var pawn = World.GetPawn<TestPawn>();

            Assert.IsNotNull(pawn, "The default Pawn is spawned and possessed.");

            Assert.AreEqual(_start.transform.position, pawn.transform.position);

            Assert.AreEqual(_start.transform.rotation.eulerAngles.y, pawn.transform.rotation.eulerAngles.y, 0.01f);

            Assert.AreSame(player, pawn.Controller);

            Assert.AreSame(playerState, pawn.PlayerState);

            Assert.AreEqual(1, pawn.Possessions);

            var hud = player.GetHUD<TestHUD>();

            Assert.IsNotNull(hud);

            Assert.AreSame(player, hud.Owner);

            Assert.AreEqual(1, hud.InitCalls);

            Assert.Less(IndexOf("GameMode.InitGame"), IndexOf("GameMode.PostLogin Player 1"));

            Assert.Less(IndexOf("GameMode.PostLogin Player 1"), IndexOf("GameMode.StartPlay"));

            Assert.Less(IndexOf("GameMode.StartPlay"), IndexOf("GameState.BeginPlay"));

            Assert.Less(IndexOf("GameState.BeginPlay"), IndexOf("Player 1.BeginPlay"));
        }

        [UnityTest]
        public IEnumerator RestartPlayer_AfterThePawnDies_SpawnsANewOne()
        {
            OpenLevel(_worldSettings, _start, _secondStart);

            yield return null;

            var player = (TestController)World.GetPlayer();

            var firstPawn = player.Pawn;

            Object.Destroy(firstPawn.gameObject);

            yield return null;

            Assert.IsNull(player.Pawn, "A destroyed Pawn is no longer the player's.");

            Assert.AreEqual(1, player.PawnChanges);

            Assert.IsNotNull(player.PlayerState, "The PlayerState survives the Pawn.");

            World.GameMode.RestartPlayer(player);

            Assert.IsNotNull(player.Pawn);

            Assert.AreNotSame(firstPawn, player.Pawn);

            Assert.AreEqual(_start.transform.position, player.Pawn.transform.position);

            Assert.AreEqual(2, player.PawnChanges);
        }

        [UnityTest]
        public IEnumerator Possess_MovesThePawnBetweenPlayers()
        {
            OpenLevel(_worldSettings, _start, _secondStart);

            yield return null;

            var first = World.GetPlayer();

            var second = World.GameMode.AddPlayer();

            Assert.AreEqual(2, World.Players.Count);

            Assert.AreEqual("Player 2", second.PlayerState.PlayerName);

            Assert.AreEqual(_secondStart.transform.position, second.Pawn.transform.position, "Players take the starts in turn.");

            IndexOf("Player 2.BeginPlay");

            var pawn = first.Pawn;

            second.Possess(pawn);

            Assert.IsNull(first.Pawn);

            Assert.AreSame(second, pawn.Controller);

            Assert.AreSame(second.PlayerState, pawn.PlayerState);
        }

        [UnityTest]
        public IEnumerator RemovePlayer_DestroysItsObjects()
        {
            OpenLevel(_worldSettings, _start, _secondStart);

            yield return null;

            var second = World.GameMode.AddPlayer();

            var pawn = second.Pawn;

            var hud = second.HUD;

            World.GameMode.RemovePlayer(second);

            yield return null;

            Assert.AreEqual(1, World.Players.Count);

            Assert.AreEqual(1, World.GameState.Players.Count);

            Assert.IsTrue(second == null && pawn == null && hud == null);

            Assert.Less(IndexOf("GameMode.Logout Player 2"), IndexOf("Player 2.EndPlay"));
        }

        [UnityTest]
        public IEnumerator EndingTheLevel_EndsEveryone()
        {
            OpenLevel(_worldSettings, _start, _secondStart);

            yield return null;

            TestGate.Log.Clear();

            World.End_Internal();

            Assert.Less(IndexOf("GameMode.EndPlay"), IndexOf("Player 1.EndPlay"));

            Assert.Less(IndexOf("Player 1.EndPlay"), IndexOf("GameState.EndPlay"));

            Assert.IsNull(World.GameMode);

            Assert.AreEqual(0, World.Players.Count);
        }

        [UnityTest]
        public IEnumerator AGameModeInTheScene_WinsOverWorldSettings()
        {
            var placed = Create<TestGameMode>("Placed Mode");

            OpenLevel(placed, _worldSettings, _start);

            yield return null;

            Assert.AreSame(placed, World.GameMode);

            Assert.IsNull(World.GetPawn<TestPawn>(), "The placed GameMode has no default Pawn.");
        }

        [UnityTest]
        public IEnumerator TheDefaultGameMode_ComesFromProjectSettings()
        {
            var settings = ScriptableObject.CreateInstance<UEArchitectureSettings>();

            settings.Set_Internal(null, _modeTemplate, null);

            Relaunch(settings);

            OpenLevel(_start);

            yield return null;

            Assert.IsInstanceOf<TestGameMode>(World.GameMode);

            Assert.IsNotNull(World.GetPawn<TestPawn>());

            Object.DestroyImmediate(settings);
        }

        [UnityTest]
        public IEnumerator APlacedPawnWithAutoPossessPlayer_IsPossessedInsteadOfTheDefaultPawn()
        {
            var placed = Create<TestPawn>("Player Character");

            placed.transform.position = new Vector3(10f, 0f, 0f);

            placed.AutoPossessPlayer = 0;

            var level = OpenLevel(_worldSettings, _start, _secondStart, placed);

            yield return null;

            var player = World.GetPlayer();

            Assert.AreSame(placed, player.Pawn, "Player 1 possesses the placed Pawn.");

            Assert.AreSame(player, placed.Controller);

            Assert.AreEqual(new Vector3(10f, 0f, 0f), placed.transform.position, "It stays where it was placed, not at a Player Start.");

            Assert.AreEqual(1, placed.Possessions);

            Assert.AreEqual(1, PawnsIn_Internal(level), "No Default Pawn was spawned.");

            Assert.Less(IndexOf("GameMode.InitGame"), IndexOf("Player Character.Possessed"));

            Assert.Less(IndexOf("Player Character.Possessed"), IndexOf("GameMode.PostLogin Player 1"), "Possessed before PostLogin, like a spawned Pawn.");

            Assert.Less(IndexOf("GameMode.PostLogin Player 1"), IndexOf("GameMode.StartPlay"));
        }

        [UnityTest]
        public IEnumerator APlacedPawnWithoutAutoPossessPlayer_KeepsTheDefaultPawn()
        {
            var placed = Create<TestPawn>("Statue");

            Assert.AreEqual(-1, placed.AutoPossessPlayer, "Off by default.");

            var level = OpenLevel(_worldSettings, _start, placed);

            yield return null;

            var pawn = World.GetPawn<TestPawn>();

            Assert.AreNotSame(placed, pawn);

            Assert.AreEqual("Hero", pawn.name, "The Default Pawn, as before.");

            Assert.AreEqual(_start.transform.position, pawn.transform.position);

            Assert.IsNull(placed.Controller);

            Assert.AreEqual(2, PawnsIn_Internal(level));
        }

        [UnityTest]
        public IEnumerator APlacedPawnForPlayer2_WaitsForPlayer2()
        {
            var placed = Create<TestPawn>("Second Character");

            placed.AutoPossessPlayer = 1;

            OpenLevel(_worldSettings, _start, _secondStart, placed);

            yield return null;

            Assert.AreEqual("Hero", World.GetPawn<TestPawn>().name, "Player 1 gets the Default Pawn.");

            Assert.IsNull(placed.Controller);

            var second = World.GameMode.AddPlayer();

            Assert.AreSame(placed, second.Pawn, "Player 2 possesses its placed Pawn when it joins.");
        }

        [UnityTest]
        public IEnumerator APlacedPawnYourCodeAlreadyPossessed_IsLeftAlone()
        {
            var placed = Create<TestPawn>("Taken");

            placed.AutoPossessPlayer = 0;

            var mode = Create<TestCodePossessGameMode>("Code Mode");

            Set(mode, "taken", placed);

            Set(mode, "defaultPawn", Template<TestPawn>("Hero"));

            OpenLevel(mode, _start, placed);

            yield return null;

            Assert.IsInstanceOf<AIController>(placed.Controller, "Still the controller the code gave it.");

            var pawn = World.GetPawn<TestPawn>();

            Assert.IsNotNull(pawn);

            Assert.AreNotSame(placed, pawn, "The player gets its Default Pawn instead.");

            Assert.AreEqual("Hero", pawn.name);
        }

        [UnityTest]
        public IEnumerator APlacedPawnForThePlayer_WinsOverAutoPossessAI()
        {
            var placed = Create<TestPawn>("Player Character");

            placed.AutoPossessPlayer = 0;

            placed.AutoPossessAI = AutoPossessAI.PlacedInWorld;

            OpenLevel(_worldSettings, _start, placed);

            yield return null;

            Assert.AreSame(World.GetPlayer(), placed.Controller);

            Assert.AreEqual(0, World.AIControllers.Count, "No AI controller was made for it.");
        }

        [UnityTest]
        public IEnumerator RestartPlayer_AfterThePlacedPawnDies_SpawnsTheDefaultPawn()
        {
            var placed = Create<TestPawn>("Player Character");

            placed.AutoPossessPlayer = 0;

            OpenLevel(_worldSettings, _start, placed);

            yield return null;

            var player = World.GetPlayer();

            Object.Destroy(placed.gameObject);

            yield return null;

            World.GameMode.RestartPlayer(player);

            Assert.IsNotNull(player.Pawn);

            Assert.AreEqual("Hero", player.Pawn.name);

            Assert.AreEqual(_start.transform.position, player.Pawn.transform.position);
        }

        [UnityTest]
        public IEnumerator RestartPlayer_AfterLeavingThePlacedPawnAlive_SpawnsTheDefaultPawn()
        {
            var placed = Create<TestPawn>("Player Character");

            placed.AutoPossessPlayer = 0;

            OpenLevel(_worldSettings, _start, placed);

            yield return null;

            var player = World.GetPlayer();

            Assert.AreSame(placed, player.Pawn);

            // Like getting out of a vehicle or a corpse left behind: the placed Pawn is alive and free again.
            player.UnPossess();

            Assert.IsNull(placed.Controller);

            World.GameMode.RestartPlayer(player);

            Assert.IsNotNull(player.Pawn);

            Assert.AreNotSame(placed, player.Pawn, "Auto Possess Player is used once per level, as in Unreal.");

            Assert.AreEqual("Hero", player.Pawn.name);

            Assert.IsNull(placed.Controller);

            Assert.AreEqual(1, placed.Possessions);
        }

        [UnityTest]
        public IEnumerator TwoPlacedPawnsForTheSamePlayer_OnlyTheFirstIsEverPossessed()
        {
            var first = Create<TestPawn>("First Character");

            first.AutoPossessPlayer = 0;

            var second = Create<TestPawn>("Second Character");

            second.AutoPossessPlayer = 0;

            OpenLevel(_worldSettings, _start, first, second);

            yield return null;

            var player = World.GetPlayer();

            Assert.AreSame(first, player.Pawn);

            Object.Destroy(first.gameObject);

            yield return null;

            World.GameMode.RestartPlayer(player);

            Assert.AreNotSame(second, player.Pawn, "The duplicate is not a fallback.");

            Assert.AreEqual("Hero", player.Pawn.name);

            Assert.IsNull(second.Controller);
        }

        [UnityTest]
        public IEnumerator APlacedPawnForPlayer2_WithAutoPossessAI_WaitsForPlayer2()
        {
            var placed = Create<TestPawn>("Second Character");

            placed.AutoPossessPlayer = 1;

            placed.AutoPossessAI = AutoPossessAI.PlacedInWorld;

            OpenLevel(_worldSettings, _start, _secondStart, placed);

            yield return null;

            Assert.IsNull(placed.Controller, "The player wins over Auto Possess AI: it waits for Player 2.");

            Assert.AreEqual(0, World.AIControllers.Count);

            var second = World.GameMode.AddPlayer();

            Assert.AreSame(placed, second.Pawn);
        }

        [UnityTest]
        public IEnumerator ALevelWithoutGameMode_StillWorks()
        {
            OpenLevel(_start);

            yield return null;

            Assert.IsTrue(World.HasBegunPlay);

            Assert.IsNull(World.GameMode);

            Assert.IsNull(World.GameState);

            Assert.IsNull(World.GetPlayer());

            Assert.IsNotNull(World.Get<LevelSubsystem>());
        }

        private static int PawnsIn_Internal(UnityEngine.SceneManagement.Scene level)
        {
            var count = 0;

            foreach (var root in level.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<Pawn>().Length;
            }

            return count;
        }
    }
}
