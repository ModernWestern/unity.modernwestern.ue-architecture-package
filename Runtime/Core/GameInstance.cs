using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The game itself, like Unreal's UGameInstance: created before the first scene loads (whatever scene you press Play in)
    /// and alive until the game quits. Keep here what must survive level changes, and get the game's subsystems with
    /// <see cref="Get{T}"/>.
    /// <para>To use your own class, derive from it, put it on a prefab and pick that prefab in Project Settings > UE Architecture.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class GameInstance : MonoBehaviour
    {
        /// <summary>
        /// The running GameInstance. Cast it to your class: <c>(MyGameInstance)GameInstance.Current</c>.
        /// </summary>
        public static GameInstance Current => GameFramework.Instance;

        /// <summary>
        /// True from the moment the framework starts until the game quits.
        /// </summary>
        public static bool IsRunning => GameFramework.IsRunning;

        /// <summary>
        /// Every subsystem of the game, in initialization order.
        /// </summary>
        public static ListView<GameInstanceSubsystem> Subsystems => GameFramework.GameSubsystems.All;

        /// <summary>
        /// A subsystem of the game, e.g. <c>GameInstance.Get&lt;SaveSubsystem&gt;()</c>. An array read: call it as often as you like.
        /// Null if the subsystem chose not to be created (ShouldCreate) or the framework is not running.
        /// </summary>
        public static T Get<T>() where T : GameInstanceSubsystem
        {
            return GameFramework.GameSubsystems.Get<T>();
        }

        /// <summary>
        /// Starts the framework now. Only needed when "Start automatically" is off in Project Settings > UE Architecture.
        /// </summary>
        public static void Launch()
        {
            GameFramework.Launch(true);
        }

        /// <summary>
        /// Called once the game's subsystems are initialized, before the first level begins. Like Unreal's UGameInstance::Init.
        /// </summary>
        protected virtual void Init()
        {
        }

        /// <summary>
        /// Called when the game quits (or Play mode stops), after the level ended and before the subsystems are deinitialized.
        /// </summary>
        protected virtual void Shutdown()
        {
        }

        internal void Init_Internal()
        {
            Init();
        }

        internal void Shutdown_Internal()
        {
            Shutdown();
        }
    }
}
