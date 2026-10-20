using UnityEngine;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Tests
{
    // Hands its child a ViewModel of its own choosing: the framework must not overwrite it.
    public sealed class ParentWidget : Widget<TestGameState>
    {
        public static readonly TestPlayerState ChildViewModel = new();

        [SerializeField] private ScoreWidget child;

        protected override void OnBind(TestGameState gameState)
        {
            Bind.Child(child, ChildViewModel);
        }
    }
}
