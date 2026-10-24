using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Assets > Create > UE Architecture: ready-to-fill scripts for every piece of the framework. They go into the root
    /// namespace of the folder's assembly (or the project's), if one is set. Written through ScriptWriter, so a name that
    /// is taken (a file or a class) is reported instead of making "ArenaPawn 1.cs".
    /// </summary>
    public static class CreateFrameworkScripts
    {
        /// <summary>
        /// The script templates. New kinds are added at the end, so saved values keep their meaning.
        /// </summary>
        public enum Kind
        {
            GameInstance,
            GameInstanceSubsystem,
            WorldSubsystem,
            GameMode,
            PlayerController,
            AIController,
            Pawn,
            HUD,
            LoadingScreen,
            GameState,
            PlayerState,
            PlayerStart,
            SaveGame,
            Message,
            TickableGameInstanceSubsystem,
            TickableWorldSubsystem
        }

        private const string Menu = "Assets/Create/UE Architecture/";

        [MenuItem(Menu + "Game Instance", false, 80)]
        private static void CreateGameInstance() => Start_Internal(Kind.GameInstance, "MyGameInstance");

        [MenuItem(Menu + "Game Instance Subsystem", false, 81)]
        private static void CreateGameSubsystem() => Start_Internal(Kind.GameInstanceSubsystem, "NewSubsystem");

        [MenuItem(Menu + "Game Instance Subsystem (Tickable)", false, 82)]
        private static void CreateTickableGameSubsystem() => Start_Internal(Kind.TickableGameInstanceSubsystem, "NewTickableSubsystem");

        [MenuItem(Menu + "World Subsystem", false, 83)]
        private static void CreateWorldSubsystem() => Start_Internal(Kind.WorldSubsystem, "NewWorldSubsystem");

        [MenuItem(Menu + "World Subsystem (Tickable)", false, 84)]
        private static void CreateTickableWorldSubsystem() => Start_Internal(Kind.TickableWorldSubsystem, "NewTickableWorldSubsystem");

        [MenuItem(Menu + "Game Mode (+ GameState, PlayerState)", false, 95)]
        private static void CreateGameMode() => Start_Internal(Kind.GameMode, "NewGameMode");

        [MenuItem(Menu + "Game State", false, 96)]
        private static void CreateGameState() => Start_Internal(Kind.GameState, "NewGameState");

        [MenuItem(Menu + "Player State", false, 97)]
        private static void CreatePlayerState() => Start_Internal(Kind.PlayerState, "NewPlayerState");

        [MenuItem(Menu + "Player Controller", false, 98)]
        private static void CreatePlayerController() => Start_Internal(Kind.PlayerController, "NewPlayerController");

        [MenuItem(Menu + "AI Controller", false, 99)]
        private static void CreateAIController() => Start_Internal(Kind.AIController, "NewAIController");

        [MenuItem(Menu + "Pawn", false, 100)]
        private static void CreatePawn() => Start_Internal(Kind.Pawn, "NewPawn");

        [MenuItem(Menu + "HUD", false, 101)]
        private static void CreateHud() => Start_Internal(Kind.HUD, "NewHUD");

        [MenuItem(Menu + "Player Start", false, 102)]
        private static void CreatePlayerStart() => Start_Internal(Kind.PlayerStart, "NewPlayerStart");

        [MenuItem(Menu + "Loading Screen", false, 113)]
        private static void CreateLoadingScreen() => Start_Internal(Kind.LoadingScreen, "NewLoadingScreen");

        [MenuItem(Menu + "Save Game", false, 124)]
        private static void CreateSaveGame() => Start_Internal(Kind.SaveGame, "NewSaveGame");

        [MenuItem(Menu + "Message", false, 125)]
        private static void CreateMessage() => Start_Internal(Kind.Message, "NewMessage");

        /// <summary>
        /// The source of a script, e.g. Source(Kind.GameMode, "Arena", "MyGame") declares ArenaGameMode, ArenaGameState and
        /// ArenaPlayerState inside namespace MyGame.
        /// </summary>
        public static string Source(Kind kind, string typedName, string rootNamespace)
        {
            return ClassTemplates.Source(kind, typedName, rootNamespace);
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

            return kind == Kind.GameMode ? ClassTemplates.BaseName(name, "GameMode", "Mode") + "GameMode" : name;
        }

        private static void Start_Internal(Kind kind, string defaultName)
        {
            var action = ScriptableObject.CreateInstance<CreateAction>();

            action.Kind = kind;

            var icon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;

            EditorCompat.StartNameEditing(action, defaultName + ".cs", icon);
        }

        /// <summary>
        /// Writes the script(s) of a kind for the path typed in the Project window, through ScriptWriter: a file or class
        /// that already exists is reported (in a dialog, or the Console in batch mode) and nothing is written. The files
        /// are what Assets > UE Architecture > Remove Files Made by the Last Action removes next.
        /// </summary>
        internal static ActionResult Create_Internal(Kind kind, string pathName)
        {
            var plan = ScriptWriter.Plan(new[] { Request_Internal(kind, pathName) });

            var result = ScriptWriter.Write(plan, null, "create-script");

            if (!result.Ok)
            {
                var problems = string.Join("\n", result.Problems);

                Debug.LogWarning("UE Architecture: " + problems);

                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("UE Architecture", problems, "OK");
                }

                return result;
            }

            LastCreatedFiles.Remember(result);

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(plan.Scripts[0].Path);

            if (script != null && !Application.isBatchMode)
            {
                ProjectWindowUtil.ShowCreatedAsset(script);
            }

            return result;
        }

        /// <summary>
        /// The request for a path typed in the Project window: in the folder picked there, which may be inside an embedded
        /// package (Packages/com.studio.game/...), as this menu always allowed.
        /// </summary>
        internal static ScriptRequest Request_Internal(Kind kind, string pathName)
        {
            var folder = GeneratorNaming.FolderOf((pathName ?? string.Empty).Replace('\\', '/'));

            return new ScriptRequest(Path.GetFileNameWithoutExtension(pathName), ClassTemplates.ParentOf(kind), folder.Length > 0 ? folder : "Assets")
            {
                Kind = kind,
                AllowPackageFolder = true
            };
        }

        // The Unity 6.4 end-action change lives in EditorCompat.
        private sealed class CreateAction : NameOnCreateAction
        {
            public Kind Kind;

            protected override void OnNamed(string pathName, string resourceFile)
            {
                Create_Internal(Kind, pathName);
            }
        }
    }
}
