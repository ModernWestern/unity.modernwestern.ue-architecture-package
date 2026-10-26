using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// How bad a problem is.
    /// </summary>
    internal enum ProblemSeverity
    {
        /// <summary>
        /// Worth knowing; nothing breaks.
        /// </summary>
        Info,

        /// <summary>
        /// Something will not work as expected (no Player Start, buttons without an EventSystem...).
        /// </summary>
        Warning,

        /// <summary>
        /// Something is broken (a loading screen without a Canvas shows nothing, the settings asset is not found in builds...).
        /// </summary>
        Error
    }

    /// <summary>
    /// One thing to fix, in plain words, with at most one fix. The same problem shows on the object that has it (its
    /// inspector), in the Level overlay and in "-uea validate"; its fix is one public action.
    /// </summary>
    internal sealed class Problem
    {
        public Problem(ProblemSeverity severity, string message, Object context = null)
        {
            Severity = severity;

            Message = message;

            Context = context;
        }

        public ProblemSeverity Severity { get; }

        /// <summary>
        /// What is wrong and what to do, e.g. "No Player Start in this level: players spawn where the GameMode is."
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// The object that has the problem (selected from the overlay), or null for the level itself.
        /// </summary>
        public Object Context { get; }

        /// <summary>
        /// The fix's button, e.g. "Add Player Start". Null when there is no fix.
        /// </summary>
        public string FixLabel { get; private set; }

        /// <summary>
        /// A stable name of the fix, e.g. "add-player-start", for tests and the command line.
        /// </summary>
        public string FixId { get; private set; }

        /// <summary>
        /// True when "Fix all safe" and "-uea fix-all" may apply it without asking: it only adds what is missing or fills
        /// an empty field, and never opens a window.
        /// </summary>
        public bool SafeForFixAll { get; private set; }

        /// <summary>
        /// True when the fix opens a window instead of changing the project (never run headless).
        /// </summary>
        public bool OpensWindow { get; private set; }

        public Func<ActionResult> Fix { get; private set; }

        public bool HasFix => Fix != null;

        public Problem WithFix(string label, string id, bool safe, Func<ActionResult> fix)
        {
            FixLabel = label;

            FixId = id;

            SafeForFixAll = safe;

            Fix = fix;

            return this;
        }

        /// <summary>
        /// A fix that opens a window ("Create a GameMode…"): offered in the Editor only.
        /// </summary>
        public Problem WithWindow(string label, string id, Action open)
        {
            OpensWindow = true;

            return WithFix(label, id, false, () =>
            {
                open();

                return null;
            });
        }

        public override string ToString()
        {
            return Message;
        }
    }

    /// <summary>
    /// What one level looks like to the tools, computed once per change (never per repaint): its GameMode and where it
    /// comes from, its Player Starts, whether it is in Build Settings, and its problems with their fixes.
    /// </summary>
    internal sealed class LevelReport
    {
        public Scene Scene;

        public GameMode GameMode;

        public GameModeSource Source;

        public int Starts;

        public int Players;

        public bool InBuild;

        /// <summary>
        /// The Pawn placed in the level that Player 1 possesses (Auto Possess Player), or null.
        /// </summary>
        public Pawn PlacedPawn;

        /// <summary>
        /// True when <see cref="PlacedPawn"/> waits for Player 1 to join: the GameMode starts with no players (or there is
        /// no GameMode), so only AddPlayer makes Player 1.
        /// </summary>
        public bool PlacedPawnWaits;

        /// <summary>
        /// How many of the players at start possess a placed Pawn instead of spawning the Default Pawn at a Player Start.
        /// </summary>
        public int PlayersWithPlacedPawns;

        public readonly List<Problem> Problems = new();

        /// <summary>
        /// Warnings and errors (infos are listed but not counted).
        /// </summary>
        public int Count => Problems.Count(problem => problem.Severity != ProblemSeverity.Info);

        /// <summary>
        /// The most important problem with a fix: the one the overlay's accent button offers. Notes are never offered
        /// there: a level with notes only has nothing to do.
        /// </summary>
        public Problem Next => Problems.Where(problem => problem.HasFix && problem.Severity != ProblemSeverity.Info).OrderByDescending(problem => problem.Severity).FirstOrDefault();
    }

    /// <summary>
    /// The problems registry: providers per component type and per level, and caches cleared when the hierarchy, the
    /// project, an object's fields or the active level change. Inspectors, the Level overlay and the command line read it.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProblemProviders
    {
        private static readonly List<KeyValuePair<Type, Action<Component, List<Problem>>>> _components = new();

        private static readonly List<Action<Scene, LevelReport>> _levels = new();

        private static readonly Dictionary<int, List<Problem>> _componentCache = new();

        private static readonly Dictionary<Scene, LevelReport> _levelCache = new();

        static ProblemProviders()
        {
            EditorApplication.hierarchyChanged -= Invalidate;

            EditorApplication.hierarchyChanged += Invalidate;

            EditorApplication.projectChanged -= Invalidate;

            EditorApplication.projectChanged += Invalidate;

            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged_Internal;

            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged_Internal;

            Undo.undoRedoPerformed -= Invalidate;

            Undo.undoRedoPerformed += Invalidate;

            EditorBuildSettings.sceneListChanged -= Invalidate;

            EditorBuildSettings.sceneListChanged += Invalidate;

            // Property fields applied to assets (the Default GameMode in Project Settings, a prefab's fields) raise no
            // Hierarchy or Project event.
            ObjectChangeEvents.changesPublished -= OnChangesPublished_Internal;

            ObjectChangeEvents.changesPublished += OnChangesPublished_Internal;

            BuiltInProblems.Register_Internal();
        }

        /// <summary>
        /// Bumped whenever the caches are cleared.
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>
        /// Adds a provider for components of <paramref name="type"/> (and its subclasses).
        /// </summary>
        public static void Register(Type type, Action<Component, List<Problem>> provider)
        {
            if (type != null && provider != null)
            {
                _components.Add(new KeyValuePair<Type, Action<Component, List<Problem>>>(type, provider));

                Invalidate();
            }
        }

        /// <summary>
        /// Adds a provider for levels.
        /// </summary>
        public static void RegisterLevel(Action<Scene, LevelReport> provider)
        {
            if (provider != null)
            {
                _levels.Add(provider);

                Invalidate();
            }
        }

        public static void Invalidate()
        {
            _componentCache.Clear();

            _levelCache.Clear();

            Version++;
        }

        /// <summary>
        /// The problems of one component (cached until something changes).
        /// </summary>
        public static IReadOnlyList<Problem> For(Component component)
        {
            if (component == null)
            {
                return Array.Empty<Problem>();
            }

            var key = EditorCompat.SessionKey(component);

            if (_componentCache.TryGetValue(key, out var known))
            {
                return known;
            }

            var problems = new List<Problem>();

            foreach (var pair in _components)
            {
                if (!pair.Key.IsInstanceOfType(component))
                {
                    continue;
                }

                try
                {
                    pair.Value(component, problems);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _componentCache[key] = problems;

            return problems;
        }

        /// <summary>
        /// The report of a loaded level (cached until something changes): its own problems and those of the framework
        /// objects in it.
        /// </summary>
        public static LevelReport ForLevel(Scene scene)
        {
            if (_levelCache.TryGetValue(scene, out var known))
            {
                return known;
            }

            var report = new LevelReport { Scene = scene };

            if (scene.IsValid() && scene.isLoaded)
            {
                report.GameMode = GameModeInfo.Effective(scene, out report.Source);

                report.Starts = LevelSetup.Starts_Internal(scene).Count;

                report.Players = report.GameMode != null ? Mathf.Max(1, report.GameMode.PlayersAtStart) : 1;

                var placed = GameModeInfo.PlacedPlayerPawns(scene);

                report.PlacedPawn = placed.FirstOrDefault(pawn => pawn.AutoPossessPlayer == 0);

                // The real Players at Start (0 = nobody joins as the level begins), not the at-least-one count of the starts.
                report.PlacedPawnWaits = report.PlacedPawn != null && !GameModeInfo.JoinsAtStart(report.GameMode, 0);

                report.PlayersWithPlacedPawns = placed.Select(pawn => pawn.AutoPossessPlayer).Where(index => GameModeInfo.JoinsAtStart(report.GameMode, index)).Distinct().Count();

                report.InBuild = !string.IsNullOrEmpty(scene.path) && EditorBuildSettings.scenes.Any(item => item.enabled && item.path == scene.path);

                foreach (var provider in _levels)
                {
                    try
                    {
                        provider(scene, report);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (component != null && _components.Any(pair => pair.Key.IsInstanceOfType(component)))
                        {
                            report.Problems.AddRange(For(component));
                        }
                    }
                }

                // The GameMode prefab the level uses: its HUD without a Canvas shows in every level that uses it.
                if (report.GameMode != null && EditorUtility.IsPersistent(report.GameMode))
                {
                    report.Problems.AddRange(For(report.GameMode));
                }
            }

            _levelCache[scene] = report;

            return report;
        }

        private static void OnActiveSceneChanged_Internal(Scene previous, Scene next)
        {
            Invalidate();
        }

        private static void OnChangesPublished_Internal(ref ObjectChangeEventStream stream)
        {
            if (stream.length > 0)
            {
                Invalidate();
            }
        }
    }
}
