using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // A guard's round: its points are the children of this object, in order. As the level begins a guard spawns at the
    // first one (World.SpawnPawn gives it its AI controller) and walks them in a loop.
    public sealed class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private Pawn guard;

        [SerializeField] private string guardName = "Guard";

        [SerializeField, Min(0f), Tooltip("Seconds the guard looks around at each point it reaches.")]
        private float waitSeconds = 1f;

        public int Count => transform.childCount;

        public float WaitSeconds => waitSeconds;

        public Transform this[int index] => transform.GetChild(index);

        internal void SpawnGuard()
        {
            if (guard == null || Count == 0)
            {
                return;
            }

            var start = this[0];

            var pawn = World.SpawnPawn(guard, start.position, start.rotation);

            pawn.name = guardName;

            if (pawn.Controller is GuardController controller)
            {
                controller.Patrol(this);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color32(0x7C, 0x5C, 0xFF, 0xFF);

            for (var i = 0; i < Count; i++)
            {
                var point = this[i].position;

                Gizmos.DrawWireSphere(point, 0.3f);

                Gizmos.DrawLine(point, this[(i + 1) % Count].position);
            }
        }
    }
}
