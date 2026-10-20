# Changelog

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
