using UnityEngine;
using UnityEngine.UI;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // Shown by Levels.Open while the level loads. The base class fades its CanvasGroup in and out.
    public sealed class ArenaLoadingScreen : LoadingScreen
    {
        [SerializeField] private Image bar;

        protected override void OnProgress(float progress)
        {
            bar.fillAmount = progress;
        }
    }
}
