using UnityEngine;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Binds the Widget on this GameObject to the gameplay framework by itself, like the HUD does with its Widgets: put it
    /// next to a Widget outside the HUD (a menu, a world-space nameplate, a persistent overlay). The Widget gets the
    /// framework object that matches its ViewModel type (the player's PlayerState or Pawn, the GameState, a subsystem...)
    /// and is bound again when the level changes or the player gets a new Pawn.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Widget))]
    [AddComponentMenu("UE Architecture/Framework Binder")]
    public sealed class FrameworkBinder : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Whose PlayerState, Pawn or PlayerController to use: 0 = the first player.")]
        private int playerIndex;

        private Widget _widget;

        private PlayerController _player;

        private void Awake()
        {
            _widget = GetComponent<Widget>();
        }

        private void OnEnable()
        {
            World.BeganPlay += Rebind_Internal;

            World.EndingPlay += Unbind_Internal;

            Rebind_Internal();
        }

        private void OnDisable()
        {
            World.BeganPlay -= Rebind_Internal;

            World.EndingPlay -= Unbind_Internal;

            Follow_Internal(null);
        }

        private void Rebind_Internal()
        {
            if (!World.HasBegunPlay)
            {
                return;
            }

            Follow_Internal(World.GetPlayer(playerIndex));

            FrameworkViewModels.Bind(_widget, _player);
        }

        // Level objects are about to go away: let go of them so the Widget never shows a dead level.
        private void Unbind_Internal()
        {
            Follow_Internal(null);

            if (_widget != null && _widget.BoundViewModel != null)
            {
                FrameworkViewModels.SetViewModel(_widget, null);
            }
        }

        private void Follow_Internal(PlayerController player)
        {
            if (_player == player)
            {
                return;
            }

            if (_player != null)
            {
                _player.PawnChanged -= OnPawnChanged_Internal;
            }

            _player = player;

            if (_player != null && FrameworkViewModels.DependsOnPawn(_widget))
            {
                _player.PawnChanged += OnPawnChanged_Internal;
            }
        }

        private void OnPawnChanged_Internal(PlayerController player)
        {
            FrameworkViewModels.Bind(_widget, player);
        }
    }
}
