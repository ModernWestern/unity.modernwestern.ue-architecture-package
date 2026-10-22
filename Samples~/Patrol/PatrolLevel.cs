using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // Marks a level as a Patrol level and says what is walkable: the boxes under Geometry. Its NavMesh is built from
    // them as the level begins (see LevelNavMesh), so there is nothing to bake. Its PatrolRoutes are its children.
    public sealed class PatrolLevel : MonoBehaviour
    {
        [SerializeField, Tooltip("The boxes the NavMesh is built from: floor, walls, crates, platforms.")]
        private Transform geometry;

        internal static PatrolLevel Current { get; private set; }

        public Transform Geometry => geometry;

        // Awake runs while the scene loads, before the level begins and creates its subsystems.
        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }
    }
}
