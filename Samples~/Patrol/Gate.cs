using UnityEngine;
using UnityEngine.AI;

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // A gate that opens and closes on a timer. Its obstacle does not carve the NavMesh, so paths still go through it: a
    // guard that finds it closed pushes against it until its move ends as Blocked, and moves on to its next point.
    [RequireComponent(typeof(NavMeshObstacle))]
    public sealed class Gate : MonoBehaviour
    {
        [SerializeField] private Transform door;

        [SerializeField, Min(0.5f)] private float openSeconds = 4f;

        [SerializeField, Min(0.5f)] private float closedSeconds = 14f;

        private NavMeshObstacle _obstacle;

        private Vector3 _closedPosition;

        public bool IsOpen { get; private set; }

        private void Start()
        {
            _obstacle = GetComponent<NavMeshObstacle>();

            _obstacle.carving = false;

            _closedPosition = door.localPosition;

            Close();
        }

        private void Open()
        {
            IsOpen = true;

            _obstacle.enabled = false;

            door.localPosition = _closedPosition - new Vector3(0f, door.localScale.y * 0.9f, 0f);

            Timers.Set(this, openSeconds, static gate => gate.Close());
        }

        private void Close()
        {
            IsOpen = false;

            _obstacle.enabled = true;

            door.localPosition = _closedPosition;

            Timers.Set(this, closedSeconds, static gate => gate.Open());
        }
    }
}
