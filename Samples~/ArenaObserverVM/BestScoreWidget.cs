using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // The best score. Bound by the HUD to the BestScore game subsystem.
    // No code: every binding is a View Binding (see the Inspector).
    public sealed class BestScoreWidget : Widget<BestScore>
    {
    }
}
