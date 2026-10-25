using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using ModernWestern.UI.ObserverVM.EditorTools;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// BindingPreview.Predict against the runtime's FrameworkViewModels.Resolve: the same framework objects are built
    /// for real (a GameMode with its GameState, a player with PlayerState, Pawn and HUD, a level subsystem, a game
    /// subsystem, the GameInstance) and every ViewModel type must land on the same slot.
    /// </summary>
    public class BindingPreviewTests
    {
        private readonly List<Object> _created = new();

        private static readonly Type[] Probes =
        {
            typeof(TestPlayerState), typeof(PlayerState), typeof(TestPawn), typeof(Pawn), typeof(TestController), typeof(PlayerController), typeof(Controller), typeof(TestHUD),
            typeof(HUD), typeof(TestGameState), typeof(GameState), typeof(TestGameMode), typeof(GameMode), typeof(LevelSubsystem), typeof(WorldSubsystem), typeof(FirstSubsystem),
            typeof(DependentSubsystem), typeof(GameInstanceSubsystem), typeof(TestGameInstance), typeof(GameInstance), typeof(MonoBehaviour), typeof(Component), typeof(object),
            typeof(string), typeof(TestLevelGameMode)
        };

        [TearDown]
        public void TearDown()
        {
            SetStatic_Internal(typeof(World), "GameMode", null);

            SetStatic_Internal(typeof(GameFramework), "Instance", null);

            World.Subsystems.Clear();

            GameFramework.GameSubsystems.Clear();

            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
        }

        [Test]
        public void TheOrder_IsResolvesOrder()
        {
            CollectionAssert.AreEqual(new[]
            {
                FrameworkSlot.PlayerState, FrameworkSlot.Pawn, FrameworkSlot.PlayerController, FrameworkSlot.HUD, FrameworkSlot.GameState, FrameworkSlot.GameMode,
                FrameworkSlot.WorldSubsystem, FrameworkSlot.GameInstanceSubsystem, FrameworkSlot.GameInstance
            }, BindingPreview.Order);
        }

        [TestCase(true, true, true, TestName = "Predict equals Resolve: a full level")]
        [TestCase(true, false, false, TestName = "Predict equals Resolve: no Pawn, no HUD")]
        [TestCase(false, false, false, TestName = "Predict equals Resolve: no GameMode, no player")]
        public void Predict_EqualsResolve(bool withGameMode, bool withPawn, bool withHud)
        {
            var live = new Dictionary<FrameworkSlot, object>();

            var set = new FrameworkTypeSet { HasPlayer = withGameMode };

            PlayerController player = null;

            if (withGameMode)
            {
                var gameMode = Make_Internal<TestGameMode>("Mode");

                var gameState = new TestGameState();

                typeof(GameMode).GetProperty("GameState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).GetSetMethod(true).Invoke(gameMode, new object[] { gameState });

                SetStatic_Internal(typeof(World), "GameMode", gameMode);

                player = Make_Internal<TestController>("Player");

                var playerState = new TestPlayerState();

                player.SetPlayerState_Internal(playerState);

                live[FrameworkSlot.GameMode] = gameMode;

                live[FrameworkSlot.GameState] = gameState;

                live[FrameworkSlot.PlayerState] = playerState;

                live[FrameworkSlot.PlayerController] = player;

                set.GameMode = typeof(TestGameMode);

                set.GameState = GameModeInfo.GameStateType(typeof(TestGameMode));

                set.PlayerState = GameModeInfo.PlayerStateType(typeof(TestGameMode));

                set.PlayerController = typeof(TestController);

                if (withPawn)
                {
                    var pawn = Make_Internal<TestPawn>("Pawn");

                    typeof(Controller).GetField("_pawn", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, pawn);

                    live[FrameworkSlot.Pawn] = pawn;

                    set.Pawn = typeof(TestPawn);
                }

                if (withHud)
                {
                    var hud = Make_Internal<TestHUD>("HUD");

                    player.SetHUD_Internal(hud);

                    live[FrameworkSlot.HUD] = hud;

                    set.HUD = typeof(TestHUD);
                }
            }

            var levelSubsystem = new LevelSubsystem();

            Insert_Internal(World.Subsystems, levelSubsystem);

            var gameSubsystem = new FirstSubsystem();

            Insert_Internal(GameFramework.GameSubsystems, gameSubsystem);

            var gameInstance = Make_Internal<TestGameInstance>("Game Instance");

            SetStatic_Internal(typeof(GameFramework), "Instance", gameInstance);

            live[FrameworkSlot.GameInstance] = gameInstance;

            set.GameInstance = typeof(TestGameInstance);

            set.WorldSubsystems = new[] { typeof(LevelSubsystem) };

            set.GameInstanceSubsystems = new[] { typeof(FirstSubsystem) };

            foreach (var probe in Probes)
            {
                var resolved = FrameworkViewModels.Resolve(probe, player);

                var expected = resolved == null ? FrameworkSlot.None
                               : ReferenceEquals(resolved, levelSubsystem) ? FrameworkSlot.WorldSubsystem
                               : ReferenceEquals(resolved, gameSubsystem) ? FrameworkSlot.GameInstanceSubsystem
                               : live.First(pair => ReferenceEquals(pair.Value, resolved)).Key;

                var predicted = BindingPreview.Predict(probe, set);

                Assert.AreEqual(expected, predicted.Slot, $"{probe.Name}: Resolve gives {expected}, Predict says {predicted.Slot}.");

                if (resolved != null)
                {
                    Assert.AreEqual(resolved.GetType(), predicted.Type, probe.Name);
                }
            }
        }

        /// <summary>
        /// A HUD binds its widgets in GameMode.AddPlayer before RestartPlayer spawns the Pawn, and only widgets of a Pawn
        /// class are bound again when the Pawn arrives: PredictForHud must match Resolve with no Pawn yet, except for those.
        /// </summary>
        [Test]
        public void PredictForHud_EqualsResolve_BeforeThePawnExists()
        {
            var gameMode = Make_Internal<TestGameMode>("Mode");

            var gameState = new TestGameState();

            typeof(GameMode).GetProperty("GameState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).GetSetMethod(true).Invoke(gameMode, new object[] { gameState });

            SetStatic_Internal(typeof(World), "GameMode", gameMode);

            var player = Make_Internal<TestController>("Player");

            var playerState = new TestPlayerState();

            player.SetPlayerState_Internal(playerState);

            var hud = Make_Internal<TestHUD>("HUD");

            player.SetHUD_Internal(hud);

            var gameInstance = Make_Internal<TestGameInstance>("Game Instance");

            SetStatic_Internal(typeof(GameFramework), "Instance", gameInstance);

            var levelSubsystem = new LevelSubsystem();

            Insert_Internal(World.Subsystems, levelSubsystem);

            var gameSubsystem = new FirstSubsystem();

            Insert_Internal(GameFramework.GameSubsystems, gameSubsystem);

            var live = new Dictionary<object, FrameworkSlot>
            {
                [gameMode] = FrameworkSlot.GameMode, [gameState] = FrameworkSlot.GameState, [player] = FrameworkSlot.PlayerController, [playerState] = FrameworkSlot.PlayerState,
                [hud] = FrameworkSlot.HUD, [gameInstance] = FrameworkSlot.GameInstance, [levelSubsystem] = FrameworkSlot.WorldSubsystem, [gameSubsystem] = FrameworkSlot.GameInstanceSubsystem
            };

            var set = new FrameworkTypeSet
            {
                GameMode = typeof(TestGameMode), GameState = GameModeInfo.GameStateType(typeof(TestGameMode)), PlayerState = GameModeInfo.PlayerStateType(typeof(TestGameMode)),
                PlayerController = typeof(TestController), HUD = typeof(TestHUD), Pawn = typeof(TestPawn), GameInstance = typeof(TestGameInstance),
                WorldSubsystems = new[] { typeof(LevelSubsystem) }, GameInstanceSubsystems = new[] { typeof(FirstSubsystem) }
            };

            foreach (var probe in Probes)
            {
                // The Pawn does not exist yet when the HUD binds.
                var resolved = FrameworkViewModels.Resolve(probe, player);

                var rebound = typeof(Pawn).IsAssignableFrom(probe) && probe.IsAssignableFrom(typeof(TestPawn));

                var expected = rebound ? FrameworkSlot.Pawn : resolved == null ? FrameworkSlot.None : live[resolved];

                Assert.AreEqual(expected, BindingPreview.PredictForHud(probe, set).Slot, $"{probe.Name} in a HUD: the runtime gives {expected}.");
            }

            Assert.AreEqual(FrameworkSlot.Pawn, BindingPreview.Predict(typeof(Component), set).Slot, "Outside a HUD (a binder binds once the level began) the Pawn is there.");

            Assert.AreEqual(FrameworkSlot.PlayerController, BindingPreview.PredictForHud(typeof(Component), set).Slot);
        }

        [Test]
        public void TheSetOfAGameMode_ReadsItsClasses()
        {
            var gameMode = Make_Internal<TestGameMode>("Mode");

            var pawn = Make_Internal<TestPawn>("Hero");

            var hud = Make_Internal<TestHUD>("HUD");

            typeof(GameMode).GetField("defaultPawn", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(gameMode, pawn);

            typeof(GameMode).GetField("hud", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(gameMode, hud);

            var set = FrameworkTypeSet.ForGameMode(gameMode);

            Assert.AreEqual(typeof(TestGameState), set.GameState);

            Assert.AreEqual(typeof(TestPlayerState), set.PlayerState);

            Assert.AreEqual(typeof(PlayerController), set.PlayerController, "An empty slot is a plain PlayerController.");

            Assert.AreEqual(typeof(TestPawn), set.Pawn);

            Assert.AreEqual(typeof(TestHUD), set.HUD);

            CollectionAssert.Contains(set.WorldSubsystems, typeof(LevelSubsystem));

            CollectionAssert.Contains(set.GameInstanceSubsystems, typeof(FirstSubsystem));

            Assert.AreEqual(FrameworkSlot.Pawn, BindingPreview.Predict(typeof(TestPawn), set).Slot);

            Assert.AreEqual(FrameworkSlot.GameInstanceSubsystem, BindingPreview.Predict(typeof(FirstSubsystem), set).Slot);

            Assert.AreEqual(FrameworkSlot.WorldSubsystem, BindingPreview.Predict(typeof(LevelSubsystem), set).Slot);

            Assert.AreEqual(FrameworkSlot.None, BindingPreview.Predict(typeof(WorldSubsystem), set).Slot, "Subsystems are found by their exact class.");

            Assert.AreEqual(FrameworkSlot.None, BindingPreview.Predict(typeof(TestPawn), set.WithoutPlayer()).Slot, "No player, no Pawn.");

            Assert.AreEqual(FrameworkSlot.GameState, BindingPreview.Predict(typeof(TestGameState), set.WithoutPlayer()).Slot, "The GameState needs no player.");

            StringAssert.Contains("Default Pawn of Mode", BindingPreview.Describe(BindingPreview.Predict(typeof(TestPawn), set), set));

            Assert.IsTrue(BindingPreview.IsFrameworkViewModel(typeof(TestPlayerState)));

            Assert.IsTrue(BindingPreview.IsFrameworkViewModel(typeof(Pawn)));

            Assert.IsFalse(BindingPreview.IsFrameworkViewModel(typeof(string)));

            Assert.IsFalse(BindingPreview.IsFrameworkViewModel(typeof(object)));
        }

        [Test]
        public void TheCatalogProvider_ListsTheFrameworkClasses()
        {
            var withTests = new FrameworkViewModelCatalogProvider(true).ViewModels().ToList();

            CollectionAssert.IsSubsetOf(new[] { typeof(GameState), typeof(PlayerState), typeof(TestGameState), typeof(TestPlayerState), typeof(FirstSubsystem), typeof(LevelSubsystem) }, withTests);

            CollectionAssert.DoesNotContain(withTests, typeof(TestPawn), "Unity objects are not ViewModels for the pickers.");

            var registered = new FrameworkViewModelCatalogProvider().ViewModels().ToList();

            CollectionAssert.Contains(registered, typeof(GameState));

            CollectionAssert.DoesNotContain(registered, typeof(TestGameState), "Test classes stay out of the pickers.");

            ViewModelCatalog.Refresh();

            var entries = ViewModelCatalog.All.Where(entry => entry.Group == FrameworkViewModelCatalogProvider.GroupName).Select(entry => entry.ViewModelType).ToList();

            CollectionAssert.Contains(entries, typeof(PlayerState), "The provider is registered.");
        }

        private T Make_Internal<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);

            _created.Add(gameObject);

            return gameObject.AddComponent<T>();
        }

        private static void SetStatic_Internal(Type type, string property, object value)
        {
            type.GetProperty(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetSetMethod(true).Invoke(null, new[] { value });
        }

        /// <summary>
        /// Puts one subsystem in a collection the way Create does, without creating every subsystem of the project.
        /// </summary>
        private static void Insert_Internal<TBase>(SubsystemCollection<TBase> collection, TBase subsystem) where TBase : Subsystem
        {
            var type = collection.GetType();

            var byIndex = (Subsystem[])type.GetField("_byIndex", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(collection);

            var index = SubsystemIndex.Of(subsystem.GetType());

            if (index >= byIndex.Length)
            {
                Array.Resize(ref byIndex, Math.Max(index + 1, SubsystemIndex.Count));

                type.GetField("_byIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(collection, byIndex);
            }

            byIndex[index] = subsystem;

            ((List<TBase>)type.GetField("_ordered", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(collection)).Add(subsystem);
        }
    }
}
