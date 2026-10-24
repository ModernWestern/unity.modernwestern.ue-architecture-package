using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// When a tickable subsystem ticks: the [Tick] attribute the template writes, e.g.
    /// <c>[Tick(TickGroup.LateUpdate, Order = -10, Unscaled = true)]</c>.
    /// </summary>
    internal readonly struct TickOptions
    {
        public readonly TickGroup Group;

        public readonly int Order;

        public readonly bool Unscaled;

        public TickOptions(TickGroup group, int order = 0, bool unscaled = false)
        {
            Group = group;

            Order = order;

            Unscaled = unscaled;
        }

        /// <summary>
        /// The attribute line, with only the values that differ from the defaults.
        /// </summary>
        public string Attribute()
        {
            var text = new StringBuilder("[Tick(TickGroup.").Append(Group);

            if (Order != 0)
            {
                text.Append(", Order = ").Append(Order);
            }

            if (Unscaled)
            {
                text.Append(", Unscaled = true");
            }

            return text.Append(")]").ToString();
        }

        /// <summary>
        /// "every frame, after every MonoBehaviour.Update" and so on, for the comment above the class.
        /// </summary>
        public string When()
        {
            string when;

            switch (Group)
            {
                case TickGroup.EarlyUpdate:
                    when = "every frame, before every MonoBehaviour.Update";

                    break;

                case TickGroup.LateUpdate:
                    when = "every frame, after every MonoBehaviour.LateUpdate";

                    break;

                case TickGroup.FixedUpdate:
                    when = "on every physics step";

                    break;

                default:
                    when = "every frame, after every MonoBehaviour.Update";

                    break;
            }

            return Unscaled ? when + ", with unscaled time (also while the game is paused)" : when;
        }
    }

    /// <summary>
    /// The source of every generated framework script: the Assets > Create > UE Architecture templates (GameMode with its
    /// GameState and PlayerState, or each in its own file, PlayerController, AIController, Pawn, HUD, PlayerStart,
    /// LoadingScreen, GameInstance, game and level subsystems, tickable ones with [Tick], SaveGame and Message), a child of
    /// a GameMode of yours, and a child of any class (Unreal's "Create Blueprint class based on X"). Used by
    /// CreateFrameworkScripts and ScriptWriter.
    /// </summary>
    internal static class ClassTemplates
    {
        private const string FrameworkNamespace = "ModernWestern.UEArchitecture";

        private static readonly Dictionary<Type, CreateFrameworkScripts.Kind> _kinds = new()
        {
            { typeof(GameInstance), CreateFrameworkScripts.Kind.GameInstance },
            { typeof(GameInstanceSubsystem), CreateFrameworkScripts.Kind.GameInstanceSubsystem },
            { typeof(WorldSubsystem), CreateFrameworkScripts.Kind.WorldSubsystem },
            { typeof(GameMode), CreateFrameworkScripts.Kind.GameMode },
            { typeof(PlayerController), CreateFrameworkScripts.Kind.PlayerController },
            { typeof(AIController), CreateFrameworkScripts.Kind.AIController },
            { typeof(Pawn), CreateFrameworkScripts.Kind.Pawn },
            { typeof(HUD), CreateFrameworkScripts.Kind.HUD },
            { typeof(LoadingScreen), CreateFrameworkScripts.Kind.LoadingScreen },
            { typeof(GameState), CreateFrameworkScripts.Kind.GameState },
            { typeof(PlayerState), CreateFrameworkScripts.Kind.PlayerState },
            { typeof(PlayerStart), CreateFrameworkScripts.Kind.PlayerStart },
            { typeof(SaveGame), CreateFrameworkScripts.Kind.SaveGame }
        };

        private static readonly Dictionary<CreateFrameworkScripts.Kind, Type> _parents = new()
        {
            { CreateFrameworkScripts.Kind.TickableGameInstanceSubsystem, typeof(GameInstanceSubsystem) },
            { CreateFrameworkScripts.Kind.TickableWorldSubsystem, typeof(WorldSubsystem) },
            { CreateFrameworkScripts.Kind.Message, null }
        };

        private static readonly Dictionary<CreateFrameworkScripts.Kind, string[]> _referenced = new();

        private static HashSet<string> _frameworkNames;

        /// <summary>
        /// The template kind for a framework base class (GameMode, Pawn, SaveGame...), or null for any other parent.
        /// </summary>
        public static CreateFrameworkScripts.Kind? KindOf(Type parent)
        {
            return parent != null && _kinds.TryGetValue(parent, out var kind) ? kind : (CreateFrameworkScripts.Kind?)null;
        }

        /// <summary>
        /// The framework base class a kind derives from (null for a Message, which is a struct).
        /// </summary>
        public static Type ParentOf(CreateFrameworkScripts.Kind kind)
        {
            return _parents.TryGetValue(kind, out var parent) ? parent : _kinds.First(pair => pair.Value == kind).Key;
        }

        /// <summary>
        /// True for the kinds whose class is named like a GameMode ("Arena" gives "ArenaGameMode"): the GameMode template,
        /// and children of a GameMode of yours.
        /// </summary>
        public static bool IsGameModeNamed(CreateFrameworkScripts.Kind? kind, Type parent)
        {
            return kind == CreateFrameworkScripts.Kind.GameMode || parent != null && typeof(GameMode).IsAssignableFrom(parent);
        }

        /// <summary>
        /// The source of a template kind, in <paramref name="rootNamespace"/> when one is given.
        /// </summary>
        public static string Source(CreateFrameworkScripts.Kind kind, string typedName, string rootNamespace)
        {
            return Source(kind, typedName, rootNamespace, false, null);
        }

        /// <summary>
        /// The source of a template kind. <paramref name="splitStates"/> leaves a GameMode's GameState and PlayerState out
        /// (they get their own files from the GameState and PlayerState kinds); <paramref name="tick"/> sets when a tickable
        /// subsystem ticks (Update, order 0, scaled time by default).
        /// </summary>
        public static string Source(CreateFrameworkScripts.Kind kind, string typedName, string rootNamespace, bool splitStates, TickOptions? tick)
        {
            var name = CreateFrameworkScripts.ClassName(typedName);

            var timing = tick ?? new TickOptions(TickGroup.Update);

            var body = (kind == CreateFrameworkScripts.Kind.GameMode && splitStates ? GameModeOnlyBody : Body(kind))
                       .Replace("#NAME#", name)
                       .Replace("#BASE#", BaseName(name, "GameMode", "Mode"))
                       .Replace("#TICK#", timing.Attribute())
                       .Replace("#WHEN#", timing.When());

            return Wrap(body, rootNamespace);
        }

        /// <summary>
        /// The classes a template kind declares: a GameMode script also declares its GameState and PlayerState, unless
        /// they are split into their own files.
        /// </summary>
        public static string[] DeclaredClasses(CreateFrameworkScripts.Kind kind, string className, bool splitStates = false)
        {
            if (kind != CreateFrameworkScripts.Kind.GameMode || splitStates)
            {
                return new[] { className };
            }

            var baseName = BaseName(className, "GameMode", "Mode");

            return new[] { baseName + "GameMode", baseName + "GameState", baseName + "PlayerState" };
        }

        /// <summary>
        /// The UE Architecture classes a template's code names (not its comments), e.g. Pawn and Controller for the Pawn
        /// template: a new class with one of these names would hide it, and the script would not compile.
        /// </summary>
        public static IReadOnlyList<string> ReferencedFrameworkTypes(CreateFrameworkScripts.Kind kind)
        {
            return ReferencedFrameworkTypeNames(kind).Select(name => name.Split('`')[0]).Distinct().ToArray();
        }

        /// <summary>
        /// <see cref="ReferencedFrameworkTypes"/> as the names the runtime gives them ("GameMode`2" for the GameMode&lt;A, B&gt;
        /// the GameMode template derives from), to look for classes of the project with the same name and arity.
        /// </summary>
        public static IReadOnlyList<string> ReferencedFrameworkTypeNames(CreateFrameworkScripts.Kind kind)
        {
            if (_referenced.TryGetValue(kind, out var known))
            {
                return known;
            }

            // Placeholders become part of a longer name, so "#BASE#GameMode" does not count as the class GameMode.
            var source = Body(kind).Replace("#NAME#", "Placeholder_").Replace("#BASE#", "Placeholder_").Replace("#TICK#", new TickOptions(TickGroup.Update).Attribute());

            var code = string.Join("\n", source.Replace("\r\n", "\n").Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                                                                                             !line.StartsWith("using ", StringComparison.Ordinal)));

            known = FrameworkTypes_Internal().Where(type =>
                                              {
                                                  var name = Regex.Escape(type.Name.Split('`')[0]);

                                                  if (type.IsGenericTypeDefinition)
                                                  {
                                                      return Regex.IsMatch(code, $@"\b{name}\s*<");
                                                  }

                                                  // [Tick(...)] names TickAttribute.
                                                  var shortName = typeof(Attribute).IsAssignableFrom(type) && name.EndsWith("Attribute", StringComparison.Ordinal)
                                                                      ? name.Substring(0, name.Length - "Attribute".Length)
                                                                      : null;

                                                  return Regex.IsMatch(code, $@"\b{name}\b(?!\s*<)") || shortName != null && Regex.IsMatch(code, $@"\[\s*{shortName}\b");
                                              })
                                              .Select(type => type.Name)
                                              .OrderBy(name => name, StringComparer.Ordinal)
                                              .ToArray();

            _referenced[kind] = known;

            return known;
        }

        /// <summary>
        /// True when <paramref name="name"/> is the name of a UE Architecture class (Pawn, HUD, GameMode, World...): a class
        /// of yours with that name hides it in your scripts that use it, so they stop compiling.
        /// </summary>
        public static bool IsFrameworkName(string name)
        {
            _frameworkNames ??= new HashSet<string>(FrameworkTypes_Internal().Select(type => type.Name.Split('`')[0]), StringComparer.Ordinal);

            return !string.IsNullOrEmpty(name) && _frameworkNames.Contains(name);
        }

        /// <summary>
        /// True for a UE Architecture class itself (not a class of yours that derives from one).
        /// </summary>
        public static bool IsFrameworkType(Type type)
        {
            return type != null && type.Assembly == typeof(GameMode).Assembly && type.Namespace == FrameworkNamespace;
        }

        /// <summary>
        /// The source of a class deriving from <paramref name="parent"/>: the framework template when the parent is a
        /// framework base class, otherwise an empty child that keeps everything its parent does.
        /// </summary>
        public static string ForParent(Type parent, string className, string rootNamespace)
        {
            var kind = KindOf(parent);

            if (kind.HasValue)
            {
                return Source(kind.Value, className, rootNamespace);
            }

            var usings = new SortedSet<string>(StringComparer.Ordinal) { "UnityEngine", "ModernWestern.UEArchitecture" };

            if (!string.IsNullOrEmpty(parent.Namespace) && parent.Namespace != rootNamespace && !parent.IsNested)
            {
                usings.Add(parent.Namespace);
            }

            var parentName = TypeReference(parent, className);

            var code = new StringBuilder();

            foreach (var name in usings)
            {
                code.Append("using ").Append(name).Append(";\n");
            }

            code.Append('\n');

            if (typeof(GameMode).IsAssignableFrom(parent))
            {
                code.Append("// A child of ").Append(parent.Name).Append(": the same rules, GameState and PlayerState (")
                    .Append(GameModeInfo.GameStateType(parent).Name).Append(", ").Append(GameModeInfo.PlayerStateType(parent).Name)
                    .Append("), like a child Blueprint. Override only\n// what changes for its levels, and pick its prefab in their World Settings.\n");
            }
            else
            {
                code.Append("// A child of ").Append(parent.Name).Append(": it does everything ").Append(parent.Name)
                    .Append(" does (like a child Blueprint). Override only what changes.\n");
            }

            code.Append("public class ").Append(className).Append(" : ").Append(parentName).Append('\n');

            code.Append("{\n}\n");

            return Wrap(code.ToString(), rootNamespace);
        }

        /// <summary>
        /// A name without one of its suffixes: BaseName("ArenaGameMode", "GameMode", "Mode") is "Arena".
        /// </summary>
        public static string BaseName(string name, params string[] suffixes)
        {
            foreach (var suffix in suffixes)
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return name.Substring(0, name.Length - suffix.Length);
                }
            }

            return name;
        }

        /// <summary>
        /// Moves the usings to the top and puts the rest in the namespace, indented.
        /// </summary>
        public static string Wrap(string body, string rootNamespace)
        {
            var usings = new StringBuilder();

            var code = new StringBuilder();

            foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
            {
                (line.StartsWith("using ", StringComparison.Ordinal) ? usings : code).Append(line).Append('\n');
            }

            if (string.IsNullOrWhiteSpace(rootNamespace))
            {
                return usings + code.ToString().TrimStart('\n');
            }

            var indented = new StringBuilder();

            foreach (var line in code.ToString().Trim('\n').Split('\n'))
            {
                indented.Append(line.Length == 0 ? string.Empty : "    " + line).Append('\n');
            }

            return $"{usings}\nnamespace {rootNamespace}\n{{\n{indented}}}\n";
        }

        /// <summary>
        /// How a generated script names its parent: by its name, or in full ("global::Game.HUD") when the name alone would
        /// mean another class (the new class itself, a nested class, or a class named like a UE Architecture class, which
        /// "using ModernWestern.UEArchitecture;" would make ambiguous).
        /// </summary>
        public static string TypeReference(Type type, string className)
        {
            var full = (type.FullName ?? type.Name).Replace('+', '.');

            if (type.Name == className || type.IsNested || IsFrameworkName(type.Name) && !IsFrameworkType(type))
            {
                return "global::" + full;
            }

            return type.Name;
        }

        /// <summary>
        /// The public classes of the UE Architecture namespace (not nested: those are named through their outer class).
        /// </summary>
        private static IEnumerable<Type> FrameworkTypes_Internal()
        {
            return typeof(GameMode).Assembly.GetExportedTypes().Where(type => type.Namespace == FrameworkNamespace && !type.IsNested);
        }

        /// <summary>
        /// A GameMode whose GameState and PlayerState are in their own files.
        /// </summary>
        private const string GameModeOnlyBody = @"using UnityEngine;
