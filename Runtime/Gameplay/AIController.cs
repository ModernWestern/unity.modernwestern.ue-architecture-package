using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// A controller driven by code instead of a player, like Unreal's AAIController. Derive from it and write the behaviour
    /// (e.g. in Update, using <see cref="Controller.Pawn"/>). A Pawn creates one by itself when its Auto Possess AI says so,
    /// or call <see cref="Pawn.SpawnDefaultController"/>. Every AI of the level is in <see cref="World.AIControllers"/>.
    /// </summary>
    public class AIController : Controller
    {
        [SerializeField, Tooltip("Give this AI a PlayerState, so it is in GameState.Players like a player (a bot on the scoreboard).")]
        private bool wantsPlayerState;

        [SerializeField, Tooltip("Destroy this controller when its Pawn is destroyed, like Unreal does with AI controllers.")]
        private bool destroyWithPawn = true;

        private bool _registered;

        public bool WantsPlayerState => wantsPlayerState;

        internal override void Possessed_Internal()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;

            World.AddAI_Internal(this);
        }

        internal override void Destroyed_Internal()
        {
            if (!_registered)
            {
                return;
            }

            _registered = false;

            World.RemoveAI_Internal(this);
        }

        internal override bool PawnDestroyed_Internal(Pawn pawn)
        {
            if (!base.PawnDestroyed_Internal(pawn))
            {
                return false;
            }

            if (destroyWithPawn && this != null)
            {
                Destroy(gameObject);
            }

            return true;
        }
    }
}
