using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // The round: time left, coins to win and the end banner. Bound by the HUD to the level's ArenaGameState.
    // No code: every binding is a View Binding (see the Inspector).
    public sealed class RoundWidget : Widget<ArenaGameState>
    {
    }
}
