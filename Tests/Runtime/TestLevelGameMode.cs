using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    // A GameMode placed in its level that points at objects of that level, like Sitizens' GameplayGameMode: it possesses a
    // placed Pawn from code instead of spawning a Default Pawn.
    public sealed class TestLevelGameMode : GameMode
    {
        [SerializeField] private TestPawn playerCharacter;

        [SerializeField] private PlayerStart spawn;

        [SerializeField] private GameObject limitCollider;

        [SerializeField] private Transform[] waypoints = new Transform[2];

        public TestPawn PlayerCharacter => playerCharacter;

        public PlayerStart Spawn => spawn;

        public GameObject LimitCollider => limitCollider;

        public Transform[] Waypoints => waypoints;
    }
}
