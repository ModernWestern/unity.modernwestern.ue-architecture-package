# Changelog

## [2.3.0] - 2026-10-24

### Added
- New Framework Class window (Assets > Create > UE Architecture > Class…, "Create Child Class…" on a framework script in the Project window, and "New class (more options)…" in every class field's picker), like Unreal's "Pick Parent Class": the common classes with what each is for (Game Mode, Player Controller, AI Controller, Pawn, HUD, Game Instance, Game and World Subsystems, Loading Screen, Save Game, Message, Player Start), a search over every framework class and every class of yours, the name checked as you type, the folder with its namespace and assembly, tick group, order and unscaled time for subsystems, new or existing GameState and PlayerState for a GameMode (in its file or their own), "Also create the prefab", "Then use it as" when opened from a field, and a "Will create" list. One compile. A folder whose asmdef lacks a reference the class needs gets "Add reference" (the asmdef keeps its GUID or name style).
- Game Mode Kit (Window > UE Architecture > New Game Mode…, Assets > Create > UE Architecture > Game Mode Kit…, "New Game Mode set…" on GameMode fields, "Create a GameMode…" in the Level overlay): type a name and choose New, Existing, Base class or None for the GameMode, GameState, PlayerState, Player Controller, Default Pawn, HUD and AI Controller; a Pawn preset (empty, a capsule, or a capsule with a NavMeshAgent and Path Following) and a HUD that is a ready Canvas. One compile later every prefab is built and wired, the GameMode is used as the project default or by one level (the settings asset is created when missing), and a Player Start per player is placed. The request waits in the pending step, so closing the window or the Editor loses nothing; running it again fills only what is missing (slots that are set keep their value). The generated classes are the usual empty templates.
- Scene view "Level" overlay: the level's GameMode and where it comes from (click to select it), its Player Starts against the players at start, whether it is in Build Settings, one accent button with the most important fix (Create a GameMode…, Use <the project's only GameMode> here, Add Player Start, Add EventSystem, Add AudioListener, or Override for this level; notes such as "Not in Build Settings" are never offered there), and the list of problems with their fixes and "Fix all safe" (one Undo step; it never adds levels to Build Settings, and changes a HUD prefab in its file instead of opening Prefab Mode). The problems follow every change, including fields edited in the Inspector or Project Settings. In Prefab Mode it lists the prefab's problems. Hide it from the Scene view's Overlays menu.
- Problems with one fix each, shown on the object that has them, in the overlay and on the command line: a level without a GameMode, without enough Player Starts, without an EventSystem for its HUD's buttons, without an AudioListener, or not in Build Settings; a HUD (a warning: it may draw in another way) or loading screen without a Canvas or a UI Toolkit UI Document; a loading screen whose Canvas Group is empty (it pops instead of fading) or that draws under the level's UI; a Player Start floating above the ground, off the NavMesh, or sharing its tag; Auto Possess AI with a plain AIController; the settings asset outside Resources.
- Pressing Play in a level without a GameMode says so once per level and session in the Console ("Animations.unity has no GameMode: players get no input"). Play is never stopped; Project Settings > UE Architecture > Editor > Play Without a GameMode turns it off.
- Assets > Create > UE Architecture > Level: a playable level named in the Project window, with World Settings, a Player Start per player on the ground, a camera aimed at it, a light, an EventSystem with the input module that fits the project, an AudioListener (unless the GameMode's Pawn or Player Controller brings one) and a ground plane; saved, added to Build Settings and opened (after asking to save the open levels). GameObject > UE Architecture > Set Up This Level adds only what a level lacks and says what was already fine; Player Starts for Every Player adds the missing ones side by side. A new Player Start lands on the ground below the Scene view's pivot (on the NavMesh when there is one) and faces the view.
- Ready-made UI prefabs, in the class picker of HUD and Loading Screen fields, each a new prefab named after its class, or after the GameMode's set for a GameMode's HUD (ArenaHUD.prefab for ArenaGameMode; an existing file is never reused, the new one gets the next free name): "New HUD prefab (Canvas ready)" (a Screen Space Overlay Canvas scaled for 1920 x 1080 with a Graphic Raycaster; never an EventSystem, which belongs to the level) and "New default loading screen" (a Canvas drawn over the level, a Canvas Group the LoadingScreen fades, a background and a Filled progress bar). They come from a new UEArchitecture.Editor.UGUI assembly that compiles only with the Unity UI package; without it the choices are hidden and the fixes say what to install.
- Inspectors: the HUD shows "has no Canvas" and the level's missing EventSystem with their fixes, and "Find children by name" for empty references; the Loading Screen shows its fade and sorting fixes and a Preview slider that shows a copy in the Game view (never saved); the Player Start shows floating, NavMesh and duplicate-tag notices and a menu of the level's start tags; the Pawn, GameMode and World Settings inspectors show their problems.
- New public actions in `UEArchitectureActions`: `CreateClass`, `CreateGameModeKit`, `CreateLevel`, `SetUpLevel`, `AddPlayerStarts`, `AddEventSystem`, `AddAudioListener`, `AddSceneToBuild`, `SnapPlayerStart`, `CreateHudPrefab`, `CreateLoadingScreenPrefab`, `AddHudCanvas`, `BuildLoadingScreen`, `UseCanvasGroup`, `SetSortingOrder`, `FindChildrenByName`, `Validate` and `FixAll`, and the command-line verbs `create-class`, `new-gamemode-kit`, `create-level`, `set-up-level`, `add-player-starts`, `create-hud-prefab`, `create-loading-screen`, `find-children`, `validate` (exit code 1 on errors; `strict=true` counts warnings) and `fix-all` (applies the fixes that only add what is missing, never Build Settings, and saves only the levels it changed). The windows copy the matching command line. Each is one Undo step and a second run changes nothing.
- Project Settings > UE Architecture > Editor: Pawn preset, UI reference resolution, and what Play does in a level without a GameMode.

### Changed
- Every action clears the cached problems as it ends, so inspectors and the overlay never show a fixed problem.
- World Settings' "This level" section lists the level's problems with their fixes instead of the single Player Start notice.

## [2.2.0] - 2026-10-24

### Added
- Class fields with a "+" picker, like Unreal's class pickers, on every framework slot (a GameMode's Player Controller, Default Pawn and HUD, World Settings' GameMode Override, the project's Game Instance, Default GameMode and Loading Screen, a Pawn's AI Controller, and your own fields of those types on prefabs): pick a prefab, pick a class without a prefab (its prefab is built at once, no compile), write a new script (the prefab is built and assigned after the compile, with a "Waiting for compile" chip, or a red "Blocked" one with Open script and Cancel), make a "Variant of" the current prefab, open the prefab or its script. Prefabs named alike (a class's prefab and every level's copy of it) are told apart by their folder, and other levels' own copies are listed apart. Dropping a script on the field builds and assigns its prefab. A slot that holds an object of the level instead of a prefab is flagged, with "Save as prefab and use" (an existing prefab is never overwritten). A waiting chip has "Finish now", and a step waiting for a level finishes when that level is opened. Right-clicking an overridden slot adds "Revert to <prefab>" to Unity's own menu. In Play mode the slots of level objects are locked, and the actions refuse to change level objects instead of throwing. "New script…" works on a prefab open in Prefab Mode.
- Selected GameMode panel in World Settings, Project Settings and (slim) the GameMode inspector: Player Controller, Default Pawn, HUD and Players at start editable in place, GameState and PlayerState with Open script. When a level uses the project default or a shared prefab, the rows are read-only ("Edit shared prefab" unlocks a shared one and says how many levels change) and "Override for this level" makes (or reuses) a variant of it in the level's own folder (`Assets/Prefabs/Modes/<Level>/<Class>.prefab` by default; a level whose name another level has gets `Modes/<Level> (<Folder>)`, so two levels never share an "own" GameMode), adds World Settings when missing and uses it. Rows a variant changes are bold; right-click reverts them. A GameMode placed in a level is edited in place; a copy of a prefab says its changes are overrides, with "Edit the prefab". Clear override, Make project default, and a line saying how many levels use the GameMode (open levels with unsaved changes are counted as they are).
- Project Settings > UE Architecture creates the settings asset by itself the first time a value is set, and its search box filters the page.
- Project window > UE Architecture: on framework scripts, "Create Prefab" (several at once) and "Create Prefab and Use as" (Player Controller, Default Pawn or HUD of the selected or default GameMode, Project Game Instance, Loading Screen); on GameMode prefabs, "Set as Project Default GameMode", "Use in Active Level", "Create Variant for Active Level" and "Create Missing Default Classes". "Create Prefab and Use as" asks before changing the project default GameMode, and its message names the GameMode it changed. "Remove Files Made by the Last Action" moves what the last action created to the trash (Undo cannot remove files). The same verbs on the GameMode component menu, and on World Settings: "Override for This Level", "Clear Override", "Select Effective GameMode".
- Scene references on a GameMode placed in a level: "Scene references: N empty of M" with Find on each empty Component or GameObject field and "Fill N from this scene", which assigns every field exactly one object fits (by type, or by its name) in one Undo step; fields that are set are never changed, and fields with several matches are highlighted. SceneReference fields get a "This level" button.
- New public actions in `UEArchitectureActions`: `AssignClass`, `AssignProjectClass`, `CreateVariantAndAssign`, `SaveAsPrefabAndAssign`, `RevertOverride`, `OverrideGameModeForLevel`, `CreateGameModeVariantForLevel`, `UseGameModeInLevel`, `ClearOverride`, `SetProjectDefaultGameMode`, `CreateMissingDefaultClasses`, `FillSceneReferences` and `RemoveCreatedFiles`, and the command-line verbs `assign-class`, `assign-project-class`, `create-class-prefab`, `create-variant`, `save-as-prefab`, `revert-override`, `override-gamemode`, `create-gamemode-variant`, `set-default-gamemode`, `use-gamemode`, `clear-override`, `create-missing-default-classes`, `fill-scene-references` and `remove-files`. The "New script…" popup and the scene references section copy the matching command line. Each is one Undo step and a second run creates nothing.
- The framework components are listed under Add Component > UE Architecture.
- Editor groundwork for create-and-wire tools:
  - `UEArchitectureActions`: public static actions that return an `ActionResult` (`CompletePending`, `ListPending`, `CancelPending`, `CreateClassPrefab`, `Assign`, `NewScript`), each one Undo step, safe to run twice and free of dialogs in batch mode.
  - Command line: `-executeMethod ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run -uea <verb> key=value ...` prints one JSON line (in the log, or on standard output with `-logFile -`), writes `Library/UEArchitecture/LastCliResult.json` and exits 0 (done), 1 (problems) or 2 (part of the work still waits). Objects are addressed as `Assets/Scenes/Boss.unity#Path/To/Object` or a prefab path. Verbs that write scripts finish after the compile: run them with `-quit`, then `-uea complete-pending`, which opens the levels it fills; without `-quit` the batch Editor waits for the compile, finishes the same way and exits by itself, or gives up after `timeout=<seconds>` (600 by default).
  - Pending steps: work that waits for a script compile (build a new class's prefab and fill the field it was made for) is kept in `Library/UEArchitecture/PendingSteps.json`, survives reloads and restarts, runs in dependency order after the compile and expires after 24 hours.
  - Project Settings > UE Architecture > Editor: where generated scripts and prefabs go and what they are called (defaults: `Assets/Scripts`; prefabs named after their class in `Assets/Prefabs/Gameplay` or `Assets/Prefabs/UI`; level overrides in `Assets/Prefabs/Modes/<Scene>`), and whether a GameMode's GameState and PlayerState get their own files.
- Script templates (Assets > Create > UE Architecture, and "New script…"): GameState, PlayerState, Player Start, SaveGame, Message, tickable game and level subsystems with `[Tick]`, and a child of one of your GameModes. The menu now reports a name that is taken (a file or a class) instead of writing "NewPawn 1.cs" with a duplicate class.

### Changed
- Gameplay tag fields and the World Settings inspector no longer search the project on every repaint: project lookups are cached, and after the first scan only the prefabs that changed are read again.
- The Project Settings page restores the label width it changes.
- GameObject > UE Architecture places World Settings and Player Start in the clicked object's scene or in the prefab open in Prefab Mode (not always the active scene), applies default presets, and puts a Player Start at the Scene view's pivot.
- The GameMode inspector no longer warns "Pawn: none" for a GameMode that possesses a Pawn placed in the level from code: it says which field points to it.

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
