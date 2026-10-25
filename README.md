# UE Architecture

Unreal Engine's gameplay framework for Unity: a **GameInstance** that starts by itself, **Subsystems** instead of singletons, **GameMode / GameState / PlayerState / PlayerController / AIController / Pawn / HUD** chosen per level, **Navigation** that always tells you how a move ended, **Levels** with loading screens, typed **Messages**, **Timers**, **Gameplay Tags** and **Save Games**. What runs every frame allocates nothing.

| Unreal | UE Architecture |
|---|---|
| `UGameInstance` | `GameInstance` (starts before the first scene, from any scene) |
| `UGameInstanceSubsystem` / `UWorldSubsystem` | `GameInstanceSubsystem` / `WorldSubsystem` (+ `ITickable`) |
| Tick groups, tick priority | `[Tick(TickGroup.LateUpdate, Order = -10)]` |
| `UWorld`, `GetTimeSeconds` | `World` (the level being played) |
| `AGameModeBase`, `AGameStateBase`, `APlayerState` | `GameMode`, `GameState`, `PlayerState` |
| `AController`, `APlayerController`, `AAIController` | `Controller`, `PlayerController`, `AIController` |
| `APawn` (Auto Possess AI), `AHUD`, `APlayerStart` | `Pawn` (`AutoPossessAI`), `HUD`, `PlayerStart` |
| `SpawnActor` | `World.SpawnPawn` |
| `AAIController::MoveTo`, `MoveToActor`, `OnMoveCompleted` | `Controller.MoveTo`, `MoveToActor`, `OnMoveCompleted` |
| `UPathFollowingComponent`, `EPathFollowingResult` | `PathFollowing`, `MoveOutcome` |
| World Settings > GameMode Override | `WorldSettings` component |
| Project Settings > Maps & Modes | Project Settings > UE Architecture |
| `OpenLevel`, level streaming | `Levels.Open`, `Levels.Stream` |
| `FTimerManager` | `Timers` |
| Lyra's `GameplayMessageSubsystem` | `Messages` |
| `FGameplayTag`, `FGameplayTagContainer` | `GameplayTag`, `GameplayTagContainer` |
| `USaveGame`, `SaveGameToSlot`, `LoadGameFromSlot` | `SaveGame`, `SaveGames.Save`, `SaveGames.Load` |
| Blueprint (class defaults) / child Blueprint | prefab (serialized fields) / prefab variant |

