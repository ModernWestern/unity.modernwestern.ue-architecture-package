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
    [AddComponentMenu("UE Architecture/Game Mode")]
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

        private readonly List<AIController> _bots = new();

        // Pawns placed in the level with Auto Possess Player, found once as the level begins.
        private readonly List<Pawn> _placedPawns = new(4);

        private static readonly List<GameObject> RootsBuffer = new();

        private static readonly List<Pawn> PawnsBuffer = new();

        private int _botCount;

        private int _nextPlayerId;

        private bool _hasStartedPlay;

        private bool _warnedNoStart;

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
        /// Puts a player back in the game: if it has no Pawn (it just joined, or its Pawn died), it possesses the Pawn placed
        /// in the level for it (Auto Possess Player) the first time, when there is a free one, else gets its default Pawn
        /// spawned at a PlayerStart; if it has one, that Pawn is moved there. Like Unreal's RestartPlayer.
        /// </summary>
        public virtual void RestartPlayer(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            // A Pawn placed for this player wins over the Default Pawn, once per level, as in Unreal. One your code already
            // possessed is left alone.
            if (player.Pawn == null && PossessPlacedPawn_Internal(player))
            {
                return;
            }

            if (defaultPawn == null && player.Pawn == null)
            {
                return;
            }

            var start = FindPlayerStart(player);

            if (start == null && !_warnedNoStart)
            {
                _warnedNoStart = true;

                Debug.LogWarning($"[UEArchitecture] {name}: the level has no PlayerStart, so players spawn where the GameMode is. " +
                                 "Add one with GameObject > UE Architecture > Player Start.", this);
            }

            if (player.Pawn != null)
            {
                if (start != null)
                {
#if UEA_NAVIGATION
                    // A NavMeshAgent keeps its own position: stop its move and warp it, or it would walk on from where it was.
                    player.StopMovement();

                    if (player.Pawn.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent) && agent.isActiveAndEnabled && agent.isOnNavMesh)
                    {
                        agent.Warp(start.transform.position);
                    }
#endif
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
        /// Picks where a player spawns. By default: the PlayerStarts of the level in turn (player 1 the first, player 2 the
        /// second...), or of any loaded scene if the level has none. Return null to spawn at the GameMode's position.
        /// </summary>
        protected virtual PlayerStart FindPlayerStart(PlayerController player)
        {
            var levelStarts = 0;

            foreach (var start in PlayerStart.All)
            {
                if (start.gameObject.scene == World.Scene)
                {
                    levelStarts++;
                }
            }

            var pick = player.PlayerIndex % Mathf.Max(1, levelStarts > 0 ? levelStarts : PlayerStart.All.Count);

            foreach (var start in PlayerStart.All)
            {
                if (levelStarts > 0 && start.gameObject.scene != World.Scene)
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    return start;
                }
            }

            return null;
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

            gameMode.InitGame_Internal(scene);

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

        internal void AddBot_Internal(AIController bot)
        {
            var playerState = CreatePlayerState() ?? new PlayerState();

            playerState.Init_Internal(bot, ++_nextPlayerId, "Bot " + ++_botCount);

            bot.SetPlayerState_Internal(playerState);

            _bots.Add(bot);

            GameState.AddPlayer_Internal(playerState);

            if (_hasStartedPlay)
            {
                Guard_Internal(playerState.BeginPlay_Internal);
            }
        }

        internal void RemoveBot_Internal(AIController bot)
        {
            if (!_bots.Remove(bot))
            {
                return;
            }

            Guard_Internal(bot.PlayerState.EndPlay_Internal);

            GameState.RemovePlayer_Internal(bot.PlayerState);
        }

        internal void EndPlay_Internal()
        {
            Guard_Internal(OnEndPlay);

            for (var i = _ownedPlayers.Count - 1; i >= 0; i--)
            {
                EndPlayFor_Internal(_ownedPlayers[i]);
            }

            for (var i = _bots.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_bots[i], null) && _bots[i].PlayerState != null)
                {
                    Guard_Internal(_bots[i].PlayerState.EndPlay_Internal);
                }
            }

            _bots.Clear();

            if (GameState != null)
            {
                Guard_Internal(GameState.EndPlay_Internal);
            }

            _hasStartedPlay = false;
        }

        private void InitGame_Internal(Scene scene)
        {
            _ownedPlayers.Clear();

            _bots.Clear();

            _botCount = 0;

            _nextPlayerId = 0;

            _hasStartedPlay = false;

            _warnedNoStart = false;

            CollectPlacedPawns_Internal(scene);

            GameState = CreateGameState() ?? new GameState();

            GameState.Init_Internal(this);

            Guard_Internal(InitGame);
        }

        // Once per level, with reused lists: nothing is searched again when a player joins or respawns.
        private void CollectPlacedPawns_Internal(Scene scene)
        {
            _placedPawns.Clear();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            scene.GetRootGameObjects(RootsBuffer);

            foreach (var root in RootsBuffer)
            {
                root.GetComponentsInChildren(false, PawnsBuffer);

                foreach (var pawn in PawnsBuffer)
                {
                    if (pawn.AutoPossessPlayer >= 0)
                    {
                        _placedPawns.Add(pawn);
                    }
                }
            }

            RootsBuffer.Clear();

            PawnsBuffer.Clear();
        }

        // One-shot, as in Unreal: the first time a player needs a Pawn, it takes the first free Pawn placed for it, and every
        // Pawn placed for that player is then crossed off the list (slots set to null, no allocation). A later RestartPlayer
        // (its Pawn died, a duplicate placed for the same player, a vehicle it left) spawns the Default Pawn instead.
        private bool PossessPlacedPawn_Internal(PlayerController player)
        {
            Pawn picked = null;

            for (var i = 0; i < _placedPawns.Count; i++)
            {
                var pawn = _placedPawns[i];

                // Destroyed (or already crossed off), or meant for another player.
                if (pawn == null || pawn.AutoPossessPlayer != player.PlayerIndex)
                {
                    continue;
                }

                _placedPawns[i] = null;

                // One your code already possessed is left alone.
                if (picked == null && pawn.Controller == null)
                {
                    picked = pawn;
                }
            }

            if (picked == null)
            {
                return false;
            }

            player.Possess(picked);

            return true;
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
