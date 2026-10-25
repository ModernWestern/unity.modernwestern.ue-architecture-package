using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    // A GameMode whose own code takes a placed Pawn as the level begins (here with an AI): Auto Possess Player leaves it alone.
    public sealed class TestCodePossessGameMode : GameMode
    {
        [SerializeField] private TestPawn taken;

        protected override void InitGame()
        {
            var ai = new GameObject("Code AI").AddComponent<AIController>();

            ai.Possess(taken);
        }
    }
}
