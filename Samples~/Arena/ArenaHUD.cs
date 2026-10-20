using TMPro;
using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Arena
{
    // The player's HUD without ObserverVM: it listens to messages and refreshes on a timer, without Update.
    // (The "Arena + ObserverVM" sample does the same with no HUD code at all.)
    public sealed class ArenaHUD : HUD
    {
        [SerializeField] private TMP_Text coins;

        [SerializeField] private TMP_Text time;

        [SerializeField] private TMP_Text best;

        [SerializeField] private GameObject banner;

        [SerializeField] private TMP_Text bannerText;

        protected override void OnInit()
        {
            banner.SetActive(false);

            // Listeners and timers of a destroyed HUD stop by themselves.
            Messages<CoinCollected>.Listen(this, static (hud, _) => hud.Refresh());

            Messages<RoundEnded>.Listen(this, static (hud, message) => hud.ShowEnd(message));

            Timers.Set(this, 0.25f, static hud => hud.Refresh(), loop: true);

            Refresh();
        }

        private void Refresh()
        {
            var player = GetPlayerState<ArenaPlayerState>();

            var gameState = World.GetGameState<ArenaGameState>();

            if (player == null || gameState == null)
            {
                return;
            }

            coins.SetText("{0} / {1}", player.Coins, gameState.CoinsToWin);

            time.SetText("{0}", Mathf.Max(0, gameState.SecondsLeft));

            best.SetText("Best {0}", GameInstance.Get<BestScore>().Best);
        }

        private void ShowEnd(RoundEnded message)
        {
            Refresh();

            banner.SetActive(true);

            var title = message.Won ? "You win!" : "Time is up!";

            bannerText.text = message.NewBest ? title + "\nNew best score" : title;
        }
    }
}
