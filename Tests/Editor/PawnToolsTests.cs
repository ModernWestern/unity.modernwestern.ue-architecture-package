using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// Pawn setup: Make AI Pawn, Auto Possess, Add Path Following and Snap to the NavMesh, each as an action and a
    /// command-line verb run twice (the second run changes nothing), and the inspector's "Controlled by".
    /// </summary>
    public class PawnToolsTests
    {
        private const string Folder = "Assets/__UEATempPawnTools";

        private EditorTestKit _kit;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            _kit.Dispose();
        }

        [Test]
        public void MakeAIPawn_Twice_FillsTheControllerAndAutoPossess()
        {
            var pawn = _kit.Prefab("Guard", typeof(TestPawn)).GetComponent<Pawn>();

            var first = UEArchitectureActions.MakeAIPawn(pawn);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.AreEqual(AutoPossessAI.PlacedInWorldOrSpawned, pawn.AutoPossessAI);

            var controller = new SerializedObject(pawn).FindProperty("aiController").objectReferenceValue as AIController;

            Assert.IsNotNull(controller, "The AI Controller slot is filled.");

            Assert.IsTrue(EditorUtility.IsPersistent(controller), "With a prefab.");

            Assert.IsTrue(first.Created.Any(path => path.EndsWith(".prefab")), string.Join("\n", first.Created));

            var second = UEArchitectureActions.MakeAIPawn(pawn);

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created);

            CollectionAssert.IsEmpty(second.Changed);

            Assert.IsFalse(UEArchitectureActions.MakeAIPawn(pawn, null, AutoPossessAI.Disabled).Ok, "Disabled is not an AI Pawn.");

            Assert.IsFalse(UEArchitectureActions.MakeAIPawn(pawn, typeof(TestPawn)).Ok, "Only AI Controller classes.");
        }

        [Test]
        public void MakeAIPawn_KeepsWhatIsSetAndUsesTheGivenClass()
        {
            var pawn = _kit.Prefab("Scout", typeof(TestPawn)).GetComponent<Pawn>();

            UEArchitectureActions.Assign(pawn, "autoPossessAI", "Spawned");

            var result = UEArchitectureActions.MakeAIPawn(pawn, typeof(TestAI));

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.AreEqual(AutoPossessAI.Spawned, pawn.AutoPossessAI, "A chosen Auto Possess stays.");

            Assert.IsInstanceOf<TestAI>(new SerializedObject(pawn).FindProperty("aiController").objectReferenceValue);
        }

        [Test]
        public void MakeAIPawn_Twice_FromTheCommandLine()
        {
            var first = _kit.Prefab("GuardA", typeof(TestPawn));

            var second = _kit.Prefab("GuardB", typeof(TestPawn));

            var targets = AssetDatabase.GetAssetPath(first) + ";" + AssetDatabase.GetAssetPath(second);

            var result = Twice_Internal("make-ai-pawn", "target=" + targets, "controller=" + typeof(TestAI).FullName, "possess=placed");

            Assert.AreEqual(2, result.Changed.Count(entry => entry.EndsWith("aiController")), string.Join("\n", result.Changed));

            Assert.AreEqual(AutoPossessAI.PlacedInWorld, first.GetComponent<Pawn>().AutoPossessAI);

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("make-ai-pawn", "target=" + targets, "possess=sometimes")).Ok, "Unknown possess value.");
        }

        [Test]
        public void SetAutoPossess_Twice_ActionAndCommandLine()
        {
            var prefab = _kit.Prefab("Turret", typeof(TestPawn));

            var pawn = prefab.GetComponent<Pawn>();

            var first = UEArchitectureActions.SetAutoPossess(pawn, AutoPossessAI.PlacedInWorld);

            Assert.IsTrue(first.Ok);

            Assert.AreEqual(1, first.Changed.Count);

            CollectionAssert.IsEmpty(UEArchitectureActions.SetAutoPossess(pawn, AutoPossessAI.PlacedInWorld).Changed);

            Twice_Internal("set-auto-possess", "target=" + AssetDatabase.GetAssetPath(prefab), "possess=spawned");

            Assert.AreEqual(AutoPossessAI.Spawned, pawn.AutoPossessAI);
        }

        [Test]
        public void AddPathFollowing_Twice_OnSeveralPrefabs()
        {
            var first = _kit.Prefab("Npc1", typeof(TestPawn));

            var second = _kit.Prefab("Npc2", typeof(TestPawn));
#if UEA_NAVIGATION
            var result = UEArchitectureActions.AddPathFollowing(new Object[] { first, second });

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            foreach (var prefab in new[] { first, second })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab));

                Assert.IsNotNull(asset.GetComponent<UnityEngine.AI.NavMeshAgent>(), asset.name);

                Assert.IsNotNull(asset.GetComponent<PathFollowing>(), asset.name);
            }

            var again = UEArchitectureActions.AddPathFollowing(new Object[] { first, second });

            Assert.IsTrue(again.Ok);

            CollectionAssert.IsEmpty(again.Changed, "Already there.");

            var third = _kit.Prefab("Npc3", typeof(TestPawn));

            Twice_Internal("add-path-following", "targets=" + AssetDatabase.GetAssetPath(third));

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(third)).GetComponent<PathFollowing>());

            CollectionAssert.AreEquivalent(new[] { first, second }, PawnMenus.NavigablePrefabs_Internal(new Object[] { first, second, _kit.Prefab("NotAPawn", typeof(TestHUD)) }));
