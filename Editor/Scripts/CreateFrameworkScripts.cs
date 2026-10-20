using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.Compilation;
using UnityEditor.ProjectWindowCallback;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Assets > Create > UE Architecture: ready-to-fill scripts for every piece of the framework. They go into the root
    /// namespace of the folder's assembly (or the project's), if one is set.
    /// </summary>
    public static class CreateFrameworkScripts
    {
        public enum Kind
        {
            GameInstance,
            GameInstanceSubsystem,
            WorldSubsystem,
            GameMode,
            PlayerController,
            Pawn,
            HUD,
            LoadingScreen
        }

        private const string Menu = "Assets/Create/UE Architecture/";

        [MenuItem(Menu + "Game Instance", false, 80)]
        private static void CreateGameInstance() => Start_Internal(Kind.GameInstance, "MyGameInstance");

        [MenuItem(Menu + "Game Instance Subsystem", false, 81)]
        private static void CreateGameSubsystem() => Start_Internal(Kind.GameInstanceSubsystem, "NewSubsystem");

        [MenuItem(Menu + "World Subsystem", false, 82)]
        private static void CreateWorldSubsystem() => Start_Internal(Kind.WorldSubsystem, "NewWorldSubsystem");

        [MenuItem(Menu + "Game Mode (+ GameState, PlayerState)", false, 93)]
        private static void CreateGameMode() => Start_Internal(Kind.GameMode, "NewGameMode");

        [MenuItem(Menu + "Player Controller", false, 94)]
        private static void CreatePlayerController() => Start_Internal(Kind.PlayerController, "NewPlayerController");

        [MenuItem(Menu + "Pawn", false, 95)]
        private static void CreatePawn() => Start_Internal(Kind.Pawn, "NewPawn");

        [MenuItem(Menu + "HUD", false, 96)]
        private static void CreateHud() => Start_Internal(Kind.HUD, "NewHUD");

        [MenuItem(Menu + "Loading Screen", false, 107)]
        private static void CreateLoadingScreen() => Start_Internal(Kind.LoadingScreen, "NewLoadingScreen");

        /// <summary>
        /// The source of a script, e.g. Source(Kind.GameMode, "Arena", "MyGame") declares ArenaGameMode, ArenaGameState and
        /// ArenaPlayerState inside namespace MyGame.
        /// </summary>
        public static string Source(Kind kind, string typedName, string rootNamespace)
        {
            var name = ClassName(typedName);

            var body = Template_Internal(kind).Replace("#NAME#", name).Replace("#BASE#", BaseName_Internal(name, "GameMode", "Mode"));

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
        /// A valid C# class name from what was typed: "arena game mode.cs" → "ArenaGameMode". GameMode scripts end in "GameMode".
        /// </summary>
        public static string ClassName(string typed)
        {
            var name = new StringBuilder();

            var upperNext = true;

            foreach (var character in Path.GetFileNameWithoutExtension(typed) ?? string.Empty)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    upperNext = true;

                    continue;
                }

                if (name.Length == 0 && char.IsDigit(character))
                {
                    name.Append('_');
                }

                name.Append(upperNext ? char.ToUpperInvariant(character) : character);

                upperNext = false;
            }

            return name.Length == 0 ? "New" : name.ToString();
        }

        /// <summary>
        /// The file name a kind of script gets for what was typed (a GameMode always ends in "GameMode").
        /// </summary>
        public static string FileName(Kind kind, string typed)
        {
            var name = ClassName(typed);

            return kind == Kind.GameMode ? BaseName_Internal(name, "GameMode", "Mode") + "GameMode" : name;
        }

        private static string BaseName_Internal(string name, params string[] suffixes)
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

        private static void Start_Internal(Kind kind, string defaultName)
        {
            var action = ScriptableObject.CreateInstance<CreateAction>();

            action.Kind = kind;

            var icon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, action, defaultName + ".cs", icon, null);
        }

        private sealed class CreateAction : EndNameEditAction
        {
            public Kind Kind;

            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var folder = Path.GetDirectoryName(pathName)?.Replace('\\', '/') ?? "Assets";

                var className = FileName(Kind, pathName);

                var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{className}.cs");

                var rootNamespace = CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(path);

                File.WriteAllText(path, Source(Kind, Kind == Kind.GameMode ? className : Path.GetFileNameWithoutExtension(path), rootNamespace));

                AssetDatabase.ImportAsset(path);

                ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(path));
            }
        }

        private static string Template_Internal(Kind kind)
        {
            switch (kind)
            {
                case Kind.GameInstance:
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

                case Kind.GameInstanceSubsystem:
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

                case Kind.WorldSubsystem:
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

                case Kind.GameMode:
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

                case Kind.PlayerController:
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

                case Kind.Pawn:
                    return @"using UnityEngine;
using ModernWestern.UEArchitecture;

// Something a player controls. Put it on the GameMode's default Pawn prefab. Data that must survive death belongs in the PlayerState.
public class #NAME# : Pawn
{
    protected override void OnPossessed(PlayerController controller)
    {
    }

    protected override void OnUnPossessed()
    {
    }
}
";

                case Kind.HUD:
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

                case Kind.LoadingScreen:
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

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}
