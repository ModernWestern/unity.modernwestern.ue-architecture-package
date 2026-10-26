using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Tests
{
    /// <summary>
    /// A GameState with something a widget can show.
    /// </summary>
    public sealed class BindableState : GameState
    {
        public Observable<int> Round { get; } = new(1);
    }

    /// <summary>
    /// The one widget class of <see cref="BindableState"/>.
    /// </summary>
    public sealed class BindableStateWidget : Widget<BindableState>
    {
    }
}
