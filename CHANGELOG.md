# Changelog

## [2.1.1] - 2026-10-24

### Fixed
- The editor compiles on Unity 6000.4 and later: the script templates use `AssetCreationEndAction` (with `EntityId`) where Unity replaced `EndNameEditAction`, which 6000.5 turned into a compile error, and the Debugger no longer calls `GetInstanceID`, also a compile error in 6000.5.

## [2.1.0] - 2026-10-20

### Added
- Navigation, like Unreal's AI MoveTo: `Controller.MoveTo`, `MoveToActor`, `StopMovement`, `PauseMove`, `ResumeMove`, `MoveStatus` and the `OnMoveCompleted` override. Every move ends with exactly one result (`MoveOutcome`: Success, Partial, Blocked, Invalid, Aborted, with a `MoveReason`), decided from the path's state instead of a distance threshold: stale and lost paths, partial paths, off-mesh links, crowds and stuck agents are handled.
- `PathFollowing` component (added to a Pawn with a NavMeshAgent the first time it moves; works on any NavMeshAgent), `MoveRequest` (callback with an owner and a static lambda, coroutine `Wait()`, `await`), `MoveResult`.
- `NavigationSystem` level subsystem: ticks the moves in progress after every LateUpdate, without allocating.
- Editor: Path Following inspector with the live move and gizmos; moves in progress in the Debugger.
- Sample: Patrol (guards patrolling with MoveTo, a chase with MoveToActor, click to move, a NavMesh built at runtime).
- Tests: 40 navigation PlayMode tests, including random interruptions of 50 agents, an agent that keeps moving without getting anywhere, and zero-GC checks.

### Changed
- `GameMode.RestartPlayer` stops the Pawn's move and warps its NavMeshAgent, which kept walking from where it was.
- `Controller` and `Pawn` are `partial` classes (navigation lives in its own files).

### Fixed
- Tests: NavMesh links are removed with `NavMesh.RemoveLink`, since `NavMeshLinkInstance.Remove()` is obsolete in Unity 6.3.

Navigation needs Unity's built-in AI module (`com.unity.modules.ai`, on by default); without it, that part is left out.

## [2.0.0] - 2026-10-20
Rewritten as Unreal's gameplay framework. The 1.x API is removed (tag `v1.2.0`); see "Migrating from 1.x" in the README.

### Added
- `GameInstance` that starts by itself before the first scene, from any scene, configured in Project Settings > UE Architecture.
- `GameInstanceSubsystem` and `WorldSubsystem` (created by themselves, dependency-ordered initialization, `ITickable`, `Get<T>` without allocating).
- `World`: level lifecycle (`BeganPlay`, `EndingPlay`), typed getters for the GameMode, GameState, players and Pawns.
- `GameMode` / `GameMode<TGameState, TPlayerState>`, `GameState`, `PlayerState`, `Controller`, `PlayerController`, `AIController`, `Pawn` (with Auto Possess AI), `HUD`, `PlayerStart`, `WorldSettings`; `World.SpawnPawn`, `World.AIControllers`, bots with a PlayerState.
- GameInstance and game subsystem hooks: `OnLevelBeganPlay`, `OnLevelEndingPlay`.
- Tick groups and order for tickable subsystems: `[Tick(TickGroup.LateUpdate, Order = -10, Unscaled = true)]`.
- `GameplayTag` and `GameplayTagContainer` (hierarchical, allocation-free queries, Inspector picker, project tag list).
- `SaveGame` and `SaveGames` (slots, versions, atomic writes, async variants).
- `Levels`: `Open` with loading screens, `Stream` / `Unstream`, `LevelLoad` usable as callback, coroutine or `await`; `SceneReference`.
- `Messages`: typed struct messages without allocation.
- `Timers`: like FTimerManager, without allocation.
- `ListView<T>`: framework lists that can be looped with `foreach` without allocating.
- Optional ObserverVM integration: HUD Widgets bound to the framework by type, `FrameworkBinder`.
- Editor: Project Settings page, GameMode and World Settings inspectors, SceneReference drawer, Debugger window, script templates, GameObject menu, link.xml for IL2CPP.
- Samples: Arena, Arena + ObserverVM.
- PlayMode and EditMode tests, including zero-GC tests.

### Removed
- `SceneRoot`, `GameEvents`, ScriptableObject `GameState` / `PlayerState`, the `PersistentLevel` flow, `IDataPersistent` / `IPersistent`, the `Persistant` and `Root` prefabs.

## [1.2.0] - 2025-05-20
Set 'Root namespace' if the user assigned one in the editor setting.

## [1.1.0] - 2025-05-07
New `GameInstance` editor `Fullpath` option added and minor fixes.

## [1.0.1] - 2025-04-11
Minor fixes.

## [1.0.0] - 2025-04-11
This is the first release of UEArchitecture package.
