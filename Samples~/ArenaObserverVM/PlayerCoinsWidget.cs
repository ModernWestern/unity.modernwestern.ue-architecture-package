using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // The coins of the HUD's player. Bound by the HUD to that player's ArenaPlayerState.
    // No code: every binding is a View Binding (see the Inspector).
    public sealed class PlayerCoinsWidget : Widget<ArenaPlayerState>
    {
    }
}
