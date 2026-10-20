# UE Architecture

Unreal Engine's gameplay framework for Unity: a **GameInstance** that starts by itself, **Subsystems** instead of singletons, **GameMode / GameState / PlayerState / PlayerController / AIController / Pawn / HUD** chosen per level, **Levels** with loading screens, typed **Messages**, **Timers**, **Gameplay Tags** and **Save Games**. What runs every frame allocates nothing.

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

Requires Unity 2021.3+. No other dependency.

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

3. **Give the level rules.** Assets > Create > UE Architecture > Game Mode creates `ArenaGameMode`, `ArenaGameState` and `ArenaPlayerState`. Put the GameMode on a prefab and fill in its **default classes**: a PlayerController prefab, a Pawn prefab and a HUD prefab.
4. **Use it in a level.** GameObject > UE Architecture > World Settings (pick the GameMode prefab) and > Player Start. Or set a default GameMode for every level in **Project Settings > UE Architecture**.
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

List the project's tags in **Project Settings > UE Architecture > Gameplay Tags**; tag fields offer them in a nested menu and flag names that are not there. Request a tag once (a static field) and compare it as often as you like: queries, adding and removing allocate nothing.

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

## Editor tools

- **Project Settings > UE Architecture**: GameInstance prefab, default GameMode, loading screen, start automatically, gameplay tags, save game slots. Lists the subsystems of the project.
- **Gameplay tag fields**: a nested menu of the project tags; unknown names are flagged.
- **GameMode inspector**: what it creates (GameState, PlayerState, PlayerController, Pawn, HUD). **World Settings inspector**: the GameMode the level will use and why, and a fix when a Pawn has no Player Start.
- **SceneReference fields**: scene picker with a one-click "Add to Build".
- **Window > UE Architecture > Debugger**: the running game, its subsystems (with their tick group), the level with its GameMode, GameState, players, AIs and subsystems, active timers and message listeners, with live public values.
- **Assets > Create > UE Architecture**: GameInstance, subsystems, GameMode (+ GameState + PlayerState), PlayerController, AIController, Pawn, HUD and LoadingScreen scripts, in the folder's root namespace.
- **GameObject > UE Architecture**: World Settings, Player Start.

## Zero allocations

Measured by tests in the Editor and in IL2CPP players: `GameInstance.Get`, `World.Get` and the typed getters, `Messages.Broadcast`, listening and stopping, setting, firing and clearing timers, a frame of the framework (timers + tickable subsystems), gameplay tag queries, adding and removing, and looping over `World.Players`, `GameState.Players`, `PlayerStart.All` and the subsystem lists with `foreach` (they are `ListView<T>`, not interfaces that box their enumerator). The first call of each kind may allocate once (arrays grow, Mono creates static lambdas).

Subsystems are created by reflection and save games are filled by the serializer; a `link.xml` generated at build time keeps both in IL2CPP builds with code stripping. Without it, a High-stripped build loses subsystems no script mentions, and even the constructor of the ones it mentions.

## Samples

Package Manager → **UE Architecture** → **Samples**. Both need TextMeshPro and work with the Built-in pipeline or URP, and with the legacy Input Manager or the Input System package.

- **Arena** (start here): collect the coins before the time runs out. A GameMode with GameState and PlayerState, a PlayerController driving a Pawn spawned at a Player Start, coins that announce themselves with Messages, a level subsystem spawning them with Timers, a HUD fed by messages and timers, a game subsystem keeping the best score in a save game while the level reopens behind a loading screen.
- **Arena + ObserverVM**: the same game with Observable states and a HUD made of Widgets with no code, bound by the HUD. Needs ObserverVM.

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

Declared minimum: 2021.3 (not tested yet). Verified with 2022.3 (Built-in pipeline; Editor, and IL2CPP players with High code stripping) and 6000.1 (URP with the Input System package only; Editor).