#else
            var result = UEArchitectureActions.AddPathFollowing(new Object[] { first, second });

            Assert.IsFalse(result.Ok, "Needs Unity's AI module.");
#endif
        }

        [Test]
        public void SnapToNavMesh_SaysWhatStopsIt()
        {
            var prefab = _kit.Prefab("Walker", typeof(TestPawn));

            Assert.IsFalse(UEArchitectureActions.SnapToNavMesh(prefab.GetComponent<Pawn>()).Ok, "Only objects of a level move.");

            var scene = _kit.CreateLevel("NoMesh");

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            var result = UEArchitectureActions.SnapToNavMesh(placed.GetComponent<Pawn>());

            Assert.IsFalse(result.Ok, "No NavMesh here.");

            StringAssert.Contains("NavMesh", result.Problems[0]);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            var cli = UEArchitectureCli.Execute(Parse_Internal("snap-to-navmesh", "target=" + scene.path + "#" + placed.name));

            Assert.IsFalse(cli.Ok);

            Assert.AreEqual("snap-to-navmesh", cli.Verb);
        }

        [Test]
        public void ControlledBy_InALevel_FollowsWhatTheLevelDoes()
        {
            var hero = _kit.Prefab("PlacedHero", typeof(TestPawn));

            var gameMode = _kit.Prefab("PlacedHeroGameMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameMode.GetComponent<GameMode>(), "defaultPawn", hero);

            FrameworkIndex.Invalidate();

            var level = _kit.CreateLevel("PawnControl");

            var placedHero = ((GameObject)PrefabUtility.InstantiatePrefab(hero, level)).GetComponent<Pawn>();

            Assert.AreEqual(PawnControl.Nobody, PawnEditor.ControlledBy(placedHero, out var placedText), "The GameMode spawns its own copy of its Default Pawn: the placed one is not the player's.");

            StringAssert.Contains("its own copy", placedText);

            placedHero.AutoPossessAI = AutoPossessAI.PlacedInWorld;

            Assert.AreEqual(PawnControl.AI, PawnEditor.ControlledBy(placedHero, out _), "A placed copy with Auto Possess AI is the AI's.");

            var spawnedOnly = new GameObject("Spawned Only").AddComponent<TestPawn>();

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedOnly.gameObject, level);

            spawnedOnly.AutoPossessAI = AutoPossessAI.Spawned;

            Assert.AreEqual(PawnControl.Nobody, PawnEditor.ControlledBy(spawnedOnly, out var spawnedText), "The level possesses placed Pawns only with Placed in World.");

            StringAssert.Contains("World.SpawnPawn", spawnedText);

            var asset = _kit.Prefab("SpawnedGuard", typeof(TestPawn)).GetComponent<Pawn>();

            UEArchitectureActions.SetAutoPossess(asset, AutoPossessAI.Spawned);

            Assert.AreEqual(PawnControl.AI, PawnEditor.ControlledBy(asset, out _), "A prefab with Spawned is the AI's when World.SpawnPawn spawns it.");

            Assert.AreEqual(PawnControl.Player, PawnEditor.ControlledBy(hero.GetComponent<Pawn>(), out _), "The Default Pawn prefab itself is the player's.");
        }

        [Test]
        public void ControlledBy_SaysPlayerAIOrNobody()
        {
            var hero = _kit.Prefab("Hero", typeof(TestPawn));

            var gameMode = _kit.Prefab("HeroGameMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameMode.GetComponent<GameMode>(), "defaultPawn", hero);

            FrameworkIndex.Invalidate();

            Assert.AreEqual(PawnControl.Player, PawnEditor.ControlledBy(hero.GetComponent<Pawn>(), out var playerText));

            StringAssert.Contains("HeroGameMode", playerText);

            var guard = _kit.Prefab("Sentry", typeof(TestPawn)).GetComponent<Pawn>();

            Assert.AreEqual(PawnControl.Nobody, PawnEditor.ControlledBy(guard, out _));

            UEArchitectureActions.SetAutoPossess(guard, AutoPossessAI.PlacedInWorld);

            Assert.AreEqual(PawnControl.AI, PawnEditor.ControlledBy(guard, out var aiText));

            StringAssert.Contains("plain AIController", aiText);
        }

        [Test]
        public void AutoPossessPlayer_ThePlayerPossessesThePlacedPawn_InTheToolsToo()
        {
            var hero = _kit.Prefab("DefaultHero", typeof(TestPawn));

            var gameMode = _kit.Prefab("AutoPossessGameMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameMode.GetComponent<GameMode>(), "defaultPawn", hero);

            var level = _kit.CreateLevel("AutoPossess");

            UEArchitectureActions.UseGameModeInLevel(gameMode, level);

            var placed = new GameObject("Player Character").AddComponent<TestPawn>();

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(placed.gameObject, level);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(level);

            ProblemProviders.Invalidate();

            Assert.IsNull(ProblemProviders.ForLevel(level).PlacedPawn, "Off by default.");

            var first = UEArchitectureActions.SetAutoPossessPlayer(placed, 0);

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            Assert.AreEqual(0, placed.AutoPossessPlayer);

            CollectionAssert.IsEmpty(UEArchitectureActions.SetAutoPossessPlayer(placed, 0).Changed, "Already set.");

            Assert.IsFalse(UEArchitectureActions.SetAutoPossessPlayer(placed, 99).Ok, "Players 1 to 8.");

            ProblemProviders.Invalidate();

            var report = ProblemProviders.ForLevel(level);

            Assert.AreSame(placed, report.PlacedPawn, "The overlay and the GameMode panel name it.");

            Assert.AreEqual(1, report.PlayersWithPlacedPawns);

            Assert.IsFalse(report.Problems.Any(problem => problem.FixId == "add-player-starts"), "Player 1 needs no Player Start: it possesses the placed Pawn.");

            Assert.AreEqual(PawnControl.Player, PawnEditor.ControlledBy(placed, out var text));

            StringAssert.Contains("Player 1", text);

            var second = new GameObject("Second Character").AddComponent<TestPawn>();

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(second.gameObject, level);

            UEArchitectureActions.SetAutoPossessPlayer(second, 0);

            ProblemProviders.Invalidate();

            var duplicate = ProblemProviders.For(second).FirstOrDefault(problem => problem.FixId == "clear-auto-possess-player");

            Assert.IsNotNull(duplicate, "Two placed Pawns for Player 1: the second one is reported.");

            Assert.AreEqual(PawnControl.Nobody, PawnEditor.ControlledBy(second, out _));

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(level);

            Twice_Internal("set-auto-possess-player", "target=" + level.path + "#Second Character", "player=2");

            Assert.AreEqual(1, second.AutoPossessPlayer, "player=2 is Player 2.");

            Twice_Internal("set-auto-possess-player", "target=" + level.path + "#Second Character", "player=none");

            Assert.AreEqual(-1, second.AutoPossessPlayer);

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("set-auto-possess-player", "target=" + level.path + "#Second Character", "player=0")).Ok, "Players count from 1.");
        }

        [Test]
        public void AutoPossessPlayer_TheToolsSayWhatTheRuntimeDoes_WithNoPlayersAtStartAndWithAutoPossessAI()
        {
            var gameMode = _kit.Prefab("NoPlayersGameMode", typeof(TestGameMode));

            var assigned = UEArchitectureActions.Assign(gameMode.GetComponent<GameMode>(), "playersAtStart", 0);

            Assert.IsTrue(assigned.Ok, string.Join("\n", assigned.Problems));

            Assert.AreEqual(0, gameMode.GetComponent<GameMode>().PlayersAtStart);

            var level = _kit.CreateLevel("NoPlayersAtStart");

            UEArchitectureActions.UseGameModeInLevel(gameMode, level);

            var placed = new GameObject("Waiting Character").AddComponent<TestPawn>();

            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(placed.gameObject, level);

            placed.AutoPossessPlayer = 0;

            placed.AutoPossessAI = AutoPossessAI.PlacedInWorld;

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(level);

            ProblemProviders.Invalidate();

            var report = ProblemProviders.ForLevel(level);

            Assert.AreSame(placed, report.PlacedPawn);

            Assert.IsTrue(report.PlacedPawnWaits, "0 players at start is 0, not 1: Player 1 joins only with AddPlayer.");

            Assert.AreEqual(0, report.PlayersWithPlacedPawns);

            var problems = ProblemProviders.For(placed).ToList();

            Assert.IsTrue(problems.Any(problem => problem.Message.Contains("Waits for Player 1 to join") && problem.Message.Contains("no players")),
                          string.Join("\n", problems.Select(problem => problem.Message)));

            Assert.IsFalse(problems.Any(problem => problem.FixId == "create-ai-controller"), "The runtime never gives it to its AI: no AI Controller warning.");

            Assert.IsTrue(problems.Any(problem => problem.Message.Contains("Auto Possess AI does nothing here")));

            Assert.AreEqual(PawnControl.Player, PawnEditor.ControlledBy(placed, out var text), "The player wins over Auto Possess AI.");

            StringAssert.Contains("waits for AddPlayer", text);
        }

        private static ActionResult Twice_Internal(string verb, params string[] pairs)
        {
            var first = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(first.Ok, $"{verb}: {string.Join("\n", first.Problems)}");

            Assert.AreEqual(verb, first.Verb);

            var second = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(second.Ok, $"{verb} again: {string.Join("\n", second.Problems)}");

            CollectionAssert.IsEmpty(second.Created, $"{verb}: the second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed, $"{verb}: the second run changes nothing.");

            return first;
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }
}
