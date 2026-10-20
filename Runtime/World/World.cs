using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The level being played, like Unreal's UWorld. A level begins when its scene finishes loading (before any Start) and
    /// ends when it unloads. Scenes streamed in additively (<see cref="Levels.Stream"/>) join the current level.
    /// <para>From Start on you can use everything here: the GameMode, the GameState, the players and the level's subsystems.</para>
    /// </summary>
    public static class World
    {
        internal static readonly SubsystemCollection<WorldSubsystem> Subsystems = new();

        private static readonly List<PlayerController> PlayersList = new();

        private static double _beginTime;

        /// <summary>
        /// Raised when a level begins play: its subsystems exist and its GameMode spawned the players. Like Unreal's BeginPlay.
        /// </summary>
        public static event Action BeganPlay;

        /// <summary>
        /// Raised when the level is about to end (another level opens, or the game quits). Everything is still alive here.
        /// </summary>
        public static event Action EndingPlay;

        /// <summary>
        /// True while a level is being played.
        /// </summary>
        public static bool HasBegunPlay { get; private set; }

        /// <summary>
        /// The scene of the level being played.
        /// </summary>
        public static Scene Scene { get; private set; }

        /// <summary>
        /// Seconds since the level began (scaled by Time.timeScale). Like Unreal's GetTimeSeconds.
        /// </summary>
        public static float TimeSeconds => HasBegunPlay ? (float)(Time.timeAsDouble - _beginTime) : 0f;

        /// <summary>
        /// The level's GameMode, or null if the level has none.
        /// </summary>
        public static GameMode GameMode { get; private set; }

        /// <summary>
        /// The level's GameState, or null if the level has no GameMode.
        /// </summary>
        public static GameState GameState => GameMode != null ? GameMode.GameState : null;

        /// <summary>
        /// The players of the level (one per PlayerController), in the order they joined.
        /// </summary>
        public static ListView<PlayerController> Players => new(PlayersList);

        /// <summary>
        /// Every subsystem of the level, in initialization order.
        /// </summary>
        public static ListView<WorldSubsystem> AllSubsystems => Subsystems.All;

        /// <summary>
        /// A subsystem of the current level, e.g. <c>World.Get&lt;EnemyRegistry&gt;()</c>. An array read: call it as often as you like.
        /// Null outside a level (before the first one begins, between levels).
        /// </summary>
        public static T Get<T>() where T : WorldSubsystem
        {
            return Subsystems.Get<T>();
        }

        /// <summary>
        /// The level's GameMode as your class, e.g. <c>World.GetGameMode&lt;ArenaMode&gt;()</c>. Null if it is not that class.
        /// </summary>
        public static T GetGameMode<T>() where T : GameMode
        {
            return GameMode as T;
        }

        /// <summary>
        /// The level's GameState as your class. Null if it is not that class.
        /// </summary>
        public static T GetGameState<T>() where T : GameState
        {
            return GameState as T;
        }

        /// <summary>
        /// The PlayerController of a player, 0 = the first one. Null if there is no such player.
        /// </summary>
        public static PlayerController GetPlayer(int index = 0)
        {
            return (uint)index < (uint)PlayersList.Count ? PlayersList[index] : null;
        }

        /// <summary>
        /// The PlayerState of a player as your class. Null if there is no such player or it is not that class.
        /// </summary>
        public static T GetPlayerState<T>(int index = 0) where T : PlayerState
        {
            var player = GetPlayer(index);

            return player != null ? player.PlayerState as T : null;
        }

        /// <summary>
        /// The Pawn a player controls, as your class. Null if there is no such player, it controls nothing or it is not that class.
        /// </summary>
        public static T GetPawn<T>(int index = 0) where T : Pawn
        {
            var player = GetPlayer(index);

            return player != null ? player.Pawn as T : null;
        }

        internal static void AddPlayer_Internal(PlayerController player)
        {
            PlayersList.Add(player);
        }

        internal static void RemovePlayer_Internal(PlayerController player)
        {
            PlayersList.Remove(player);
        }

        internal static void Begin_Internal(Scene scene)
        {
            if (HasBegunPlay)
            {
                End_Internal();
            }

            Scene = scene;

            _beginTime = Time.timeAsDouble;

            Subsystems.Create();

            GameMode = GameMode.Spawn_Internal(scene);

            HasBegunPlay = true;

            if (GameMode != null)
            {
                Guard_Internal(GameMode.StartPlay_Internal);
            }

            foreach (var subsystem in Subsystems.All)
            {
                Guard_Internal(subsystem.WorldBeginPlay_Internal);
            }

            Raise_Internal(BeganPlay);
        }

        /// <summary>
        /// True between <see cref="EndingPlay"/> and the moment the level's scene is unloaded.
        /// </summary>
        public static bool IsEnding { get; private set; }

        /// <summary>
        /// Ends the level completely (quitting, or a scene loaded without Levels.Open).
        /// </summary>
        internal static void End_Internal()
        {
            EndPlay_Internal();

            Teardown_Internal();
        }

        /// <summary>
        /// First half of the end: EndingPlay, GameMode/GameState/PlayerState EndPlay, level timers. Levels.Open calls it
        /// right before the new scene replaces the old one, while the old objects are still alive.
        /// </summary>
        internal static void EndPlay_Internal()
        {
            if (!HasBegunPlay || IsEnding)
            {
                return;
            }

            IsEnding = true;

            Raise_Internal(EndingPlay);

            // If the scene was unloaded directly (SceneManager.LoadScene), the GameMode's GameObject is already destroyed,
            // but its GameState and PlayerStates (plain objects) still need their EndPlay.
            if (!ReferenceEquals(GameMode, null))
            {
                Guard_Internal(GameMode.EndPlay_Internal);
            }

            Timers.ClearLevelTimers_Internal();
        }

        /// <summary>
        /// Second half: the level's subsystems go away. Runs once its scene is unloaded, so its objects never see them missing.
        /// </summary>
        internal static void Teardown_Internal()
        {
            if (!HasBegunPlay)
            {
                return;
            }

            Subsystems.Deinitialize();

            Subsystems.Clear();

            PlayersList.Clear();

            GameMode = null;

            HasBegunPlay = false;

            IsEnding = false;

            Scene = default;
        }

        internal static void Reset_Internal()
        {
            Subsystems.Clear();

            PlayersList.Clear();

            GameMode = null;

            HasBegunPlay = false;

            IsEnding = false;

            Scene = default;

            BeganPlay = null;

            EndingPlay = null;
        }

        private static void Guard_Internal(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void Raise_Internal(Action handlers)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
