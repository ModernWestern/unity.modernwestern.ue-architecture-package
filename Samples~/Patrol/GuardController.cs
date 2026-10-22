using UnityEngine;
using UnityEngine.AI;

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // A guard's brain. It walks its route with MoveTo and decides what to do from how each move ended: it looks around
    // at a point it reached, and skips one it cannot reach or is blocked from. When it sees the player it chases them
    // with MoveToActor, which replaces the patrol move; catching them ends the chase.
    public sealed class GuardController : AIController
    {
        [SerializeField, Min(0f)] private float sightRange = 3.5f;

        [SerializeField, Min(0f), Tooltip("Seconds the player can stay out of sight before the guard gives up.")]
        private float giveUpAfter = 2f;

        [SerializeField, Min(0f), Tooltip("Seconds the guard ignores the player after catching them.")]
        private float calmAfterCatch = 4f;

        private PatrolRoute _route;

        private int _next;

        private bool _chasing;

        private float _unseen;

        private float _calmUntil;

        private TimerHandle _resume;

        private string _goal;

        public string Activity { get; private set; } = "Waiting";

        public void Patrol(PatrolRoute route)
        {
            _route = route;

            _next = route.Count > 1 ? 1 : 0;

            Timers.Set(this, 0.2f, static guard => guard.Look(), loop: true);

            MoveToNext();
        }

        private void MoveToNext()
        {
            if (_route == null || _route.Count == 0)
            {
                return;
            }

            var point = _route[_next];

            // A move that is replaced reports its end inside MoveTo, so the goal changes only after it returns.
            MoveTo(point.position);

            _goal = point.name;

            Activity = "Walking to " + point.name;
        }

        private void Chase(Pawn player)
        {
            _chasing = true;

            _unseen = 0f;

            Timers.Clear(ref _resume);

            MoveToActor(player);

            _goal = "you";

            Activity = "Chasing you";
        }

        protected override void OnMoveCompleted(MoveResult result)
        {
            Messages.Broadcast(new MoveEnded { Who = Pawn != null ? Pawn.name : name, Goal = _goal, Result = result });

            // Something else took over: a chase replaced the patrol move, or the level is ending. Not ours to continue.
            if (result.Outcome == MoveOutcome.Aborted)
            {
                return;
            }

            if (_chasing)
            {
                _chasing = false;

                if (result.IsSuccess)
                {
                    // Sent back to the start, maybe still in sight: give them a head start.
                    _calmUntil = World.TimeSeconds + calmAfterCatch;

                    Messages.Broadcast(new PlayerCaught { Guard = this });
                }

                Activity = result.IsSuccess ? "Caught you" : "Lost you";

                _resume = Timers.Set(this, 1.5f, static guard => guard.MoveToNext());

                return;
            }

            _next = (_next + 1) % _route.Count;

            // Look around at a point it reached; leave one it could not reach (Invalid) or was kept from (Blocked).
            Activity = result.IsSuccess ? "Looking around" : "Skipping " + _goal;

            _resume = Timers.Set(this, result.IsSuccess ? _route.WaitSeconds : 0.6f, static guard => guard.MoveToNext());
        }

        private void Look()
        {
            var pawn = Pawn;

            var player = World.GetPawn<Pawn>();

            if (pawn == null || player == null || _route == null)
            {
                return;
            }

            var from = pawn.transform.position;

            var to = player.transform.position;

            // In range and nothing in between on the NavMesh (walls cut it).
            var sees = (to - from).sqrMagnitude <= sightRange * sightRange && !NavMesh.Raycast(from, to, out _, NavMesh.AllAreas);

            if (!_chasing)
            {
                if (sees && World.TimeSeconds >= _calmUntil)
                {
                    Chase(player);
                }

                return;
            }

            _unseen = sees ? 0f : _unseen + 0.2f;

            if (_unseen >= giveUpAfter)
            {
                _chasing = false;

                Activity = "Lost you";

                MoveToNext();
            }
        }
    }
}
