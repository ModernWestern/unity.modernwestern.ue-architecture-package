using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// One entry in Unity's PlayerLoop, right after every MonoBehaviour.Update, that runs the timers and the tickable
    /// subsystems. No GameObject, no per-frame allocation.
    /// </summary>
    internal static class FrameworkLoop
    {
        private struct UEArchitectureTick
        {
        }

        public static void Install()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();

            Remove_Internal(ref root);

            for (var i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(Update))
                {
                    continue;
                }

                var update = root.subSystemList[i];

                var systems = update.subSystemList ?? Array.Empty<PlayerLoopSystem>();

                var insertAt = Array.FindIndex(systems, static system => system.type == typeof(Update.ScriptRunBehaviourUpdate)) + 1;

                if (insertAt <= 0)
                {
                    insertAt = systems.Length;
                }

                var withTick = new PlayerLoopSystem[systems.Length + 1];

                Array.Copy(systems, 0, withTick, 0, insertAt);

                withTick[insertAt] = new PlayerLoopSystem
                {
                    type = typeof(UEArchitectureTick),
                    updateDelegate = Tick_Internal
                };

                Array.Copy(systems, insertAt, withTick, insertAt + 1, systems.Length - insertAt);

                update.subSystemList = withTick;

                root.subSystemList[i] = update;

                break;
            }

            PlayerLoop.SetPlayerLoop(root);
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

            var deltaTime = Time.deltaTime;

            GameFramework.GameSubsystems.Tick(deltaTime);

            World.Subsystems.Tick(deltaTime);
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
                if (system.subSystemList[i].type == typeof(UEArchitectureTick))
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
                system.subSystemList = Array.FindAll(system.subSystemList, static each => each.type != typeof(UEArchitectureTick));
            }

            return removed;
        }
    }
}
