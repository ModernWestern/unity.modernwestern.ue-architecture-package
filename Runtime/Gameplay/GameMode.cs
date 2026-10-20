using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The rules of a level, like Unreal's AGameModeBase. Put it on a prefab, fill in its default classes (PlayerController,
    /// Pawn and HUD prefabs) and choose it per level in World Settings, or as the project default in Project Settings.
    /// When the level begins it creates the GameState, then one PlayerController + PlayerState + HUD per player, and spawns
    /// each player's Pawn at a PlayerStart.
    /// <para>Derive from <see cref="GameMode{TGameState,TPlayerState}"/> to use your own GameState and PlayerState classes.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class GameMode : MonoBehaviour
    {
        [Header("Default classes")]
        [SerializeField, Tooltip("Prefab with the PlayerController component. Empty = a plain PlayerController.")]
        private PlayerController playerController;

        [SerializeField, Tooltip("Prefab spawned for each player at a PlayerStart. Empty = players start without a Pawn.")]
        private Pawn defaultPawn;

        [SerializeField, Tooltip("Prefab with each player's interface. Empty = no HUD.")]
        private HUD hud;

        [Header("Players")]
        [SerializeField, Min(0), Tooltip("Players created when the level begins. Add more later with AddPlayer().")]
        private int playersAtStart = 1;

        private readonly List<PlayerController> _ownedPlayers = new();

        private int _nextPlayerId;

        private bool _hasStartedPlay;

        public GameState GameState { get; private set; }

        public bool HasStartedPlay => _hasStartedPlay;

        public PlayerController PlayerControllerPrefab => playerController;

        public Pawn DefaultPawnPrefab => defaultPawn;

        public HUD HUDPrefab => hud;

        public int PlayersAtStart => playersAtStart;

        /// <summary>
        /// Adds a player now (e.g. a second local player joining): PlayerController, PlayerState, HUD and Pawn.
        /// Like Unreal's Login + PostLogin.
        /// </summary>
        public PlayerController AddPlayer()
        {
            var index = World.Players.Count;

            var player = SpawnPrefab_Internal(playerController, "PlayerController");

            if (player == null)
            {
                player = new GameObject().AddComponent<PlayerController>();

                MoveToLevel_Internal(player.gameObject);
            }

            player.name = "PlayerController " + index;

            var playerState = CreatePlayerState() ?? new PlayerState();

            playerState.Init_Internal(player, ++_nextPlayerId, "Player " + (index + 1));

            player.Init_Internal(playerState, index);

            _ownedPlayers.Add(player);

            World.AddPlayer_Internal(player);

            GameState.AddPlayer_Internal(playerState);

            var playerHud = SpawnPrefab_Internal(hud, "HUD");

            if (playerHud != null)
            {
                player.SetHUD_Internal(playerHud);

                Guard_Internal(playerHud.Init_Internal, player);
            }

            RestartPlayer(player);

            Guard_Internal(OnPostLogin, player);

            if (_hasStartedPlay)
            {
                BeginPlayFor_Internal(player);
            }

            return player;
        }

        /// <summary>
        /// Removes a player: its PlayerController, HUD and Pawn are destroyed. Like Unreal's Logout.
        /// </summary>
        public void RemovePlayer(PlayerController player)
        {
            if (player == null || !_ownedPlayers.Remove(player))
            {
                return;
            }

            Guard_Internal(OnLogout, player);

            EndPlayFor_Internal(player);

            GameState.RemovePlayer_Internal(player.PlayerState);

            World.RemovePlayer_Internal(player);

            if (player.Pawn != null)
            {
                Destroy(player.Pawn.gameObject);
            }

            if (player.HUD != null)
            {
                Destroy(player.HUD.gameObject);
            }

            Destroy(player.gameObject);
        }

        /// <summary>
        /// Puts a player back in the game: spawns its default Pawn at a PlayerStart if it has none (e.g. after dying), or
        /// moves its current Pawn there. Like Unreal's RestartPlayer.
        /// </summary>
        public virtual void RestartPlayer(PlayerController player)
        {
            if (player == null || (defaultPawn == null && player.Pawn == null))
            {
                return;
            }

            var start = FindPlayerStart(player);

            if (player.Pawn != null)
            {
                if (start != null)
                {
                    player.Pawn.transform.SetPositionAndRotation(start.transform.position, start.transform.rotation);
                }

                return;
            }

            var pawn = SpawnDefaultPawnFor(player, start);

            if (pawn != null)
            {
                player.Possess(pawn);
            }
        }

        /// <summary>
        /// Creates the GameState. <see cref="GameMode{TGameState,TPlayerState}"/> overrides it with your class.
        /// </summary>
        protected virtual GameState CreateGameState()
        {
            return new GameState();
        }

        /// <summary>
        /// Creates the PlayerState of a new player. <see cref="GameMode{TGameState,TPlayerState}"/> overrides it with your class.
        /// </summary>
        protected virtual PlayerState CreatePlayerState()
        {
            return new PlayerState();
        }

        /// <summary>
        /// Called when the level begins, after the GameState was created and before any player exists. Like Unreal's InitGame.
        /// </summary>
        protected virtual void InitGame()
        {
        }

        /// <summary>
        /// Called once the first players were spawned: the match starts. Like Unreal's StartPlay.
        /// </summary>
        protected virtual void StartPlay()
        {
        }

        /// <summary>
        /// Called when the level ends. Everything is still alive here.
        /// </summary>
        protected virtual void OnEndPlay()
        {
        }

        /// <summary>
        /// A player joined (its Pawn is already spawned). Like Unreal's PostLogin.
        /// </summary>
        protected virtual void OnPostLogin(PlayerController player)
        {
        }

        /// <summary>
        /// A player is about to be removed. Like Unreal's Logout.
        /// </summary>
        protected virtual void OnLogout(PlayerController player)
        {
        }

        /// <summary>
        /// Picks where a player spawns. By default: the PlayerStarts in turn (player 1 the first, player 2 the second...).
        /// Return null to spawn at the GameMode's position.
        /// </summary>
        protected virtual PlayerStart FindPlayerStart(PlayerController player)
        {
            var starts = PlayerStart.All;

            return starts.Count > 0 ? starts[player.PlayerIndex % starts.Count] : null;
        }

        /// <summary>
        /// Spawns the default Pawn for a player at a start. Override it to pick a different prefab per player.
        /// </summary>
        protected virtual Pawn SpawnDefaultPawnFor(PlayerController player, PlayerStart start)
        {
            if (defaultPawn == null)
            {
                return null;
            }

            var origin = start != null ? start.transform : transform;

            var pawn = Instantiate(defaultPawn, origin.position, origin.rotation);

            pawn.name = defaultPawn.name;

            MoveToLevel_Internal(pawn.gameObject);

            return pawn;
        }

        internal static GameMode Spawn_Internal(Scene scene)
        {
            // A GameMode placed in the scene wins; then the World Settings override; then the project default.
            var gameMode = WorldSettings.FindInScene_Internal<GameMode>(scene);

            if (gameMode == null)
            {
                var worldSettings = WorldSettings.FindInScene_Internal<WorldSettings>(scene);

                var prefab = worldSettings != null && worldSettings.GameModeOverride != null
                                 ? worldSettings.GameModeOverride
                                 : GameFramework.Settings != null ? GameFramework.Settings.DefaultGameMode : null;

                if (prefab == null)
                {
                    return null;
                }

                gameMode = Instantiate(prefab);

                gameMode.name = prefab.name;

                if (scene.IsValid() && scene.isLoaded && gameMode.gameObject.scene != scene)
                {
                    SceneManager.MoveGameObjectToScene(gameMode.gameObject, scene);
                }
            }

            gameMode.InitGame_Internal();

            return gameMode;
        }

        internal void StartPlay_Internal()
        {
            for (var i = 0; i < playersAtStart; i++)
            {
                AddPlayer();
            }

            _hasStartedPlay = true;

            Guard_Internal(StartPlay);

            Guard_Internal(GameState.BeginPlay_Internal);

            foreach (var player in _ownedPlayers)
            {
                BeginPlayFor_Internal(player);
            }
        }

        internal void EndPlay_Internal()
        {
            Guard_Internal(OnEndPlay);

            for (var i = _ownedPlayers.Count - 1; i >= 0; i--)
            {
                EndPlayFor_Internal(_ownedPlayers[i]);
            }

            if (GameState != null)
            {
                Guard_Internal(GameState.EndPlay_Internal);
            }

            _hasStartedPlay = false;
        }

        private void InitGame_Internal()
        {
            _ownedPlayers.Clear();

            _nextPlayerId = 0;

            _hasStartedPlay = false;

            GameState = CreateGameState() ?? new GameState();

            GameState.Init_Internal(this);

            Guard_Internal(InitGame);
        }

        private static void BeginPlayFor_Internal(PlayerController player)
        {
            Guard_Internal(player.PlayerState.BeginPlay_Internal);

            if (player != null)
            {
                Guard_Internal(player.BeginPlay_Internal);
            }
        }

        private static void EndPlayFor_Internal(PlayerController player)
        {
            // The controller may already be destroyed (scene unloaded directly): its PlayerState still gets EndPlay.
            if (player != null)
            {
                Guard_Internal(player.EndPlay_Internal);
            }

            if (!ReferenceEquals(player, null) && player.PlayerState != null)
            {
                Guard_Internal(player.PlayerState.EndPlay_Internal);
            }
        }

        private T SpawnPrefab_Internal<T>(T prefab, string fallbackName) where T : Component
        {
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab);

            instance.name = string.IsNullOrEmpty(prefab.name) ? fallbackName : prefab.name;

            MoveToLevel_Internal(instance.gameObject);

            return instance;
        }

        private static void MoveToLevel_Internal(GameObject gameObject)
        {
            var level = World.Scene;

            if (level.IsValid() && level.isLoaded && gameObject.scene != level && gameObject.transform.parent == null)
            {
                SceneManager.MoveGameObjectToScene(gameObject, level);
            }
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

        private static void Guard_Internal(Action<PlayerController> action, PlayerController player)
        {
            try
            {
                action(player);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, player);
            }
        }
    }

    /// <summary>
    /// A GameMode that creates your own GameState and PlayerState classes:
    /// <code>public sealed class ArenaMode : GameMode&lt;ArenaState, ArenaPlayerState&gt; { }</code>
    /// </summary>
    public abstract class GameMode<TGameState, TPlayerState> : GameMode where TGameState : GameState, new() where TPlayerState : PlayerState, new()
    {
        /// <summary>
        /// The GameState, as your class.
        /// </summary>
        public new TGameState GameState => (TGameState)base.GameState;

        protected override GameState CreateGameState()
        {
            return new TGameState();
        }

        protected override PlayerState CreatePlayerState()
        {
            return new TPlayerState();
        }

        /// <summary>
        /// The PlayerState of a player, as your class.
        /// </summary>
        protected static TPlayerState PlayerStateOf(PlayerController player)
        {
            return player != null ? player.PlayerState as TPlayerState : null;
        }
    }
}
