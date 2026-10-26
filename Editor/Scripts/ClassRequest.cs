using System;
using System.Linq;
using UnityEditor;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Where a new GameMode's GameState and PlayerState classes go.
    /// </summary>
    public enum StateFiles
    {
        /// <summary>
        /// As set in Project Settings > UE Architecture > Editor (Separate State Files).
        /// </summary>
        AsInSettings,

        /// <summary>
        /// In the GameMode's file.
        /// </summary>
        SameFile,

        /// <summary>
        /// Each in its own file (ArenaGameState.cs, ArenaPlayerState.cs).
        /// </summary>
        OwnFiles
    }

    /// <summary>
    /// A new framework class, like Unreal's "Pick Parent Class": what <see cref="UEArchitectureActions.CreateClass"/>
    /// writes. Plain fields, so the New Framework Class window keeps it across reloads and the command line fills it.
    /// </summary>
    [Serializable]
    public sealed class ClassRequest
    {
        /// <summary>
        /// The class name as typed ("arena pawn" gives ArenaPawn; a GameMode always ends in GameMode).
        /// </summary>
        public string className = string.Empty;

        /// <summary>
        /// The full name of the class it derives from ("ModernWestern.UEArchitecture.Pawn", or one of yours). Empty for
        /// a Message, which is a struct.
        /// </summary>
        public string parentClass = string.Empty;

        /// <summary>
        /// The folder of the script (empty: the scripts folder of Project Settings > UE Architecture > Editor).
        /// </summary>
        public string folder = string.Empty;

        /// <summary>
        /// A Message (a struct sent with Messages.Broadcast) instead of a class.
        /// </summary>
        public bool message;

        /// <summary>
        /// A subsystem that ticks (ITickable with [Tick]).
        /// </summary>
        public bool tickable;

        public TickGroup tickGroup = TickGroup.Update;

        public int tickOrder;

        public bool unscaled;

        /// <summary>
        /// For a GameMode: the full name of an existing GameState class to use, or empty for a new one named after it.
        /// </summary>
        public string gameStateClass = string.Empty;

        /// <summary>
        /// For a GameMode: the full name of an existing PlayerState class to use, or empty for a new one named after it.
        /// </summary>
        public string playerStateClass = string.Empty;

        /// <summary>
        /// For a GameMode: where its new GameState and PlayerState go.
        /// </summary>
        public StateFiles stateFiles = StateFiles.AsInSettings;

        /// <summary>
        /// For components: also build its prefab after the compile (named after the class, in the prefab folder of the
        /// editor settings).
        /// </summary>
        public bool withPrefab;

        /// <summary>
        /// Then put the prefab in this field ("Then use it as"): the object (an EditorCompat object key) and the field.
        /// </summary>
        public string assignToKey = string.Empty;

        public string assignToProperty = string.Empty;

        /// <summary>
        /// The class it derives from, or null (not compiled, or a Message).
        /// </summary>
        public Type Parent => message ? null : ClassRequestTypes.Find(parentClass);

        public ClassRequest Clone()
        {
            return (ClassRequest)MemberwiseClone();
        }
    }

    /// <summary>
    /// Finding the classes a request names: full names first, then the UE Architecture class with that name, then a
    /// framework-derived class of the project with that name (when only one has it).
    /// </summary>
    internal static class ClassRequestTypes
    {
        /// <summary>
        /// The bases a new framework class can derive from (the New Framework Class window lists their subclasses).
        /// </summary>
        public static readonly Type[] Bases =
        {
            typeof(GameMode), typeof(GameState), typeof(PlayerState), typeof(Controller), typeof(Pawn), typeof(HUD), typeof(GameInstance), typeof(GameInstanceSubsystem),
            typeof(WorldSubsystem), typeof(LoadingScreen), typeof(SaveGame), typeof(PlayerStart)
        };

        public static Type Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            name = name.Trim();

            var type = PendingSteps.FindClass(name) ?? PendingSteps.FindClass("ModernWestern.UEArchitecture." + name);

            if (type != null)
            {
                return type;
            }

            var matches = Bases.SelectMany(baseType => TypeCache.GetTypesDerivedFrom(baseType)).Where(candidate => candidate.Name == name).Distinct().ToArray();

            return matches.Length == 1 ? matches[0] : null;
        }

        /// <summary>
        /// True when a class can be a parent: a public, non-sealed, non-generic framework class or subclass, not from a
        /// test assembly.
        /// </summary>
        public static bool CanBeParent(Type type)
        {
            return type != null && type.IsClass && !type.IsSealed && !type.ContainsGenericParameters && !type.IsGenericType && (type.IsPublic || type.IsNestedPublic) &&
                   Bases.Any(baseType => baseType.IsAssignableFrom(type)) && !(type.Assembly.GetName().Name ?? string.Empty).Contains(".Tests");
        }

        /// <summary>
        /// Every class a new class can derive from: the framework bases and the project's classes that derive from them.
        /// </summary>
        public static Type[] Parents()
        {
            return Bases.Concat(Bases.SelectMany(baseType => TypeCache.GetTypesDerivedFrom(baseType)))
                        .Where(type => CanBeParent(type) && !type.IsAbstract)
                        .Distinct()
                        .OrderBy(type => ClassTemplates.IsFrameworkType(type) ? 0 : 1)
                        .ThenBy(type => type.Name, StringComparer.Ordinal)
                        .ToArray();
        }
    }
}
