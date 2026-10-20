namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Base of every subsystem. Like Unreal's USubsystem: you never create one, the framework does it for you and keeps
    /// it alive as long as its owner (the game or the level). Derive from <see cref="GameInstanceSubsystem"/> or
    /// <see cref="WorldSubsystem"/>, never from this class.
    /// </summary>
    public abstract class Subsystem
    {
        internal Subsystem()
        {
        }

        /// <summary>
        /// True between <see cref="Initialize"/> and <see cref="Deinitialize"/>.
        /// </summary>
        public bool IsInitialized { get; private set; }

        internal bool IsInitializing { get; private set; }

        internal int InitializationOrder { get; set; }

        /// <summary>
        /// Return false to skip this subsystem (e.g. a debug tool in release builds). Like Unreal's ShouldCreateSubsystem.
        /// </summary>
        protected virtual bool ShouldCreate()
        {
            return true;
        }

        /// <summary>
        /// Called once, after every subsystem of the same owner was created. Calling Get on another subsystem here
        /// initializes it first, so dependencies just work.
        /// </summary>
        protected virtual void Initialize()
        {
        }

        /// <summary>
        /// Called once, in the reverse order of initialization.
        /// </summary>
        protected virtual void Deinitialize()
        {
        }

        internal bool ShouldCreate_Internal()
        {
            return ShouldCreate();
        }

        internal void Initialize_Internal()
        {
            if (IsInitialized || IsInitializing)
            {
                return;
            }

            IsInitializing = true;

            try
            {
                Initialize();
            }
            finally
            {
                IsInitializing = false;

                IsInitialized = true;
            }
        }

        internal void Deinitialize_Internal()
        {
            if (!IsInitialized)
            {
                return;
            }

            IsInitialized = false;

            Deinitialize();
        }
    }

    /// <summary>
    /// A subsystem that lives as long as the game: created before the first scene loads and destroyed when the game quits.
    /// Get it from anywhere with <c>GameInstance.Get&lt;T&gt;()</c>. Good for saves, settings, audio, analytics, online services.
    /// </summary>
    public abstract class GameInstanceSubsystem : Subsystem
    {
        public GameInstance GameInstance => GameInstance.Current;

        /// <summary>
        /// Called every time a level begins play (players spawned, level subsystems ready).
        /// </summary>
        protected virtual void OnLevelBeganPlay()
        {
        }

        /// <summary>
        /// Called when the current level is about to end, with everything still alive.
        /// </summary>
        protected virtual void OnLevelEndingPlay()
        {
        }

        internal void LevelBeganPlay_Internal()
        {
            OnLevelBeganPlay();
        }

        internal void LevelEndingPlay_Internal()
        {
            OnLevelEndingPlay();
        }
    }

    /// <summary>
    /// A subsystem that lives as long as the current level: created when it starts and destroyed when it ends.
    /// Get it with <c>World.Get&lt;T&gt;()</c>. Good for things that belong to one level: enemy registries, spawners, objectives.
    /// </summary>
    public abstract class WorldSubsystem : Subsystem
    {
        /// <summary>
        /// Called after the level's GameMode started play (players are spawned). Like Unreal's OnWorldBeginPlay.
        /// </summary>
        protected virtual void OnWorldBeginPlay()
        {
        }

        internal void WorldBeginPlay_Internal()
        {
            OnWorldBeginPlay();
        }
    }

    /// <summary>
    /// Add it to a subsystem to get <see cref="Tick"/> every frame, without a MonoBehaviour and without allocating.
    /// </summary>
    public interface ITickable
    {
        /// <param name="deltaTime">Scaled frame time (0 while the game is paused with Time.timeScale = 0).</param>
        void Tick(float deltaTime);
    }
}
