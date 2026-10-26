using UnityEngine;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Tests
{
    /// <summary>
    /// Holds a child widget in a [BindWidget] field: the child gets its ViewModel from here, not from the framework.
    /// </summary>
    public sealed class HoldingWidget : Widget<TestGameState>
    {
        [SerializeField, BindWidget] private ScoreWidget child;
    }
}