Works on its own. With [ObserverVM](https://github.com/ModernWestern/unity.modernwestern.observer-vm-package) installed, the Widgets of your HUD are bound to the framework by themselves.

## Installation

Package Manager → **Add package from git URL**:

```
https://github.com/ModernWestern/unity.modernwestern.ue-architecture-package.git
```

To pin a version, add its tag: `...ue-architecture-package.git#v2.0.0`. The 1.x API (`SceneRoot`, `GameEvents`, ScriptableObject states) stays available at `#v1.2.0` (see [Migrating from 1.x](#migrating-from-1x)).

Requires Unity 2021.3+. No other dependency (navigation uses Unity's built-in AI module, on by default).

## Quick start

1. **Nothing to set up to start.** Press Play in any scene: the framework starts before the scene loads and the scene becomes the current level.
2. **Move features into subsystems.** Assets > Create > UE Architecture > Game Instance Subsystem:

   ```csharp
   public sealed class SaveSubsystem : GameInstanceSubsystem
   {
       protected override void Initialize() { /* load */ }
   }

   GameInstance.Get<SaveSubsystem>().Save();   // from anywhere, no singleton code
   ```

3. **Give the level rules.** Window > UE Architecture > **New Game Mode…** (the Game Mode Kit): type "Arena" and press Create. One compile later you have `ArenaGameMode` with its `ArenaGameState` and `ArenaPlayerState`, `ArenaPlayerController`, `ArenaPawn` and `ArenaHUD`, their prefabs with every **default class** filled in, the GameMode used as the project default (or by one level), and a Player Start. By hand: Assets > Create > UE Architecture > Game Mode, put it on a prefab and fill in its default classes with the "+" of each field.
4. **Use it in a level.** The Scene view's **Level** overlay shows the level's GameMode and offers the next fix (Add Player Start, Add EventSystem...). Assets > Create > UE Architecture > **Level** makes a playable level; GameObject > UE Architecture > **Set Up This Level** completes an existing one. Or set a default GameMode for every level in **Project Settings > UE Architecture**.
5. **Press Play.** The GameMode creates the GameState, a PlayerController with its PlayerState and HUD, and spawns the Pawn at the Player Start.

## GameInstance

The game itself, alive from the first scene until the game quits. You rarely need your own: keep features in subsystems. If you do, it works like a GameInstance Blueprint in Unreal: derive from `GameInstance`, put it on a prefab (its serialized fields are the class defaults) and pick the prefab in Project Settings. A prefab variant is a child Blueprint.

```csharp
public class MyGameInstance : GameInstance
{
    [SerializeField] private GameConfig config;     // a class default, set on the prefab

    protected override void Init() { }              // subsystems are ready (in Awake they are not yet)
    protected override void OnLevelBeganPlay() { }  // every level; the first call is Unreal's OnStart
    protected override void OnLevelEndingPlay() { } // everything is still alive: a good moment to save
    protected override void Shutdown() { }          // the game quits (or Play mode stops)
}

var game = (MyGameInstance)GameInstance.Current;
```

Game subsystems get `OnLevelBeganPlay` / `OnLevelEndingPlay` too.

It is created before the first scene loads, so it exists whatever scene you press Play in: no "persistent level" scene, no bootstrap scene. "Enter Play Mode Options" without domain reload is supported.

## Subsystems

Created by themselves, destroyed by themselves, reachable from anywhere. An end to singleton boilerplate.

| | Lives | Get it with |
|---|---|---|
| `GameInstanceSubsystem` | the whole game | `GameInstance.Get<T>()` |
| `WorldSubsystem` | the current level | `World.Get<T>()` |

```csharp
public sealed class EnemyRegistry : WorldSubsystem, ITickable
{
    protected override bool ShouldCreate() => true;          // false to skip it (e.g. a debug tool in builds)
    protected override void Initialize() { }                  // calling Get<Other>() here initializes Other first
    protected override void OnWorldBeginPlay() { }            // the GameMode spawned the players
    public void Tick(float deltaTime) { }                     // every frame, no MonoBehaviour
    protected override void Deinitialize() { }                // reverse order of initialization
}
```

`Get<T>()` is an array read: call it in Update as often as you like. Subsystems exist in **every** level of the project, so one that belongs to a kind of level should check it (e.g. `World.GetGameMode<ArenaGameMode>() != null`).

**When it ticks** (Unreal's tick groups and tick priority): without the attribute, a tickable runs after every `MonoBehaviour.Update`.

```csharp
[Tick(TickGroup.LateUpdate, Order = -10, Unscaled = true)]
public sealed class CameraRig : WorldSubsystem, ITickable { public void Tick(float deltaTime) { } }
```

| Group | Runs | Unreal |
|---|---|---|
| `EarlyUpdate` | before every `MonoBehaviour.Update` | TG_PrePhysics (input, preparation) |
| `Update` (default) | after every `MonoBehaviour.Update`, with the timers | TG_DuringPhysics |
| `LateUpdate` | after every `MonoBehaviour.LateUpdate` | TG_PostUpdateWork (cameras) |
| `FixedUpdate` | each physics step, with the fixed time step | physics sub-stepping |

Lower `Order` runs first; `Unscaled` keeps it ticking while `Time.timeScale` is 0.

## Levels

A **level** begins when its scene finishes loading (before any `Start`) and ends when it unloads. From `Start` on, everything is there: the GameMode, the GameState, the players and the level's subsystems.

```csharp
World.BeganPlay += OnLevelStarted;        // also: World.EndingPlay
World.GetGameMode<ArenaGameMode>();       // typed getters, null if it is not that class
World.GetPlayerState<ArenaPlayerState>(); // player 0 by default
World.GetPawn<Runner>(1);                 // player 1
World.TimeSeconds;                        // since the level began

Levels.Open(nextLevel);                   // ends this level, loading screen, begins the next one
Levels.Open(nextLevel, bossLoadingScreen);// a specific loading screen (or null for none)
Levels.Stream("Caves");                   // additive: joins the current level
Levels.Unstream("Caves");
```

`Levels.Open` ends the level while its objects are still alive (`EndPlay` code can use them), loads the next one in the background and swaps when ready. Wait for it the way you prefer: `Completed += ...`, `yield return`, or `await`.

**Pick scenes in the Inspector** with `[SerializeField] SceneReference nextLevel;`: it follows renames, and warns with a one-click fix when the scene is not in Build Settings. In the Editor, a scene not in Build Settings still loads (with a warning), so prototypes just work.

**Loading screens**: derive from `LoadingScreen` (or use it as is: it fades a CanvasGroup), put it on a prefab and pick it in Project Settings. Override `OnProgress`, `Show` and `Hide` for your own animation.

## Gameplay framework

| Class | What it is | Where it lives |
|---|---|---|
| `GameMode` | the rules of a level, its default classes | a prefab, chosen per level |
| `GameState` | the state of the match everyone reads | plain object, one per level |
| `PlayerState` | the state of one player (survives the Pawn) | plain object, one per player |
| `PlayerController` | the player's will: input, possession | one per player |
| `AIController` | an AI driving a Pawn by code | one per AI Pawn |
| `Pawn` | what a player or an AI controls | spawned at a `PlayerStart`, placed, or `World.SpawnPawn` |
| `HUD` | one player's interface | one per player |
| `WorldSettings` | the level's GameMode override | a component in the scene |

```csharp
public class ArenaGameMode : GameMode<ArenaGameState, ArenaPlayerState>
{
    protected override void InitGame() { }                          // GameState created, no players yet
    protected override void StartPlay() { }                         // players spawned: the match starts
    protected override void OnPostLogin(PlayerController p) { }     // a player joined
    protected override PlayerStart FindPlayerStart(PlayerController p) => base.FindPlayerStart(p);

    void OnPlayerDied(PlayerController player) => RestartPlayer(player);   // spawns a new Pawn at a PlayerStart
}
```

The level's GameMode is, in order: a GameMode placed in the scene, the World Settings override, the project default. `AddPlayer()` / `RemovePlayer()` handle local players joining and leaving; `Possess(pawn)` moves control between Pawns, from a player to an AI and back.

**AI.** `PlayerController` and `AIController` both derive from `Controller`. Give a Pawn an AI Controller prefab and an **Auto Possess AI** (placed in the level, spawned, or both), and it creates its controller by itself:

```csharp
public class Chaser : AIController
{
    void Update() { if (Pawn != null) Pawn.transform.position += /* chase */ Vector3.zero; }
}

World.SpawnPawn(enemyPrefab, position, rotation);   // possessed right away if Auto Possess AI says "Spawned"
enemy.SpawnDefaultController();                      // or by hand
World.AIControllers;                                 // every AI of the level
```

An AI with **Wants Player State** gets a PlayerState of the GameMode's class and appears in `GameState.Players` (a bot on the scoreboard, `IsBot`). By default an AIController goes away with its Pawn.

**GameState and PlayerState are plain objects owned by the level**, not assets: nothing leaks between Play sessions or between the Editor and builds. What must outlive a level goes in the GameInstance or a game subsystem.

## Navigation

Unity's NavMeshAgent never tells you that it arrived. The usual check, `!pathPending && remainingDistance <= stoppingDistance`, fails in ways the tests of this package measured:

- Right after `Warp` or `ResetPath` the path is gone and `remainingDistance` is 0: it says "arrived" where the agent was teleported.
- On long paths `remainingDistance` is `Infinity` most of the way.
- If the destination cannot be reached, Unity moves `agent.destination` to the closest edge: comparing with it says "arrived" 10 m away, comparing with the point you asked for never does.
- With another agent standing on the destination, the agent jitters a couple of centimetres away and never stops.

**`MoveTo` ends every move with exactly one event**, like Unreal's `AAIController::MoveTo` and `OnMoveCompleted`:

```csharp
public class Guard : AIController
{
    void Patrol() => MoveTo(points[next].position);

    protected override void OnMoveCompleted(MoveResult result)
    {
        if (result.Outcome == MoveOutcome.Aborted) return;   // something else took over

        next = (next + 1) % points.Length;                   // reached it, or cannot: go on
        Timers.Set(this, result.IsSuccess ? 1f : 0f, static guard => guard.Patrol());
    }
}
```

| Outcome | When |
|---|---|
| `Success` | It stands within the acceptance radius (or the agent's stopping distance, whichever is larger) of where the complete path ends. Also when it can get no closer and its body already covers that point (another agent standing there). |
| `Partial` | The destination cannot be reached and it got as close as possible. Only with `allowPartial: true`; otherwise the move is `Invalid` at once, before walking. |
| `Blocked` | It did not move `BlockDistance` (0.1 m) in `BlockTime` (3 s): stuck against an obstacle, a closed door, an off-mesh link never finished. Or it kept moving inside a small area (two agent widths) for three `BlockTime`s: dancing around another agent, circling. |
| `Invalid` | No path, nothing walkable near the destination, or the agent is not on a NavMesh (it waits up to `BlockTime` for one to be built or loaded). |
| `Aborted` | A new move, `StopMovement`, the Pawn disabled or destroyed, the level ending, the Pawn changing controller, or the actor it followed destroyed. |

`result.Reason` says why (`AlreadyThere`, `NewRequest`, `Unreachable`, `DestinationNotOnNavMesh`...), with `Position`, `Destination` and `Duration`.

How it decides: it waits until the path is computed (it never reads stale path data from before `SetDestination`), keeps the end of the path being followed (Unity's `pathEndPosition` follows the agent once a path is dropped), checks arrival against that end with a horizontal radius and a height tolerance, never while crossing an off-mesh link, finds the path again when it is lost (`Warp`, NavMesh rebuilt), asks again once at the end of a partial path in case the end moves, and ends as `Blocked` when progress stops. A NaN destination, a disabled agent or a move requested while the level ends still get their one event.

**Ways to hear the end**, pick one:

```csharp
ai.MoveTo(point, this, static (self, result) => self.OnArrived(result));   // callback, allocates nothing
protected override void OnMoveCompleted(MoveResult result) { }             // in the controller
pawn.PathFollowing.MoveCompleted += result => { };                          // anyone listening
yield return ai.MoveTo(point).Wait();                                       // coroutine
var result = await ai.MoveTo(point);                                        // async method
```

The event never arrives inside `MoveTo`, so subscribing right after it is safe; it comes on a later frame, after every `LateUpdate`. The one exception is a move being replaced or stopped: it ends right away, before `MoveTo` or `StopMovement` returns. You can request the next move from any of them. When a `MoveTo` replaces a move, the `MoveTo` calls made by that move's handlers at that moment are ignored (they return `MoveRequest.None`), so a patrol that always moves on cannot steal the move that replaced it.

- **The Pawn needs a NavMeshAgent**; its `PathFollowing` component is added the first time it moves (add it yourself to set `BlockTime` and `BlockDistance`). It also works on a plain GameObject: `GetComponent<PathFollowing>().MoveTo(...)`.
- **Follow an actor** with `MoveToActor(target)`: it repaths as the actor moves and ends when touching it (its NavMeshAgent radius, NavMeshObstacle or Renderer size), also when the actor is an obstacle whose path is always partial.
- **Pause** with `PauseMove` / `ResumeMove`: a paused move is never Blocked. **Stop** with `StopMovement`, not `NavMeshAgent.ResetPath` (a path lost without it is found again). Setting the agent's speed to 0 ends the move as Blocked.
- `MoveStatus` (`Idle`, `Waiting`, `Moving`, `Paused`) and `MoveRequest.IsActive`, `Stop()`, `TryGetResult()`.
- `GameMode.RestartPlayer` stops the Pawn's move and warps its NavMeshAgent.
- The level's `NavigationSystem` (a level subsystem) ticks only the moves in progress, without allocating.

You still need a NavMesh: bake it, use NavMeshSurface from the AI Navigation package, or build it at runtime (the Patrol sample does). Navigation uses Unity's built-in AI module, on by default; without it this part of the package is left out and everything else works.

## Messages

Typed messages between systems that do not know each other. A message is a struct: broadcasting allocates nothing.

```csharp
public struct PlayerDied { public PlayerState Player; }

Messages.Broadcast(new PlayerDied { Player = player });

_listener = Messages<PlayerDied>.Listen(this, static (self, message) => self.ShowDeath(message.Player));
_listener.Dispose();
```

Use a `static` lambda that receives the owner, so nothing is captured. Listeners whose owner is a destroyed Unity object are removed by themselves. Do not rely on the order of the listeners.

## Timers

Like Unreal's FTimerManager. Replaces `Invoke` (which looks methods up by name) and coroutines (which allocate).

```csharp
_spawn = Timers.Set(this, 2f, static self => self.SpawnEnemy(), loop: true);
Timers.SetLooping(this, 0.5f, 2f, static self => self.Pulse());        // first after 0.5 s, then every 2 s
Timers.NextFrame(this, static self => self.LateSetup());
Timers.Set(this, 1f, static self => self.Blink(), flags: TimerFlags.Unscaled | TimerFlags.Persistent);

_spawn.Remaining; Timers.Pause(_spawn); Timers.Resume(_spawn);
Timers.Clear(ref _spawn);
```

Timers are cleared when the level ends unless `Persistent`; `Unscaled` ones keep running while `Time.timeScale` is 0. Timers of a destroyed Unity object stop by themselves. A long frame fires a looping timer once per missed interval.

## Gameplay Tags

Hierarchical names compared as numbers, like Unreal's gameplay tags: `Status.Burning` is a child of `Status`. Use them instead of strings, enums or booleans for states, damage types, teams, abilities.

```csharp
static readonly GameplayTag Stunned = GameplayTag.Request("Status.Stunned");

[SerializeField] private GameplayTag team;                 // picked in the Inspector
[SerializeField] private GameplayTagContainer states;      // a set of tags

if (states.HasTag(GameplayTag.Request("Status"))) { }     // true with "Status.Stunned": parents match
if (states.HasAll(required) && !states.HasAny(blocked)) { }
states.AddTag(Stunned); states.RemoveTag(Stunned);
```

List the project's tags in **Project Settings > UE Architecture > Gameplay Tags** (or Window > UE Architecture > Gameplay Tags); tag fields offer them in a searchable tree with "New tag…", and flag names that are not there with [Add to project]. **Generate C# constants** writes `Tags.Status.Burning` for every tag, so a typo is a compile error. Request a tag once (a static field) and compare it as often as you like: queries, adding and removing allocate nothing.

## Save games

Like Unreal's USaveGame: a class with the fields to keep, saved in named slots.

```csharp
[Serializable]
public sealed class Progress : SaveGame
{
    public int level;
    public GameplayTagContainer unlocked = new();

    public override int Version => 2;                                 // raise it when the fields change
    protected override void OnAfterLoad(int savedVersion) { }         // upgrade older saves here
}

SaveGames.Save(progress, "Slot 1");
var progress = SaveGames.Load<Progress>("Slot 1") ?? new Progress();
await SaveGames.SaveAsync(progress, "Slot 1");                       // the file is written on another thread
SaveGames.Exists("Slot 1"); SaveGames.Delete("Slot 1"); SaveGames.Slots();
```

Slots are JSON files in `persistentDataPath/SaveGames`, written to a temporary file and swapped in: a crash while saving never corrupts the previous save. A damaged file or a slot holding another class loads as `null` with a warning. Project Settings shows the folder and the slots.

## With ObserverVM

Nothing to set up: when `com.modernwestern.observer-vm` 2.0+ is installed, an extra assembly connects both packages.

- **Widgets in a HUD are bound by themselves** to the framework object matching their ViewModel type: the player's `PlayerState`, `Pawn`, `PlayerController` or `HUD`, then the `GameState`, the `GameMode`, a level subsystem, a game subsystem, the `GameInstance`.
- A Pawn Widget follows respawns; a Widget its parent already bound (`Bind.Child`) is left alone.
- **`FrameworkBinder`** does the same for Widgets outside the HUD (menus, overlays), and follows level changes.

```csharp
public sealed class ArenaPlayerState : PlayerState
{
    public Observable<int> Coins { get; } = new();   // a Widget<ArenaPlayerState> in the HUD shows it, no code
}
```

With ObserverVM 2.2 or newer, a second assembly (UEArchitecture.ObserverVM.Editor; left out with ObserverVM 2.0 and 2.1) adds editor tools through ObserverVM's extension points:

- **Bound by** in the widget inspector says what a widget will probably get, without Play mode: "Arena HUD: will probably bind to the Player State (ArenaPlayerState)", "Framework Binder (Player 1): will probably bind to PlayerCharacter (Pawn of Gameplay Mode, placed in this scene)", or in red "Nothing will bind this widget: it is outside a HUD and has no Framework Binder", with **Add Framework Binder** below. The answer follows the runtime's order (a test keeps them equal); it cannot see `ShouldCreate`, `SetViewModel` from code or `Bind.Child`, so it says "probably", and a widget a parent widget holds in a `[BindWidget]` field keeps ObserverVM's own answer. A HUD binds its widgets before the GameMode spawns the player's Pawn, so inside a HUD a widget whose ViewModel the Pawn fits but which is not a Pawn class (an interface, for example) is predicted to get what comes after the Pawn. Two mistakes are flagged: a Framework Binder inside a HUD prefab (it binds its player before the HUD can, so every player's HUD shows that player: remove it), and a HUD placed in a level (only the HUD a GameMode spawns for a player binds its widgets).
- **Framework Binder inspector**: a Player 1/2... popup and what the widget will probably get; a player the GameMode does not start with gets nothing, since the binder only sees players that exist when the level begins. In Play mode the live object with Ping.
- **Bind Framework Widgets Below** (Hierarchy right-click under GameObject > UE Architecture, or Project right-click on a prefab): a checklist of the framework widgets below, those a HUD or a parent widget binds unticked (also the widgets of a prefab placed inside a HUD prefab); Apply adds the binders in one Undo step. On a prefab placed in a level, "Add to" picks this copy (an override, the default) or the prefab itself (every copy); from the command line `apply-to=instance|prefab`. A widget's gear menu has UE Architecture > Add Framework Binder. Widgets that ObserverVM's "Widget for ViewModel" adds for a framework object outside a HUD get their binder by themselves (Project Settings > UE Architecture > Editor > Add Framework Binders).
- **Widgets in this HUD** (HUD inspector): which GameModes use the HUD (a popup picks the one to preview when several do), each widget with the framework object it will probably get, or "nothing matches" with Open script, and **Add Widget for** with the GameState, PlayerState, Pawn, Player Controller, HUD and subsystems that have an Observable, an ObservableList or a Command. An existing `Widget<T>` is added and filled at once (Generate UI or Create missing); otherwise a thin widget class is written and the widget is added inside the HUD prefab after the compile. Project right-click on a framework script > UE Architecture > **Create Widget for This** puts one inside a HUD, under a Canvas with a Framework Binder, or in a prefab only; a class with nothing to show gets a message saying what to add. The Game Mode Kit gains the HUD preset "HUD with Widgets for Game State and Player State" when they have something to show (otherwise it says it added nothing); the kit and the step that finishes after the compile save the HUD prefab file instead of opening it in Prefab Mode.
- **Pickers**: ObserverVM's ViewModel pickers list the GameState and PlayerState classes and the subsystems under "Framework (UE Architecture)".
- **Validation**: ObserverVM's Validation window (and its Play and build checks) lists framework widgets nothing binds (with Add Framework Binder), framework widgets of a level without a GameMode, HUD widgets nothing matches, a Framework Binder inside a HUD, and framework widgets of a HUD placed in a level; the Level overlay and `-uea validate` show "Bind 2 widgets". Prefabs outside a HUD depend on where they are placed and are not reported.
- Command line: `predict-binding`, `add-framework-binders` (with `dry-run=true` and `apply-to=instance|prefab`), `add-framework-binder`, `hud-widgets`, `add-hud-widget` and `create-widget-for` join the `-uea` verbs.

## Editor tools

- **Class fields with "+"** (like Unreal's class pickers): every GameMode, Player Controller, Pawn, HUD, AI Controller, Game Instance and Loading Screen slot has a picker with the prefabs, the classes that have no prefab yet (picking one builds its prefab at once, no compile), "New script…" (the prefab is built and assigned after the compile; the popup can copy the matching command line), "Variant of" the current prefab, "Open prefab" and "Open script". Prefabs named alike are told apart by their folder, and other levels' own copies are listed apart. Drop a script on the field to build and assign its prefab. A slot waiting for a compile shows a chip with "Finish now"; right-click an overridden slot for "Revert to <prefab>" next to Unity's own items. In Play mode the slots of level objects are locked. Generated prefabs are named after their class, in `Assets/Prefabs/Gameplay` or `Assets/Prefabs/UI` (Project Settings > UE Architecture > Editor).
- **Project Settings > UE Architecture**: GameInstance prefab, default GameMode (with its default classes editable in place), loading screen, start automatically, gameplay tags, save game slots, and the project's subsystems and levels (see below). The settings asset is created the first time you set something.
- **World Settings inspector**, like Unreal's: the GameMode Override and the **Selected GameMode** the level uses, with its Player Controller, Default Pawn, HUD, players, GameState and PlayerState. When the level uses the project default or a shared prefab, its rows are read-only (a shared prefab can be edited on purpose with "Edit shared prefab", which says how many levels change) and **Override for this level** makes a variant of it for this level only (`Assets/Prefabs/Modes/<Level>/<Class>.prefab`; two levels with the same name get `Modes/<Level> (<Folder>)`) and uses it; changed rows are bold and right-click reverts them. Clear override, Make project default, and a fix when a Pawn has no Player Start.
- **GameMode inspector**: the same default classes, and for a GameMode placed in a level, its **scene references** ("Fill N from this scene" assigns every field exactly one object of the level fits, in one Undo step; never overwrites).
- **Project window > UE Architecture**: Create Prefab (from one or several framework scripts), Create Prefab and Use as (Player Controller, Default Pawn, HUD of the selected GameMode prefab, or of the project default after asking; Project Game Instance, Loading Screen), and on GameMode prefabs Set as Project Default GameMode, Use in Active Level, Create Variant for Active Level, Create Missing Default Classes. The GameMode and World Settings component menus have the same verbs. **Remove Files Made by the Last Action** moves the prefabs, scripts and folders the last action created to the trash (Undo cannot remove files).
- **Gameplay tags**: a tag field's ▾ opens a searchable tree (typing "burn" finds Status.Burning) whose last item, "New tag…", adds the tag to the project and puts it in the field. A value that is not a project tag reads "\"Status.Burnin\" is not a project tag" with [Add to project] [Pick]. A container opens a checkbox tree. The tag manager (Project Settings > UE Architecture > Gameplay Tags, or Window > UE Architecture > Gameplay Tags) shows the tags as a tree with search, Add and Add sub-tag inline, **Rename…** ("Update 14 references in 6 assets; 2 levels listed, not changed": the tag and the tags below it change in Project Settings and in the tag fields of prefabs and every other asset, Animator Controllers, Timelines and presets included; levels and scripts that use the name are listed for you to change, never rewritten; the generated constants file is not counted, generate it again instead), **Delete…** (with how often it is used; the parent stays) and **Generate C# constants**: `GameplayTags.g.cs` with `Tags.Status.Burning` (a parent's own tag is `Tags.Status.Tag`), in the scripts folder by default (Project Settings > UE Architecture > Editor > Tag Constants File), after checking that the folder's assembly references UEArchitecture.Runtime ("Add reference" fixes it). It never overwrites a file it did not write, and refuses tags whose C# names would clash ("1Up" and "_1Up"); a name that needs no escaping always means its own tag.
- **SceneReference fields**: scene picker with a one-click "Add to Build" and "This level".
- **Window > UE Architecture > Debugger**: the running game, its subsystems (with their tick group), the level with its GameMode, GameState, players, AIs and subsystems, the moves in progress, active timers and message listeners, with live public values.
- **Path Following inspector**: in Play mode, the move in progress (status, destination, acceptance radius, seconds without progress) and how the last one ended. A selected agent draws its path and where arrival is measured.
- **New Framework Class** (Assets > Create > UE Architecture > Class…, "Create Child Class…" on a framework script, "New class (more options)…" in a class field's picker), like Unreal's "Pick Parent Class": the common classes with what each is for, a search over every framework class and yours, the name checked as you type, the folder with its namespace and assembly, the tick of a subsystem, new or existing GameState and PlayerState for a GameMode, "Also create the prefab" and "Then use it as". One compile. When the folder's asmdef lacks a reference the class needs, "Add reference" adds it (keeping the asmdef's GUID or name style).
- **Game Mode Kit** (Window > UE Architecture > New Game Mode…, Assets > Create > UE Architecture > Game Mode Kit…, "New Game Mode set…" on GameMode fields, "Create a GameMode…" in the Level overlay): a name, then New, Existing, Base class or None for the GameMode, GameState, PlayerState, Player Controller, Default Pawn, HUD and AI Controller, a Pawn preset (empty, a capsule, a capsule with a NavMeshAgent) and a HUD that is a ready Canvas, used as the project default or by one level, with a Player Start per player. One compile, then every prefab is built and wired; the request waits in a pending step, so closing the window or the Editor loses nothing. Running it again fills only what is missing. The classes are the usual empty templates: no gameplay code to maintain.
- **Level overlay** (Scene view; hide it from the Overlays menu): "GameMode: ArenaGameMode (project default) · Starts 1/1 · In build", one accent button with the next fix (Create a GameMode…, Add Player Start, Add EventSystem, Add AudioListener, Override for this level) and the level's problems with "Fix all safe" (one Undo step; a HUD prefab is changed in its file, without opening Prefab Mode). Notes such as "Not in Build Settings" keep their own button (Add to Build) but are never the accent button nor part of "Fix all safe": which levels ship is your choice. In Prefab Mode it lists the prefab's problems. Pressing Play in a level without a GameMode says so once in the Console (Project Settings > UE Architecture > Editor turns it off).
- **Levels**: Assets > Create > UE Architecture > Level makes a playable level (World Settings, a Player Start per player on the ground, a camera aimed at it, a light, an EventSystem with the input module that fits the project, an AudioListener unless the GameMode's Pawn or Player Controller brings one, a ground plane), adds it to Build Settings and opens it. GameObject > UE Architecture > Set Up This Level adds only what is missing and says what was already fine; Player Starts for Every Player adds the missing starts side by side. New Player Starts land on the ground below the Scene view's pivot and face the view.
- **HUD and Loading Screen prefabs**: "New HUD prefab (Canvas ready)" and "New default loading screen" in the class picker, each a new prefab in the UI prefab folder named after its class, or after the GameMode's set for a GameMode's HUD (`ArenaHUD.prefab` for ArenaGameMode; an existing file is never reused, the new one gets the next free name) (a Canvas drawn over the level, a Canvas Group the LoadingScreen fades, a background and a Filled progress bar to fill in `OnProgress`). They need the Unity UI package (an optional assembly, UEArchitecture.Editor.UGUI); never an EventSystem in a HUD prefab, which is spawned once per player.
- **Problems with one fix**, on the object that has them: HUD and Loading Screen inspectors (no Canvas or UI Document, an empty Canvas Group, a low sorting order, the level's missing EventSystem, "Find children by name" for empty references, a Preview slider that shows a copy in the Game view), the Player Start inspector (floating, off the NavMesh, a tag another start has, a menu of the level's tags), and the Pawn, GameMode and World Settings inspectors.
- **Assets > Create > UE Architecture**: GameInstance, game and level subsystems (also tickable, with `[Tick]`), GameMode (+ GameState + PlayerState, or each in its own file: Project Settings > UE Architecture > Editor > Separate State Files), GameState, PlayerState, PlayerController, AIController, Pawn, HUD, Player Start, LoadingScreen, SaveGame and Message scripts, in the folder's root namespace. A name that is taken (a file or a class) is reported instead of writing "NewPawn 1.cs".
- **Pawn inspector**: "Controlled by: the player (Default Pawn of Arena Mode)", "AI" or "nobody yet" (a copy of the Default Pawn placed in a level is not the player's: the GameMode spawns its own; a placed Pawn with Auto Possess AI "Spawned" is possessed only when `World.SpawnPawn` spawns it); an AI section (folded for a player's Pawn) with Auto Possess AI and the AI Controller class field, and fixes for "Auto Possess is on but there is no AI Controller" (Create AI Controller prefab, or Use your only AI Controller class) and "An AI Controller is set but Auto Possess AI is Disabled" (Placed, Spawned, Both). With Unity's AI module, a Navigation section: Add NavMeshAgent + Path Following in one click, Block Time and Block Distance inline, "Off the NavMesh" with Snap, and "No NavMesh in this level". The Pawn's gear menu has **Make AI Pawn** (pick the AI Controller class) and Add Path Following; a NavMeshAgent's gear menu and Project right-click on several Pawn prefabs have **Add Path Following** (tunable per prefab, instead of adding it from code).
- **GameObject > UE Architecture**: World Settings, Player Start (in the clicked object's scene or the prefab open in Prefab Mode; a Player Start goes to the Scene view's pivot). Add Component > UE Architecture lists the framework components.
- **Subsystems list** (Project Settings > UE Architecture): each subsystem with a Game or World chip, when it ticks ("Tick: LateUpdate, order 10", from `[Tick]`), [Open] and [Ping] its script, and [+ Game subsystem] [+ World subsystem], which open New Framework Class prefilled.
- **Levels list** (Project Settings > UE Architecture): the Build Settings scenes in order with their build index, then the project's other levels, each with its GameMode and where it comes from, World Settings and Player Starts, and the fixes [Open], [Add to Build], [Set as first scene] and [Use project default]. Read from the saved level files and labelled approximate (a GameMode inside a nested prefab is not seen; a World Settings placed as a prefab shows the prefab's own override); cached per level, so the page never scans the project on repaint.
- **Command line** for the tags and the lists: `add-tag tag=Status.Burning`, `rename-tag from=Status to=Condition` and `remove-tag tag=...` (a preview, exit code 2, while an asset, a level or a script uses the tag; `apply=true` does it), `find-tag-usages tag=...`, `generate-tag-constants [path=Assets/...cs] [namespace=...] [fix-references=true]`, `list-levels`, `list-subsystems`, `add-to-build scene=...` and `set-first-level scene=...`. The matching actions are `UEArchitectureActions.AddTag`, `RenameTag`, `RemoveTag`, `FindTagUsages`, `GenerateTagConstants`, `ListLevels`, `ListSubsystems`, `AddSceneToBuild` and `SetFirstLevel`.

Every button and menu calls a public static method of `UEArchitectureActions` that returns an `ActionResult`, is one Undo step and creates nothing the second time. They also run from the command line, printing one JSON line:

```
Unity -batchmode -projectPath . -executeMethod ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run -uea override-gamemode scene=Assets/Scenes/Boss.unity -quit
```

Verbs: `assign-class`, `assign-project-class`, `create-class-prefab`, `create-variant`, `save-as-prefab`, `revert-override`, `override-gamemode`, `create-gamemode-variant`, `set-default-gamemode`, `use-gamemode`, `clear-override`, `create-missing-default-classes`, `fill-scene-references`, `new-script`, `assign`, `remove-files`, `complete-pending`, `list-pending`, `cancel-pending`, `create-class`, `new-gamemode-kit`, `create-level`, `set-up-level`, `add-player-starts`, `create-hud-prefab`, `create-loading-screen`, `find-children`, `validate`, `fix-all`, `make-ai-pawn`, `set-auto-possess`, `add-path-following`, `snap-to-navmesh`, and with ObserverVM 2.2 the verbs of [With ObserverVM](#with-observervm) (`-uea help` lists their arguments). Objects are addressed as `Assets/Scenes/Boss.unity#Path/To/Object` or a prefab path. The JSON line goes to the log (to standard output with `-logFile -`) and to `Library/UEArchitecture/LastCliResult.json`. Exit codes: 0 done, 1 problems, 2 part of the work still waits. "Copy CLI command" copies the arguments only, from `-batchmode` to `-quit`: put the Unity Editor before them (`Unity.exe` on Windows, `Unity.app/Contents/MacOS/Unity` on macOS; in PowerShell, `& "C:\...\Unity.exe"`).

For CI, `-uea validate scenes=build` checks the enabled levels of Build Settings (or `open`, `all`, or paths separated by `;`) and exits with 1 on errors (`strict=true` counts warnings too); `-uea fix-all scenes=build` applies the fixes that only add what is missing (Player Starts, EventSystem, AudioListener, a missing Canvas); it never adds levels to Build Settings, and saves only the levels it changed. A HUD without a Canvas or a UI Toolkit UI Document is a warning (it may draw in another way), so it fails validate only with `strict=true`. A Game Mode set from the command line: `-uea new-gamemode-kit name=Arena use=default`, then `-uea complete-pending`.

Verbs that write scripts finish after the compile. Either run them with `-quit`, then run `-uea complete-pending` (it opens the levels it fills, saves and closes them); or run them without `-quit`: the batch Editor waits for the compile, finishes the same way and exits by itself, giving up after `timeout=<seconds>` (600 by default).

### Placed Pawns and moving an existing project

**Auto Possess Player.** A Pawn placed in a level can be possessed by a player as it joins, instead of the GameMode spawning its Default Pawn, like Unreal's Auto Possess Player: in the Pawn inspector's Possession section pick Player 1 (or `pawn.AutoPossessPlayer = 0` from code). The placed Pawn wins over the Default Pawn, once per level as in Unreal: after that (it died, the player left it, or a second Pawn was placed for the same player) `RestartPlayer` spawns the Default Pawn. One your code already possessed is left alone. The player also wins over Auto Possess AI, as in Unreal: a placed Pawn with a player set does not get its AI controller as the level begins; it waits for its player (Player 2 waits until your code calls `AddPlayer` when the GameMode starts with one player). The GameMode looks for placed Pawns once, as the level begins, so nothing runs per frame. The GameMode panel and the Level overlay say 'Player 1 possesses the placed Pawn "X"', or that it waits for Player 1 to join when the GameMode starts with no players. Verb: `set-auto-possess-player target=<Pawn address> player=1|none`.

**Moving an existing project to the framework.** On a MonoBehaviour's gear menu, UE Architecture > Convert to GameMode… (or Player Controller, AI Controller, Pawn, HUD, Player Start, Loading Screen, Game Instance) shows the one line that changes, the GameState and PlayerState scripts it writes, the methods that must call the base class, the fields that would clash, and the scenes and prefabs that use the class, and can rename the class and its file (the file keeps its GUID, so nothing loses the component; the objects named after the old class are renamed after the compile). It asks before writing and writes exactly what the preview showed, all or nothing (if the script changed on disk since, nothing is written); Undo cannot take back a script edit, so copy the original first if you may want it back. Only UTF-8 scripts are edited (a byte order mark is kept); a script in another encoding is refused until you re-save it as UTF-8, so no accent is lost. Unusual code (partial, nested or generic classes, declarations with comments or `#if` inside) is refused with the reason, and so is a rename whose old name may not be the class everywhere (qualified names, interpolated strings, an enum value or member with the same name). A GameMode's gear menu has Change Game State and Player State Classes…, and framework components have Change Class… (in place, where the component was added; it lists the fields that are lost; Undo cannot swap a class back: Change Class again restores the class, but the lost fields start empty). Project right-click on a script: Find Usages in Scenes and Prefabs.

```
Unity -batchmode -projectPath . -executeMethod ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run -uea convert-class script=Assets/Game/GameplayScene.cs to=GameMode rename=GameplayGameMode -quit
```

Without `apply=true` the verbs that edit scripts or components (`convert-class`, `change-class`, `change-gamemode-states`) print the preview and change nothing. A renamed class finishes with `-uea complete-pending` after the compile (exit code 2 until then). `find-usages script=<path>` lists where a class is used.

## Zero allocations

Measured by tests in the Editor and in IL2CPP players: `GameInstance.Get`, `World.Get` and the typed getters, `Messages.Broadcast`, listening and stopping, setting, firing and clearing timers, a frame of the framework (timers + tickable subsystems), gameplay tag queries, adding and removing, `MoveTo` with a static callback, a tick of every move in progress, and looping over `World.Players`, `GameState.Players`, `PlayerStart.All` and the subsystem lists with `foreach` (they are `ListView<T>`, not interfaces that box their enumerator). The first call of each kind may allocate once (arrays grow, Mono creates static lambdas).

Subsystems are created by reflection and save games are filled by the serializer; a `link.xml` generated at build time keeps both in IL2CPP builds with code stripping. Without it, a High-stripped build loses subsystems no script mentions, and even the constructor of the ones it mentions.

## Samples

Package Manager → **UE Architecture** → **Samples**. They work with the Built-in pipeline or URP, and with the legacy Input Manager or the Input System package. The Arena samples need TextMeshPro.

- **Arena** (start here): collect the coins before the time runs out. A GameMode with GameState and PlayerState, a PlayerController driving a Pawn spawned at a Player Start, coins that announce themselves with Messages, a level subsystem spawning them with Timers, a HUD fed by messages and timers, a game subsystem keeping the best score in a save game while the level reopens behind a loading screen.
- **Arena + ObserverVM**: the same game with Observable states and a HUD made of Widgets with no code, bound by the HUD. Needs ObserverVM.
- **Patrol**: navigation. Two guards patrol with `MoveTo` and react to how each move ended: a point on a platform nobody can reach (`Invalid`), a gate that closes in the way (`Blocked`), and a chase with `MoveToActor` when they see you. You walk where you click (a click on the platform ends `Partial`). The level builds its NavMesh as it begins: nothing to bake, no package to add.

## Running the tests

Add the package to `testables` in `Packages/manifest.json`, then Window > General > Test Runner, or from the command line:

```
Unity -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testResults results.xml
```

## Migrating from 1.x

| 1.x | 2.0 |
|---|---|
| `PersistentLevel` scene + `GameInstance` component in it | nothing: the GameInstance starts by itself (custom class: a prefab in Project Settings) |
| `SceneRoot.Construct / Init / Deconstruct` | a `GameMode` (`InitGame`, `StartPlay`, `OnEndPlay`) or a `WorldSubsystem` |
| `GameState` / `PlayerState` ScriptableObjects + `Clear()` | `GameState` / `PlayerState` plain classes created per level |
| `GameEvents` ScriptableObject | `Messages` (typed structs) |
| `GameInstance.LoadScene(name)` | `Levels.Open(scene)` / `Levels.Stream(scene)` |
| static `MyGameInstance.GameState` | `World.GetGameState<T>()`, `World.GetPlayerState<T>()` |

## Unity versions

Declared minimum: 2021.3 (not tested yet). Verified with 2022.3 (Built-in pipeline; Editor, and IL2CPP players with High code stripping, including the Patrol sample), 6000.1 (URP with the Input System package only; Editor) and 6000.3 LTS (URP, inside a game; Editor). Without Unity's AI module, everything but navigation compiles and passes its tests. The 2.2.0 editor tools also compile and pass their EditMode tests on 6000.6 (and 6000.1); the 2.3.0 tools on 2022.3 (with and without ObserverVM, and without the AI module) and 6000.6; the 2.4.0 tools on 2022.3 (with ObserverVM 2.2, with ObserverVM 2.0 where the ObserverVM editor assembly is left out, and without ObserverVM or the AI module) and 6000.6 (with ObserverVM 2.2). The Level overlay uses the Overlay API of 2021.2 and later.
