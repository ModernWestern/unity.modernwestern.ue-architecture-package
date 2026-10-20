using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernWestern.UEArchitecture.Tests
{
    public class AIControllerTests : FrameworkTestBase
    {
        private WorldSettings _worldSettings;

        private TestAI _aiTemplate;

        private TestPawn _enemyTemplate;

        [SetUp]
        public void BuildTemplates()
        {
            var mode = Template<TestGameMode>("Mode");

            Set(mode, "defaultPawn", Template<TestPawn>("Hero"));

            _worldSettings = Create<WorldSettings>("World Settings");

            Set(_worldSettings, "gameModeOverride", mode);

            _aiTemplate = Template<TestAI>("Enemy AI");

            _enemyTemplate = Template<TestPawn>("Enemy");

            Set(_enemyTemplate, "aiController", _aiTemplate);
        }

        private TestPawn PlacedEnemy(AutoPossessAI autoPossess)
        {
            var enemy = Create<TestPawn>("Placed Enemy");

            Set(enemy, "aiController", _aiTemplate);

            enemy.AutoPossessAI = autoPossess;

            return enemy;
        }

        [UnityTest]
        public IEnumerator APawnPlacedInTheLevel_GetsItsAIController()
        {
            var enemy = PlacedEnemy(AutoPossessAI.PlacedInWorld);

            var level = OpenLevel(_worldSettings, Create<PlayerStart>("Start"), enemy);

            yield return null;

            var ai = enemy.GetController<TestAI>();

            Assert.IsNotNull(ai, "Auto Possess AI creates the controller from the Pawn's AI Controller prefab.");

            Assert.IsTrue(enemy.IsAIControlled);

            Assert.IsFalse(enemy.IsPlayerControlled);

            Assert.AreEqual(1, ai.Possessions);

            Assert.AreEqual(1, ai.BeginPlays);

            Assert.AreEqual(level, ai.gameObject.scene, "Spawned into the level.");

            Assert.AreEqual(1, World.AIControllers.Count);

            Assert.AreSame(ai, World.AIControllers[0]);

            Assert.IsNull(ai.PlayerState, "No PlayerState unless the AI asks for one.");
        }

        [UnityTest]
        public IEnumerator Disabled_LeavesThePawnAlone()
        {
            var enemy = PlacedEnemy(AutoPossessAI.Disabled);

            OpenLevel(_worldSettings, Create<PlayerStart>("Start"), enemy);

            yield return null;

            Assert.IsNull(enemy.Controller);

            Assert.AreEqual(0, World.AIControllers.Count);
        }

        [UnityTest]
        public IEnumerator SpawnPawn_PossessesOnlyWhenAutoPossessIncludesSpawned()
        {
            OpenLevel(_worldSettings, Create<PlayerStart>("Start"));

            yield return null;

            _enemyTemplate.AutoPossessAI = AutoPossessAI.Spawned;

            var spawned = World.SpawnPawn(_enemyTemplate, new Vector3(2f, 0f, 2f), Quaternion.identity);

            Assert.IsInstanceOf<TestAI>(spawned.Controller);

            Assert.AreEqual(new Vector3(2f, 0f, 2f), spawned.transform.position);

            Assert.AreEqual(World.Scene, spawned.gameObject.scene);

            _enemyTemplate.AutoPossessAI = AutoPossessAI.PlacedInWorld;

            Assert.IsNull(World.SpawnPawn(_enemyTemplate, Vector3.zero, Quaternion.identity).Controller);
        }

        [UnityTest]
        public IEnumerator ABot_GetsAPlayerState_AndLeavesWithItsPawn()
        {
            Set(_aiTemplate, "wantsPlayerState", true);

            var enemy = PlacedEnemy(AutoPossessAI.PlacedInWorld);

            OpenLevel(_worldSettings, Create<PlayerStart>("Start"), enemy);

            yield return null;

            var ai = enemy.GetController<TestAI>();

            var bot = ai.GetPlayerState<TestPlayerState>();

            Assert.IsNotNull(bot, "A bot gets the GameMode's PlayerState class.");

            Assert.IsTrue(bot.IsBot);

            Assert.AreEqual("Bot 1", bot.PlayerName);

            Assert.AreEqual(2, World.GameState.Players.Count, "The player and the bot.");

            Assert.AreSame(bot, enemy.PlayerState);

            IndexOf("Bot 1.BeginPlay");

            Object.Destroy(enemy.gameObject);

            yield return null;

            yield return null;

            Assert.IsTrue(ai == null, "The AI controller goes with its Pawn.");

            Assert.AreEqual(1, World.GameState.Players.Count);

            Assert.AreEqual(0, World.AIControllers.Count);

            IndexOf("Bot 1.EndPlay");
        }

        [UnityTest]
        public IEnumerator APlayer_CanTakeAPawnFromAnAI()
        {
            var enemy = PlacedEnemy(AutoPossessAI.PlacedInWorld);

            OpenLevel(_worldSettings, Create<PlayerStart>("Start"), enemy);

            yield return null;

            var ai = enemy.GetController<TestAI>();

            var player = World.GetPlayer();

            player.Possess(enemy);

            Assert.IsNull(ai.Pawn);

            Assert.AreSame(player, enemy.Controller);

            Assert.IsTrue(enemy.IsPlayerControlled);
        }

        [UnityTest]
        public IEnumerator TheEndOfTheLevel_EndsTheAIs()
        {
            OpenLevel(_worldSettings, Create<PlayerStart>("Start"), PlacedEnemy(AutoPossessAI.PlacedInWorld));

            yield return null;

            TestGate.Log.Clear();

            World.End_Internal();

            IndexOf("AI.EndPlay");

            Assert.AreEqual(0, World.AIControllers.Count);
        }
    }
}
