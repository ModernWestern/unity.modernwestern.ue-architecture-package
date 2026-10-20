using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace ModernWestern.UEArchitecture.Samples.Arena
{
    // The player's will: reads WASD or the arrow keys and drives the Runner it possesses.
    public sealed class ArenaPlayerController : PlayerController
    {
        private void Update()
        {
            var runner = GetPawn<Runner>();

            var gameState = World.GetGameState<ArenaGameState>();

            if (runner != null && gameState != null && !gameState.IsOver)
            {
                runner.Move(ReadMove());
            }
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static Vector2 ReadMove()
        {
            var keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return Vector2.zero;
            }

            var move = Vector2.zero;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;

            return move;
        }
#else
        private static Vector2 ReadMove() => new(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }
}
