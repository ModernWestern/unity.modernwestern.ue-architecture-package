using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Four entries in Unity's PlayerLoop, one per tick group: before and after every MonoBehaviour.Update (the timers run
    /// in the second one), after every LateUpdate and after every FixedUpdate. No GameObject, no per-frame allocation.
    /// </summary>
    internal static class FrameworkLoop
    {
        private struct UEArchitectureEarlyTick
        {
        }

        private struct UEArchitectureTick
        {
        }

        private struct UEArchitectureLateTick
        {
        }

        private struct UEArchitectureFixedTick
        {
        }

        private static readonly Type[] Markers = { typeof(UEArchitectureEarlyTick), typeof(UEArchitectureTick), typeof(UEArchitectureLateTick), typeof(UEArchitectureFixedTick) };

        public static void Install()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();

            Remove_Internal(ref root);

            Insert_Internal(ref root, typeof(Update), typeof(Update.ScriptRunBehaviourUpdate), false, typeof(UEArchitectureEarlyTick), EarlyTick_Internal);

            Insert_Internal(ref root, typeof(Update), typeof(Update.ScriptRunBehaviourUpdate), true, typeof(UEArchitectureTick), Tick_Internal);

            Insert_Internal(ref root, typeof(PreLateUpdate), typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate), true, typeof(UEArchitectureLateTick), LateTick_Internal);

            Insert_Internal(ref root, typeof(FixedUpdate), typeof(FixedUpdate.ScriptRunBehaviourFixedUpdate), true, typeof(UEArchitectureFixedTick), FixedTick_Internal);

            PlayerLoop.SetPlayerLoop(root);
        }

        private static void Insert_Internal(ref PlayerLoopSystem root, Type phase, Type anchor, bool after, Type marker, PlayerLoopSystem.UpdateFunction update)
        {
            for (var i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != phase)
                {
                    continue;
                }

                var phaseSystem = root.subSystemList[i];

                var systems = phaseSystem.subSystemList ?? Array.Empty<PlayerLoopSystem>();

                var anchorIndex = Array.FindIndex(systems, system => system.type == anchor);

                var insertAt = anchorIndex < 0 ? systems.Length : after ? anchorIndex + 1 : anchorIndex;

                var withTick = new PlayerLoopSystem[systems.Length + 1];

                Array.Copy(systems, 0, withTick, 0, insertAt);

                withTick[insertAt] = new PlayerLoopSystem
                {
                    type = marker,
                    updateDelegate = update
                };

                Array.Copy(systems, insertAt, withTick, insertAt + 1, systems.Length - insertAt);

                phaseSystem.subSystemList = withTick;

                root.subSystemList[i] = phaseSystem;

                return;
            }
        }

        public static void Uninstall()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();

            if (Remove_Internal(ref root))
            {
                PlayerLoop.SetPlayerLoop(root);
            }
        }

        /// <summary>
        /// One frame of the framework. Public to the tests so they can measure it.
        /// </summary>
        internal static void Tick_Internal()
        {
            Timers.Tick_Internal(Time.timeAsDouble, Time.unscaledTimeAsDouble, Time.frameCount);

            TickRegistry.Run(TickGroup.Update, Time.deltaTime, Time.unscaledDeltaTime);
        }

        private static void EarlyTick_Internal()
        {
            TickRegistry.Run(TickGroup.EarlyUpdate, Time.deltaTime, Time.unscaledDeltaTime);
        }

        private static void LateTick_Internal()
        {
            TickRegistry.Run(TickGroup.LateUpdate, Time.deltaTime, Time.unscaledDeltaTime);
        }

        private static void FixedTick_Internal()
        {
            TickRegistry.Run(TickGroup.FixedUpdate, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        }

        private static bool Remove_Internal(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
            {
                return false;
            }

            var removed = false;

            var count = system.subSystemList.Length;

            for (var i = 0; i < system.subSystemList.Length; i++)
            {
                if (Array.IndexOf(Markers, system.subSystemList[i].type) >= 0)
                {
                    count--;

                    removed = true;
                }
                else
                {
                    removed |= Remove_Internal(ref system.subSystemList[i]);
                }
            }

            if (count != system.subSystemList.Length)
            {
                system.subSystemList = Array.FindAll(system.subSystemList, static each => Array.IndexOf(Markers, each.type) < 0);
            }

            return removed;
        }
    }
}
