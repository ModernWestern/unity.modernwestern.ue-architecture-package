#if UEA_NAVIGATION
using System;
using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The level's navigation, like Unreal's UNavigationSystem: ticks every move in progress (only those) after every
    /// LateUpdate, once the agents moved, without allocating. You never need to call it; read it for debugging.
    /// </summary>
    [Tick(TickGroup.LateUpdate, Order = -100)]
    public sealed class NavigationSystem : WorldSubsystem, ITickable
    {
        /// <summary>
        /// Moves in progress (or about to report how they ended), in no particular order.
        /// </summary>
        public static ListView<PathFollowing> Moving => new(PathFollowing.Active);

        public void Tick(float deltaTime)
        {
            var frame = Time.frameCount;

            var active = PathFollowing.Active;

            // Backwards: a move that ends leaves the list by swapping with the last one, which was already ticked.
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count)
                {
                    continue;
                }

                var pathFollowing = active[i];

                try
                {
                    pathFollowing.Tick_Internal(deltaTime, frame);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, pathFollowing);
                }
            }
        }
    }
}
#endif
