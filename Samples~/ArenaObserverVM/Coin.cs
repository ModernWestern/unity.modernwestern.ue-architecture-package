using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // A coin does not know the GameMode or the HUD: it only says "I was collected".
    public sealed class Coin : MonoBehaviour
    {
        [SerializeField] private float radius = 0.9f;

        [SerializeField] private float spinDegrees = 180f;

        private void Update()
        {
            transform.Rotate(0f, spinDegrees * Time.deltaTime, 0f, Space.World);

            // World.Players can be looped with foreach without allocating.
            foreach (var player in World.Players)
            {
                var pawn = player.Pawn;

                if (pawn == null)
                {
                    continue;
                }

                var offset = pawn.transform.position - transform.position;

                offset.y = 0f;

                if (offset.sqrMagnitude > radius * radius)
                {
                    continue;
                }

                Messages.Broadcast(new CoinCollected { Player = player.PlayerState, Position = transform.position });

                Destroy(gameObject);

                return;
            }
        }
    }
}
