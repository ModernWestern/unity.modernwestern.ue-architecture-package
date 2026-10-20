using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.ArenaObserverVM
{
    // The Pawn: a cube that runs where its PlayerController tells it. The GameMode spawns it at the PlayerStart.
    public sealed class Runner : Pawn
    {
        [SerializeField] private float speed = 6f;

        [SerializeField, Tooltip("The Runner cannot leave this area (centered on the world origin).")]
        private Vector2 arenaSize = new(16f, 10f);

        public void Move(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f)
            {
                return;
            }

            var direction = new Vector3(input.x, 0f, input.y).normalized;

            var position = transform.position + direction * (speed * Time.deltaTime);

            position.x = Mathf.Clamp(position.x, -arenaSize.x * 0.5f, arenaSize.x * 0.5f);

            position.z = Mathf.Clamp(position.z, -arenaSize.y * 0.5f, arenaSize.y * 0.5f);

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        }
    }
}
