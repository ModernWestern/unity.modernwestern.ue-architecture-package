using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // The player clicks where to go, and their Pawn walks there with the same MoveTo the guards use. A click on a crate
    // or a wall walks next to it (the closest walkable point). The top of the platform cannot be reached: the move allows
    // partial paths, so it ends as Partial, as close as it gets.
    public sealed class PatrolPlayerController : PlayerController
    {
        public string Activity { get; private set; } = "Click to walk";

        private void Update()
        {
            var camera = Camera.main;

            if (Pawn == null || camera == null || !ReadClick(out var screenPoint))
            {
                return;
            }

            var ray = camera.ScreenPointToRay(screenPoint);

            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance))
            {
                return;
            }

            MoveTo(ray.GetPoint(distance), allowPartial: true);

            Activity = "Walking";
        }

        protected override void OnMoveCompleted(MoveResult result)
        {
            Messages.Broadcast(new MoveEnded { Who = "You", Goal = "the click", Result = result });

            switch (result.Outcome)
            {
                case MoveOutcome.Success:
                    Activity = "Here";
                    break;

                case MoveOutcome.Partial:
                    Activity = "As close as it gets";
                    break;

                case MoveOutcome.Aborted:
                    // A new click replaced it: "Walking" stays.
                    break;

                default:
                    Activity = "Cannot go there";
                    break;
            }
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static bool ReadClick(out Vector2 screenPoint)
        {
            var mouse = Mouse.current;

            screenPoint = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }
#else
        private static bool ReadClick(out Vector2 screenPoint)
        {
            screenPoint = Input.mousePosition;

            return Input.GetMouseButtonDown(0);
        }
#endif
    }
}