using ModernWestern.UEArchitecture;

// The rules of a level. Put it on a prefab, fill in its default classes (PlayerController, Pawn, HUD) and pick it in a
// level's World Settings, or as the default GameMode in Project Settings > UE Architecture. Its GameState and PlayerState
// are in #BASE#GameState.cs and #BASE#PlayerState.cs.
public class #BASE#GameMode : GameMode<#BASE#GameState, #BASE#PlayerState>
{
    protected override void StartPlay()
    {
        // The players are spawned: the match starts.
    }

    protected override void OnPostLogin(PlayerController player)
    {
    }
}
";

        /// <summary>
        /// The template of a kind: #NAME# is the class name, #BASE# the name without its GameMode/Mode suffix, #TICK# the
        /// [Tick] attribute and #WHEN# when it ticks.
        /// </summary>
        public static string Body(CreateFrameworkScripts.Kind kind)
        {
            switch (kind)
            {
                case CreateFrameworkScripts.Kind.GameInstance:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// The game itself: alive from the first scene until the game quits. Put it on a prefab and pick the prefab in
// Project Settings > UE Architecture. Prefer subsystems for features (saves, audio...): GameInstance.Get<T>().
public class #NAME# : GameInstance
{
    protected override void Init()
    {
    }

    protected override void Shutdown()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.GameInstanceSubsystem:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Lives as long as the game and is created by itself. Get it from anywhere: GameInstance.Get<#NAME#>().
// Add ITickable to get Tick(deltaTime) every frame.
public sealed class #NAME# : GameInstanceSubsystem
{
    protected override void Initialize()
    {
    }

    protected override void Deinitialize()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.WorldSubsystem:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Lives as long as the current level and is created by itself. Get it from anywhere: World.Get<#NAME#>().
// Add ITickable to get Tick(deltaTime) every frame.
public sealed class #NAME# : WorldSubsystem
{
    protected override void Initialize()
    {
    }

    protected override void OnWorldBeginPlay()
    {
        // The GameMode spawned the players.
    }

    protected override void Deinitialize()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.TickableGameInstanceSubsystem:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Lives as long as the game, is created by itself and ticks #WHEN#, without a MonoBehaviour and without
// allocating. Get it from anywhere: GameInstance.Get<#NAME#>().
#TICK#
public sealed class #NAME# : GameInstanceSubsystem, ITickable
{
    protected override void Initialize()
    {
    }

    public void Tick(float deltaTime)
    {
    }

    protected override void Deinitialize()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.TickableWorldSubsystem:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Lives as long as the current level, is created by itself and ticks #WHEN#, without a MonoBehaviour and
// without allocating. Get it from anywhere: World.Get<#NAME#>().
#TICK#
public sealed class #NAME# : WorldSubsystem, ITickable
{
    protected override void Initialize()
    {
    }

    public void Tick(float deltaTime)
    {
    }

    protected override void Deinitialize()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.GameMode:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// The rules of a level. Put it on a prefab, fill in its default classes (PlayerController, Pawn, HUD) and pick it in a
// level's World Settings, or as the default GameMode in Project Settings > UE Architecture.
public class #BASE#GameMode : GameMode<#BASE#GameState, #BASE#PlayerState>
{
    protected override void StartPlay()
    {
        // The players are spawned: the match starts.
    }

    protected override void OnPostLogin(PlayerController player)
    {
    }
}

// The state of the match everyone can read (score to win, time left...). Created by the GameMode for each level.
public class #BASE#GameState : GameState
{
    protected override void OnBeginPlay()
    {
    }

    protected override void OnEndPlay()
    {
    }
}

// The state of one player (name, score, team...). Survives the Pawn dying.
public class #BASE#PlayerState : PlayerState
{
    public int Score { get; set; }
}
";

                case CreateFrameworkScripts.Kind.GameState:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// The state of the match everyone can read (score to win, time left...). Created by the GameMode for each level: pick it
// in your GameMode's class, GameMode<#NAME#, YourPlayerState>.
public class #NAME# : GameState
{
    protected override void OnBeginPlay()
    {
    }

    protected override void OnEndPlay()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.PlayerState:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// The state of one player (name, score, team...). Survives the Pawn dying. Pick it in your GameMode's class,
// GameMode<YourGameState, #NAME#>.
public class #NAME# : PlayerState
{
    public int Score { get; set; }
}
";

                case CreateFrameworkScripts.Kind.PlayerController:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// The player's will: reads input and drives the possessed Pawn. Put it on the GameMode's PlayerController prefab.
public class #NAME# : PlayerController
{
    private void Update()
    {
        if (Pawn == null)
        {
            return;
        }

        // Read input here and drive the Pawn.
    }

    protected override void OnPossess(Pawn pawn)
    {
    }
}
";

                case CreateFrameworkScripts.Kind.AIController:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// An AI: drives its Pawn by code. Put it on a prefab, pick the prefab in the Pawn's AI Controller field and set the Pawn's
// Auto Possess AI (placed in the level, spawned with World.SpawnPawn, or both).
public class #NAME# : AIController
{
    private void Update()
    {
        if (Pawn == null)
        {
            return;
        }

        // Decide here and drive the Pawn.
    }

    protected override void OnPossess(Pawn pawn)
    {
    }
}
";

                case CreateFrameworkScripts.Kind.Pawn:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Something a player or an AI controls. Put it on the GameMode's default Pawn prefab (players) or set its Auto Possess AI
// (enemies, NPCs). Data that must survive death belongs in the PlayerState.
public class #NAME# : Pawn
{
    protected override void OnPossessed(Controller controller)
    {
        // A PlayerController or an AIController.
    }

    protected override void OnUnPossessed()
    {
    }
}
";

                case CreateFrameworkScripts.Kind.HUD:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// One player's interface. Put it on the root of the GameMode's HUD prefab (your Canvas).
// With ObserverVM installed, Widgets inside it get the PlayerState, Pawn, GameState... by themselves.
public class #NAME# : HUD
{
    protected override void OnInit()
    {
        // Owner, PlayerState and Pawn are ready.
    }
}
";

                case CreateFrameworkScripts.Kind.PlayerStart:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// A Player Start with your own data (a team, an order...). Place it in the level like any Player Start; your GameMode picks
// one in FindPlayerStart, or by its tag with PlayerStart.Find(""Red Team"").
public class #NAME# : PlayerStart
{
    protected override void OnEnable()
    {
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }
}
";

                case CreateFrameworkScripts.Kind.LoadingScreen:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Shown while Levels.Open loads a level. Put it on a prefab with your loading UI and pick it in Project Settings > UE Architecture.
public class #NAME# : LoadingScreen
{
    protected override void OnProgress(float progress)
    {
        // From 0 to 1, e.g. fill a progress bar.
    }
}
";

                case CreateFrameworkScripts.Kind.SaveGame:
                    return @"using System;
using UnityEngine;
using ModernWestern.UEArchitecture;

// The data you save, like Unreal's USaveGame: public (or [SerializeField]) fields, the way Unity serializes them.
// Save it with SaveGames.Save(save, ""Slot 1""), load it with SaveGames.Load<#NAME#>(""Slot 1"").
[Serializable]
public sealed class #NAME# : SaveGame
{
    // Raise it when the fields change, and upgrade older saves in OnAfterLoad.
    public override int Version => 1;

    public int level;

    protected override void OnAfterLoad(int savedVersion)
    {
    }
}
";

                case CreateFrameworkScripts.Kind.Message:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// A message between systems that do not know each other, like Lyra's gameplay messages. A struct, so sending it allocates
// nothing. Send it with Messages.Broadcast(new #NAME#()), listen with Messages<#NAME#>.Listen(this, OnMessage).
public struct #NAME#
{
}
";

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}
