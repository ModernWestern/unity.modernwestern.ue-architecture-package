using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// When a Pawn gets an AI controller by itself. Like Unreal's Auto Possess AI.
    /// </summary>
    public enum AutoPossessAI
    {
        /// <summary>
        /// Never: a player or your code possesses it.
        /// </summary>
        Disabled,

        /// <summary>
        /// When it is placed in a level, as the level begins.
        /// </summary>
        PlacedInWorld,

        /// <summary>
        /// When it is spawned with <see cref="World.SpawnPawn{T}"/>.
        /// </summary>
        Spawned,

        PlacedInWorldOrSpawned
    }

    /// <summary>
    /// Something a player or an AI can control, like Unreal's APawn: a character, a car, an enemy. The GameMode spawns each
    /// player's default Pawn at a PlayerStart; AI Pawns get their <see cref="AIController"/> by themselves (Auto Possess AI).
    /// Data that must survive death belongs in the PlayerState, not here.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UE Architecture/Pawn")]
    public partial class Pawn : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField, Min(-1), Tooltip("Placed in a level: the player (0 = Player 1) that possesses this Pawn when it first joins, instead of getting the " +
                                          "GameMode's Default Pawn (once per level; Auto Possess AI then waits for the player). -1 = no player.")]
        private int autoPossessPlayer = -1;

        [Header("AI")]
        [SerializeField, Tooltip("When this Pawn creates its AI controller by itself.")]
        private AutoPossessAI autoPossessAI = AutoPossessAI.Disabled;

        [SerializeField, Tooltip("Prefab with the AIController for this Pawn. Empty = a plain AIController.")]
        private AIController aiController;

        private Controller _controller;

        /// <summary>
        /// The controller of this Pawn (a PlayerController or an AIController), or null.
        /// </summary>
        public Controller Controller => _controller != null ? _controller : null;

        /// <summary>
        /// The PlayerState of whoever controls this Pawn, or null (AIs have one only if they ask for it).
        /// </summary>
        public PlayerState PlayerState => Controller != null ? Controller.PlayerState : null;

        public bool IsPlayerControlled => Controller is PlayerController;

        public bool IsAIControlled => Controller is AIController;

        public AutoPossessAI AutoPossessAI
        {
            get => autoPossessAI;
            set => autoPossessAI = value;
        }

        /// <summary>
        /// For a Pawn placed in a level: the player (0 = Player 1) that possesses it when it joins, instead of getting the
        /// GameMode's Default Pawn, like Unreal's Auto Possess Player. -1 (the default) = no player. Read once, as the level
        /// begins, and used once: when that player first needs a Pawn (later respawns get the Default Pawn). A placed Pawn
        /// with a player set never gets its AI controller as the level begins: it waits for its player.
        /// </summary>
        public int AutoPossessPlayer
        {
            get => autoPossessPlayer;
            set => autoPossessPlayer = value;
        }

        public T GetController<T>() where T : Controller
        {
            return Controller as T;
        }

        public T GetPlayerState<T>() where T : PlayerState
        {
            return PlayerState as T;
        }

        /// <summary>
        /// Creates this Pawn's AI controller (its AI Controller prefab, or a plain AIController) and possesses it.
        /// Does nothing if something already controls it. Like Unreal's SpawnDefaultController.
        /// </summary>
        public AIController SpawnDefaultController()
        {
            if (Controller != null)
            {
                return null;
            }

            var controller = aiController != null ? Instantiate(aiController) : new GameObject().AddComponent<AIController>();

            controller.name = (aiController != null ? aiController.name : "AIController") + " (" + name + ")";

            var level = World.Scene;

            if (level.IsValid() && level.isLoaded && controller.gameObject.scene != level)
            {
                SceneManager.MoveGameObjectToScene(controller.gameObject, level);
            }

            controller.Possess(this);

            return controller;
        }

        /// <summary>
        /// Called when a controller takes control of this Pawn.
        /// </summary>
        protected virtual void OnPossessed(Controller controller)
        {
        }

        /// <summary>
        /// Called when the controller lets go of this Pawn.
        /// </summary>
        protected virtual void OnUnPossessed()
        {
        }

        /// <summary>
        /// If you need OnDestroy, override it and call base.OnDestroy(): it tells the controller its Pawn is gone.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.PawnDestroyed_Internal(this);
            }
        }

        internal bool AutoPossesses_Internal(bool spawned)
        {
            switch (autoPossessAI)
            {
                case AutoPossessAI.PlacedInWorld:
                    return !spawned;

                case AutoPossessAI.Spawned:
                    return spawned;

                case AutoPossessAI.PlacedInWorldOrSpawned:
                    return true;

                default:
                    return false;
            }
        }

        internal void PossessedBy_Internal(Controller controller)
        {
#if UEA_NAVIGATION
            AbortMove_Internal();
#endif
            _controller = controller;

            OnPossessed(controller);
        }

        internal void UnPossessed_Internal()
        {
#if UEA_NAVIGATION
            // Before letting go, so the controller that is leaving hears how its move ended.
            AbortMove_Internal();
#endif
            _controller = null;

            OnUnPossessed();
        }
    }
}
