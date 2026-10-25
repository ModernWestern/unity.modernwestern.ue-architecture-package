using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ModernWestern.UI.ObserverVM;
using ModernWestern.UI.ObserverVM.EditorTools;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The framework object a widget gets, in the order <see cref="FrameworkViewModels.Resolve"/> looks: the player's
    /// PlayerState, Pawn, PlayerController and HUD, the GameState and GameMode, a level subsystem, a game subsystem, the
    /// GameInstance.
    /// </summary>
    public enum FrameworkSlot
    {
        None,
        PlayerState,
        Pawn,
        PlayerController,
        HUD,
        GameState,
        GameMode,
        WorldSubsystem,
        GameInstanceSubsystem,
        GameInstance
    }

    /// <summary>
    /// The framework classes a level (or a HUD, or a GameMode) will have at runtime, read from the GameMode prefab, its
    /// default classes, the project settings and the subsystem classes: what <see cref="BindingPreview.Predict"/> needs.
    /// Plain data, so tests build it by hand.
    /// </summary>
    public sealed class FrameworkTypeSet
    {
        private static readonly IReadOnlyList<Type> NoTypes = Array.Empty<Type>();

        /// <summary>
        /// False when the widget's player does not exist (no GameMode, or a player index past the players at start): the
        /// PlayerState, Pawn, PlayerController and HUD are then skipped, like Resolve does with no player.
        /// </summary>
        public bool HasPlayer { get; set; } = true;

        public Type PlayerState { get; set; }

        public Type Pawn { get; set; }

        public Type PlayerController { get; set; }

        public Type HUD { get; set; }

        public Type GameState { get; set; }

        public Type GameMode { get; set; }

        public Type GameInstance { get; set; }

        public IReadOnlyList<Type> WorldSubsystems { get; set; } = NoTypes;

        public IReadOnlyList<Type> GameInstanceSubsystems { get; set; } = NoTypes;

        /// <summary>
        /// The GameMode the set comes from ("Arena Mode"), or empty.
        /// </summary>
        public string GameModeName { get; set; } = string.Empty;

        /// <summary>
        /// Where the GameMode comes from ("project default", "World Settings override", "placed in the scene"), or empty.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// How to name the Pawn when it is not the GameMode's Default Pawn: "PlayerCharacter (Pawn of Gameplay Mode, placed
        /// in this scene)". Empty: named after its class.
        /// </summary>
        public string PawnLabel { get; set; } = string.Empty;

        /// <summary>
        /// The GameMode prefab (or placed GameMode) the set was read from, or null.
        /// </summary>
        public GameMode GameModeObject { get; set; }

        /// <summary>
        /// True when no GameMode was found and the framework's base classes stand in (a HUD no GameMode uses yet).
        /// </summary>
        public bool Assumed { get; set; }

        /// <summary>
        /// The set of a GameMode (a prefab, or one placed in a level): its GameState and PlayerState classes, its default
        /// classes (a plain PlayerController when the slot is empty), the project's GameInstance and every subsystem class.
        /// </summary>
        public static FrameworkTypeSet ForGameMode(GameMode gameMode, string source = null)
        {
            var set = Subsystems_Internal(new FrameworkTypeSet());

            if (gameMode == null)
            {
                set.HasPlayer = false;

                return set;
            }

            var type = gameMode.GetType();

            set.GameModeObject = gameMode;

            set.GameMode = type;

            set.GameModeName = gameMode.gameObject.name;

            set.Source = source ?? string.Empty;

            set.GameState = GameModeInfo.GameStateType(type);

            set.PlayerState = GameModeInfo.PlayerStateType(type);

            set.PlayerController = gameMode.PlayerControllerPrefab != null ? gameMode.PlayerControllerPrefab.GetType() : typeof(PlayerController);

            set.Pawn = gameMode.DefaultPawnPrefab != null ? gameMode.DefaultPawnPrefab.GetType() : null;

            set.HUD = gameMode.HUDPrefab != null ? gameMode.HUDPrefab.GetType() : null;

            set.HasPlayer = true;

            return set;
        }

        /// <summary>
        /// The set of a loaded level: its GameMode as the runtime picks it (placed, World Settings, project default). A
        /// GameMode placed in the level that points to a Pawn placed in it (possessed by its code) gives that Pawn.
        /// </summary>
        public static FrameworkTypeSet ForScene(Scene scene)
        {
            var source = GameModeSource.None;

            var gameMode = scene.IsValid() && scene.isLoaded ? GameModeInfo.Effective(scene, out source) : null;

            var set = ForGameMode(gameMode, gameMode != null ? GameModeInfo.Describe(source) : null);

            if (gameMode != null && set.Pawn == null && !EditorUtility.IsPersistent(gameMode) && GameModeInfo.TryPlacedPawn(new SerializedObject(gameMode), out _, out var placed))
            {
                set.Pawn = placed.GetType();

                set.PawnLabel = $"{placed.gameObject.name} (Pawn of {gameMode.gameObject.name}, placed in this scene)";
            }

            return set;
        }

        /// <summary>
        /// The framework's base classes, for a HUD that no GameMode uses yet.
        /// </summary>
        public static FrameworkTypeSet Assume(Type hudType)
        {
            var set = Subsystems_Internal(new FrameworkTypeSet());

            set.Assumed = true;

            set.PlayerState = typeof(PlayerState);

            set.PlayerController = typeof(PlayerController);

            set.HUD = hudType;

            set.GameState = typeof(GameState);

            set.GameMode = typeof(GameMode);

            return set;
        }

        /// <summary>
        /// A copy with another HUD class (the HUD being looked at).
        /// </summary>
        public FrameworkTypeSet WithHud(Type hudType)
        {
            var copy = (FrameworkTypeSet)MemberwiseClone();

            copy.HUD = hudType;

            return copy;
        }

        /// <summary>
        /// A copy without a player (a Framework Binder whose player does not exist at start).
        /// </summary>
        public FrameworkTypeSet WithoutPlayer()
        {
            var copy = (FrameworkTypeSet)MemberwiseClone();

            copy.HasPlayer = false;

            return copy;
        }

        /// <summary>
        /// A copy without a Pawn (a HUD binds its widgets before the GameMode spawns the player's Pawn).
        /// </summary>
        public FrameworkTypeSet WithoutPawn()
        {
            var copy = (FrameworkTypeSet)MemberwiseClone();

            copy.Pawn = null;

            copy.PawnLabel = string.Empty;

            return copy;
        }

        private static FrameworkTypeSet Subsystems_Internal(FrameworkTypeSet set)
        {
            set.WorldSubsystems = Concrete_Internal(typeof(WorldSubsystem));

            set.GameInstanceSubsystems = Concrete_Internal(typeof(GameInstanceSubsystem));

            var settings = FrameworkIndex.Settings;

            set.GameInstance = settings != null && settings.GameInstancePrefab != null ? settings.GameInstancePrefab.GetType() : typeof(GameInstance);

            return set;
        }

        private static IReadOnlyList<Type> Concrete_Internal(Type baseType)
        {
            return TypeCache.GetTypesDerivedFrom(baseType).Where(type => !type.IsAbstract && !type.ContainsGenericParameters).ToList();
        }
    }

    /// <summary>
    /// What a widget will probably get: the slot and the class of the object.
    /// </summary>
    public readonly struct FrameworkPrediction
    {
        public FrameworkPrediction(FrameworkSlot slot, Type type)
        {
            Slot = slot;

            Type = type;
        }

        public FrameworkSlot Slot { get; }

        /// <summary>
        /// The class of the object the widget gets, or null.
        /// </summary>
        public Type Type { get; }

        public bool Matches => Slot != FrameworkSlot.None;

        /// <summary>
        /// True for subsystems: they exist only when their ShouldCreate says so, which the tools cannot know.
        /// </summary>
        public bool DependsOnShouldCreate => Slot == FrameworkSlot.WorldSubsystem || Slot == FrameworkSlot.GameInstanceSubsystem;

        public override string ToString() => Matches ? $"{Slot} ({Type?.Name})" : "nothing";
    }

    /// <summary>
    /// Who gives a widget its ViewModel, as far as the editor can tell.
    /// </summary>
    public enum BindingSource
    {
        /// <summary>
        /// Not a framework widget, or a parent widget gives it its ViewModel: ObserverVM's own answer applies.
        /// </summary>
        None,

        /// <summary>
        /// Its HUD binds it when the HUD starts.
        /// </summary>
        HUD,

        /// <summary>
        /// The Framework Binder on its object binds it.
        /// </summary>
        FrameworkBinder,

        /// <summary>
        /// Nothing: an object of a level outside any HUD and without a Framework Binder.
        /// </summary>
        Nothing,

        /// <summary>
        /// A prefab outside any HUD: it depends on where it is placed.
        /// </summary>
        DependsOnPlacement
    }

    /// <summary>
    /// The editor's answer to "what will this widget bind to?": who binds it, the framework classes it is checked against,
    /// the prediction, and the sentence the inspector shows ("Arena HUD: will probably bind to the Player State
    /// (ArenaPlayerState)").
    /// </summary>
    public sealed class WidgetBinding
    {
        public Widget Widget { get; internal set; }

        public BindingSource Source { get; internal set; }

        /// <summary>
        /// The HUD or Framework Binder that binds the widget, or null.
        /// </summary>
        public Object Binder { get; internal set; }

        public FrameworkTypeSet Set { get; internal set; }

        public FrameworkPrediction Prediction { get; internal set; }

        /// <summary>
        /// The widget expects something the framework can give (a PlayerState, a Pawn, the GameState, a subsystem...).
        /// </summary>
        public bool IsFrameworkWidget { get; internal set; }

        /// <summary>
        /// The widget shows a Pawn: with no Pawn it waits for one instead of failing.
        /// </summary>
        public bool DependsOnPawn { get; internal set; }

        /// <summary>
        /// The widget has a Framework Binder and is inside a HUD a GameMode spawns: the binder binds its player before the
        /// HUD can, so every player's copy of the HUD shows that player.
        /// </summary>
        public bool BinderInsideHud { get; internal set; }

        /// <summary>
        /// The widget's HUD is placed in a level, not spawned by a GameMode: nothing initialises it, so it binds nothing.
        /// </summary>
        public bool HudNotSpawned { get; internal set; }

        public string Text { get; internal set; } = string.Empty;

        public string Tooltip { get; internal set; } = string.Empty;

        /// <summary>
        /// 0 bound, 1 hint, 2 warning, 3 error (the order of ObserverVM's BoundByTone).
        /// </summary>
        public int Severity { get; internal set; }

        /// <summary>
        /// True when nothing will give the widget its ViewModel and a Framework Binder would.
        /// </summary>
        public bool NeedsBinder => Source == BindingSource.Nothing;

        public override string ToString() => Text;
    }

    /// <summary>
    /// "What will this widget bind to?" without entering Play mode. <see cref="Predict"/> is pure and follows
    /// <see cref="FrameworkViewModels.Resolve"/> step by step (a test keeps them equal); <see cref="Of"/> finds who binds a
    /// widget (its HUD, a Framework Binder, a parent widget, nothing) and the framework classes of its level or HUD. Static
    /// reading cannot see ShouldCreate, SetViewModel from code or Bind.Child, so the sentences say "will probably bind".
    /// </summary>
    public static class BindingPreview
    {
        /// <summary>
        /// The order Resolve looks in.
        /// </summary>
        public static readonly IReadOnlyList<FrameworkSlot> Order = new[]
        {
            FrameworkSlot.PlayerState, FrameworkSlot.Pawn, FrameworkSlot.PlayerController, FrameworkSlot.HUD, FrameworkSlot.GameState, FrameworkSlot.GameMode,
            FrameworkSlot.WorldSubsystem, FrameworkSlot.GameInstanceSubsystem, FrameworkSlot.GameInstance
        };

        private static readonly Type[] FrameworkBases =
        {
            typeof(PlayerState), typeof(Pawn), typeof(PlayerController), typeof(HUD), typeof(GameState), typeof(GameMode), typeof(WorldSubsystem), typeof(GameInstanceSubsystem),
            typeof(GameInstance)
        };

        private static readonly Dictionary<string, FrameworkTypeSet> _sets = new(StringComparer.Ordinal);

        private static readonly Dictionary<(HUD Hud, GameMode Preview), FrameworkTypeSet> _hudSets = new();

        private static readonly Dictionary<(Widget Widget, GameMode Preview), WidgetBinding> _bindings = new();

        private static int _setsVersion = -1;

        /// <summary>
        /// The framework object a widget expecting <paramref name="viewModelType"/> gets with these classes, in Resolve's
        /// order. Pure.
        /// </summary>
        public static FrameworkPrediction Predict(Type viewModelType, FrameworkTypeSet set)
        {
            if (viewModelType == null || set == null)
            {
                return default;
            }

            if (set.HasPlayer)
            {
                if (Fits_Internal(viewModelType, set.PlayerState))
                {
                    return new FrameworkPrediction(FrameworkSlot.PlayerState, set.PlayerState);
                }

                if (Fits_Internal(viewModelType, set.Pawn))
                {
                    return new FrameworkPrediction(FrameworkSlot.Pawn, set.Pawn);
                }

                if (Fits_Internal(viewModelType, set.PlayerController))
                {
                    return new FrameworkPrediction(FrameworkSlot.PlayerController, set.PlayerController);
                }

                if (Fits_Internal(viewModelType, set.HUD))
                {
                    return new FrameworkPrediction(FrameworkSlot.HUD, set.HUD);
                }
            }

            if (Fits_Internal(viewModelType, set.GameState))
            {
                return new FrameworkPrediction(FrameworkSlot.GameState, set.GameState);
            }

            if (Fits_Internal(viewModelType, set.GameMode))
            {
                return new FrameworkPrediction(FrameworkSlot.GameMode, set.GameMode);
            }

            // Subsystems are looked up by their exact class: a base class finds nothing, and nothing after it is tried.
            if (typeof(WorldSubsystem).IsAssignableFrom(viewModelType))
            {
                return set.WorldSubsystems.Contains(viewModelType) ? new FrameworkPrediction(FrameworkSlot.WorldSubsystem, viewModelType) : default;
            }

            if (typeof(GameInstanceSubsystem).IsAssignableFrom(viewModelType))
            {
                return set.GameInstanceSubsystems.Contains(viewModelType) ? new FrameworkPrediction(FrameworkSlot.GameInstanceSubsystem, viewModelType) : default;
            }

            return Fits_Internal(viewModelType, set.GameInstance) ? new FrameworkPrediction(FrameworkSlot.GameInstance, set.GameInstance) : default;
        }

        /// <summary>
        /// <see cref="Predict"/> for a widget inside a HUD. GameMode.AddPlayer binds the HUD's widgets before it spawns the
        /// player's Pawn, and only widgets of a Pawn class are bound again when the Pawn arrives: a ViewModel the Pawn fits
        /// that is not a Pawn class (an interface, Component) gets what comes after the Pawn. Pure.
        /// </summary>
        public static FrameworkPrediction PredictForHud(Type viewModelType, FrameworkTypeSet set)
        {
            var prediction = Predict(viewModelType, set);

            if (prediction.Slot != FrameworkSlot.Pawn || typeof(Pawn).IsAssignableFrom(viewModelType))
            {
                return prediction;
            }

            return Predict(viewModelType, set.WithoutPawn());
        }

        /// <summary>
        /// True when the framework could give a widget of this ViewModel type something: a framework class, a base class of
        /// one (other than object and Unity's own classes) or an interface one implements.
        /// </summary>
        public static bool IsFrameworkViewModel(Type viewModelType)
        {
            if (viewModelType == null || viewModelType == typeof(object) || viewModelType.Assembly == typeof(Object).Assembly)
            {
                return false;
            }

            foreach (var frameworkBase in FrameworkBases)
            {
                if (frameworkBase.IsAssignableFrom(viewModelType) || viewModelType.IsAssignableFrom(frameworkBase))
                {
                    return true;
                }
            }

            return viewModelType.IsInterface && FrameworkBases.Any(frameworkBase => TypeCache.GetTypesDerivedFrom(frameworkBase).Any(viewModelType.IsAssignableFrom));
        }

        /// <summary>
        /// "Player State", "Default Pawn", "level subsystem"... for sentences.
        /// </summary>
        public static string SlotLabel(FrameworkSlot slot)
        {
            switch (slot)
            {
                case FrameworkSlot.PlayerState:
                    return "Player State";

                case FrameworkSlot.Pawn:
                    return "Pawn";

                case FrameworkSlot.PlayerController:
                    return "Player Controller";

                case FrameworkSlot.HUD:
                    return "HUD";

                case FrameworkSlot.GameState:
                    return "Game State";

                case FrameworkSlot.GameMode:
                    return "Game Mode";

                case FrameworkSlot.WorldSubsystem:
                    return "level subsystem";

                case FrameworkSlot.GameInstanceSubsystem:
                    return "game subsystem";

                case FrameworkSlot.GameInstance:
                    return "Game Instance";

                default:
                    return "nothing";
            }
        }

        /// <summary>
        /// "the Player State (ArenaPlayerState)", "PlayerCharacter (Pawn of Gameplay Mode, placed in this scene)", "the
        /// level subsystem BestScore (if it is created)".
        /// </summary>
        public static string Describe(FrameworkPrediction prediction, FrameworkTypeSet set)
        {
            if (!prediction.Matches)
            {
                return "nothing";
            }

            var name = prediction.Type != null ? prediction.Type.Name : "?";

            switch (prediction.Slot)
            {
                case FrameworkSlot.Pawn when set != null && !string.IsNullOrEmpty(set.PawnLabel):
                    return set.PawnLabel;

                case FrameworkSlot.Pawn when set != null && !string.IsNullOrEmpty(set.GameModeName):
                    return $"the Pawn ({name}, Default Pawn of {set.GameModeName})";

                case FrameworkSlot.WorldSubsystem:
                case FrameworkSlot.GameInstanceSubsystem:
                    return $"the {SlotLabel(prediction.Slot)} {name} (if it is created)";

                default:
                    return $"the {SlotLabel(prediction.Slot)} ({name})";
            }
        }

        /// <summary>
        /// Who binds <paramref name="widget"/> and what it will probably get. Answers for a HUD come from the GameModes
        /// that use its prefab (<paramref name="preview"/> picks one when several do), answers for a level from its
        /// GameMode. Cached until the hierarchy or the project changes (inspectors ask on every repaint).
        /// </summary>
        public static WidgetBinding Of(Widget widget, GameMode preview = null)
        {
            if (widget == null)
            {
                return Compute_Internal(null, preview);
            }

            Refresh_Internal();

            var key = (widget, preview);

            if (!_bindings.TryGetValue(key, out var binding))
            {
                binding = Compute_Internal(widget, preview);

                _bindings[key] = binding;
            }

            return binding;
        }

        private static WidgetBinding Compute_Internal(Widget widget, GameMode preview)
        {
            var binding = new WidgetBinding { Widget = widget };

            if (widget == null)
            {
                binding.Text = "Nothing to look at.";

                return binding;
            }

            var viewModelType = widget.ViewModelType;

            binding.IsFrameworkWidget = IsFrameworkViewModel(viewModelType);

            binding.DependsOnPawn = viewModelType != null && typeof(Pawn).IsAssignableFrom(viewModelType);

            // A parent widget that holds this one in a [BindWidget] field gives it its ViewModel (Bind.Child, Bind.List): the
            // HUD and Framework Binders leave a bound widget alone.
            if (BoundByParent(widget))
            {
                binding.Source = BindingSource.None;

                return binding;
            }

            var hud = HudOf(widget);

            // The binder goes first: GameMode.AddPlayer spawns the HUD after the level has begun, so the binder's OnEnable
            // binds its player before the HUD starts, and the HUD leaves a bound widget alone.
            var binder = widget.GetComponent<FrameworkBinder>();

            if (binder != null)
            {
                ExplainBinder_Internal(binding, binder, hud);

                return binding;
            }

            if (hud != null)
            {
                binding.Source = BindingSource.HUD;

                binding.Binder = hud;

                if (IsPlacedInLevel(hud))
                {
                    ExplainPlacedHud_Internal(binding, hud);

                    return binding;
                }

                binding.Set = SetForHud(hud, preview);

                Explain_Internal(binding, HudName_Internal(hud));

                return binding;
            }

            if (!binding.IsFrameworkWidget)
            {
                binding.Source = BindingSource.None;

                return binding;
            }

            if (EditorUtility.IsPersistent(widget) || PrefabStageUtility.GetPrefabStage(widget.gameObject) != null)
            {
                binding.Source = BindingSource.DependsOnPlacement;

                binding.Severity = 1;

                binding.Text = "Depends on where it is placed: inside a HUD the HUD binds it, elsewhere it needs a Framework Binder";

                binding.Tooltip = "This prefab has no HUD above it and no Framework Binder. Where a level places it decides what binds it.";

                return binding;
            }

            binding.Source = BindingSource.Nothing;

            binding.Severity = 3;

            binding.Set = SetForLevel_Internal(widget.gameObject, out _);

            binding.Text = binding.Set.GameModeObject == null
                               ? "Nothing will bind this widget: it is outside a HUD, has no Framework Binder, and this level has no GameMode"
                               : "Nothing will bind this widget: it is outside a HUD and has no Framework Binder";

            var would = Predict(viewModelType, binding.Set);

            binding.Prediction = would;

            binding.Tooltip = would.Matches
                                  ? $"With a Framework Binder it will probably get {Describe(would, binding.Set)}."
                                  : "Add a Framework Binder, or move it under the HUD.";

            return binding;
        }

        /// <summary>
        /// True for a HUD placed in a level (in Edit mode, or in Play mode when no GameMode spawned it for a player): only
        /// the HUD a GameMode spawns for a player is started, and only that one binds its widgets. False for a HUD prefab,
        /// one open in Prefab Mode, and one a GameMode spawned.
        /// </summary>
        public static bool IsPlacedInLevel(HUD hud)
        {
            if (hud == null || EditorUtility.IsPersistent(hud))
            {
                return false;
            }

            var scene = hud.gameObject.scene;

            if (!scene.IsValid() || EditorSceneManager.IsPreviewScene(scene))
            {
                return false;
            }

            return !(Application.isPlaying && hud.Owner != null);
        }

        private static void ExplainBinder_Internal(WidgetBinding binding, FrameworkBinder binder, HUD hud)
        {
            binding.Source = BindingSource.FrameworkBinder;

            binding.Binder = binder;

            var index = PlayerIndex(binder);

            var set = SetForLevel_Internal(binder.gameObject, out var dependsOnLevel);

            var players = set.GameModeObject != null ? set.GameModeObject.PlayersAtStart : 0;

            if (set.GameModeObject == null || index >= players)
            {
                set = set.WithoutPlayer();
            }

            binding.Set = set;

            Explain_Internal(binding, $"Framework Binder (Player {index + 1})");

            if (hud != null && !IsPlacedInLevel(hud))
            {
                var hudName = HudName_Internal(hud);

                binding.BinderInsideHud = true;

                binding.Severity = Math.Max(binding.Severity, 2);

                binding.Text = $"Framework Binder (Player {index + 1}) inside {hudName}: it binds Player {index + 1} before the HUD can; remove it";

                binding.Tooltip = $"A GameMode spawns {hudName} for every player after the level has begun, and the Framework Binder binds the widget as the HUD appears, before the HUD binds its widgets: " +
                                  $"every player's {hudName} shows Player {index + 1}. Remove the Framework Binder and the HUD binds the widget to its own player.";

                return;
            }

            if (set.GameModeObject != null && index >= players)
            {
                binding.Tooltip += $" {set.GameModeName} starts {players} player(s), and the binder only sees players that exist when the level begins: Player {index + 1} is not one of them.";
            }

            if (dependsOnLevel)
            {
                binding.Tooltip += " This is a prefab: the answer is for the project's default GameMode; each level that places it may give it something else.";
            }

            binding.Tooltip = binding.Tooltip.Trim();
        }

        private static void ExplainPlacedHud_Internal(WidgetBinding binding, HUD hud)
        {
            binding.HudNotSpawned = true;

            binding.Set = SetForLevel_Internal(hud.gameObject, out _);

            binding.Severity = binding.IsFrameworkWidget ? 3 : 1;

            binding.Text = $"{hud.gameObject.name.Trim()} is placed in the level, not spawned by a GameMode: nothing binds it";

            binding.Tooltip = "Only the HUD a GameMode spawns for a player (its HUD slot) binds its widgets; a HUD placed in a level never starts. " +
                              "Make it a prefab and put it in the GameMode's HUD slot, or give this widget a Framework Binder.";
        }

        /// <summary>
        /// The HUD on this object or above it, or null. A HUD a GameMode spawns binds every widget under it; one placed in a
        /// level binds nothing (see <see cref="IsPlacedInLevel"/>).
        /// </summary>
        public static HUD HudOf(Component component)
        {
            for (var current = component != null ? component.transform : null; current != null; current = current.parent)
            {
                var hud = current.GetComponent<HUD>();

                if (hud != null)
                {
                    return hud;
                }
            }

            return null;
        }

        /// <summary>
        /// True when a parent widget holds this one in a [BindWidget] field (it gives it its ViewModel).
        /// </summary>
        public static bool BoundByParent(Widget widget)
        {
            return widget != null && BoundByDetector.Detect(widget).HasValue;
        }

        /// <summary>
        /// The Framework Binder's player (0 = the first player).
        /// </summary>
        public static int PlayerIndex(FrameworkBinder binder)
        {
            var property = binder != null ? new SerializedObject(binder).FindProperty("playerIndex") : null;

            return property != null ? Mathf.Max(0, property.intValue) : 0;
        }

        /// <summary>
        /// The GameModes whose HUD is this HUD's prefab (none for a HUD that is not a prefab).
        /// </summary>
        public static IReadOnlyList<GameMode> GameModesUsing(HUD hud)
        {
            var asset = HudAsset_Internal(hud);

            return asset != null ? FrameworkIndex.GameModesUsing(asset.gameObject) : Array.Empty<GameMode>();
        }

        /// <summary>
        /// The classes a HUD's widgets are checked against: those of <paramref name="preview"/>, else of the first GameMode
        /// that uses the HUD's prefab (the project default first), else of the level the HUD is in, else the framework's
        /// base classes.
        /// </summary>
        public static FrameworkTypeSet SetForHud(HUD hud, GameMode preview = null)
        {
            if (hud == null)
            {
                return FrameworkTypeSet.Assume(typeof(HUD));
            }

            Refresh_Internal();

            var key = (hud, preview);

            if (!_hudSets.TryGetValue(key, out var set))
            {
                set = SetForHud_Internal(hud, preview);

                _hudSets[key] = set;
            }

            return set;
        }

        private static FrameworkTypeSet SetForHud_Internal(HUD hud, GameMode preview)
        {
            var hudType = hud.GetType();

            if (preview != null)
            {
                return FrameworkTypeSet.ForGameMode(preview, Usage_Internal(preview)).WithHud(hudType);
            }

            var users = GameModesUsing(hud);

            if (users.Count > 0)
            {
                var settings = FrameworkIndex.Settings;

                var first = users.FirstOrDefault(user => settings != null && settings.DefaultGameMode == user) ?? users[0];

                return FrameworkTypeSet.ForGameMode(first, Usage_Internal(first)).WithHud(hudType);
            }

            var scene = hud.gameObject.scene;

            if (!EditorUtility.IsPersistent(hud) && scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
            {
                var set = Cached_Internal("scene:" + scene.handle, () => FrameworkTypeSet.ForScene(scene));

                if (set.GameModeObject != null)
                {
                    return set.WithHud(hudType);
                }
            }

            return FrameworkTypeSet.Assume(hudType);
        }

        /// <summary>
        /// "project default" or "used by 3 levels", for a GameMode prefab.
        /// </summary>
        internal static string Usage_Internal(GameMode gameMode)
        {
            if (gameMode == null)
            {
                return string.Empty;
            }

            var settings = FrameworkIndex.Settings;

            if (settings != null && settings.DefaultGameMode == gameMode)
            {
                return "project default";
            }

            if (!EditorUtility.IsPersistent(gameMode))
            {
                return "placed in the scene";
            }

            var levels = FrameworkIndex.LevelsUsing(gameMode.gameObject);

            return levels == 0 ? "not used by a level yet" : levels == 1 ? "used by 1 level" : $"used by {levels} levels";
        }

        private static FrameworkTypeSet SetForLevel_Internal(GameObject gameObject, out bool dependsOnLevel)
        {
            dependsOnLevel = false;

            var scene = gameObject.scene;

            if (!EditorUtility.IsPersistent(gameObject) && scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
            {
                return Cached_Internal("scene:" + scene.handle, () => FrameworkTypeSet.ForScene(scene));
            }

            // A prefab: answered for the project's default GameMode.
            dependsOnLevel = true;

            return Cached_Internal("default", () =>
            {
                var settings = FrameworkIndex.Settings;

                return FrameworkTypeSet.ForGameMode(settings != null ? settings.DefaultGameMode : null, "project default");
            });
        }

        /// <summary>
        /// Clears the cached sets and answers when the hierarchy or the project changed.
        /// </summary>
        private static void Refresh_Internal()
        {
            if (_setsVersion == ProblemProviders.Version)
            {
                return;
            }

            _sets.Clear();

            _hudSets.Clear();

            _bindings.Clear();

            _setsVersion = ProblemProviders.Version;
        }

        private static FrameworkTypeSet Cached_Internal(string key, Func<FrameworkTypeSet> build)
        {
            Refresh_Internal();

            if (!_sets.TryGetValue(key, out var set))
            {
                set = build();

                _sets[key] = set;
            }

            return set;
        }

        private static void Explain_Internal(WidgetBinding binding, string who)
        {
            var viewModelType = binding.Widget.ViewModelType;

            var prediction = binding.Source == BindingSource.HUD ? PredictForHud(viewModelType, binding.Set) : Predict(viewModelType, binding.Set);

            binding.Prediction = prediction;

            var name = viewModelType != null ? viewModelType.Name : "ViewModel";

            if (prediction.Matches)
            {
                binding.Severity = prediction.DependsOnShouldCreate ? 1 : 0;

                binding.Text = $"{who}: will probably bind to {Describe(prediction, binding.Set)}";

                binding.Tooltip = binding.Set.Assumed
                                      ? "No GameMode uses this HUD yet: the answer is for the framework's base classes."
                                      : string.IsNullOrEmpty(binding.Set.GameModeName)
                                          ? string.Empty
                                          : $"Read from {binding.Set.GameModeName}{(string.IsNullOrEmpty(binding.Set.Source) ? string.Empty : $" ({binding.Set.Source})")}.";

                return;
            }

            if (binding.DependsOnPawn && binding.Set.HasPlayer)
            {
                binding.Severity = 2;

                binding.Text = $"{who} binds it once the player has a Pawn, but {(string.IsNullOrEmpty(binding.Set.GameModeName) ? "the GameMode" : binding.Set.GameModeName)} has no Default Pawn of this class";

                binding.Tooltip = "Give the GameMode a Default Pawn, or possess one from code.";

                return;
            }

            binding.Severity = binding.IsFrameworkWidget ? 2 : 1;

            binding.Text = binding.IsFrameworkWidget
                               ? $"No framework object of {(string.IsNullOrEmpty(binding.Set.GameModeName) ? "this level" : binding.Set.GameModeName)} is a {name}: it stays unbound"
                               : $"No framework object is a {name}: it stays unbound unless your code calls SetViewModel";

            binding.Tooltip = binding.Set.HasPlayer || binding.Source != BindingSource.FrameworkBinder
                                  ? "Change the widget's ViewModel class, or give the GameMode the class it expects."
                                  : "This player does not exist when the level begins, so its PlayerState, Pawn, PlayerController and HUD are not there to bind.";
        }

        private static string HudName_Internal(HUD hud)
        {
            var asset = HudAsset_Internal(hud);

            return (asset != null ? asset.gameObject.name : hud.gameObject.name).Trim();
        }

        private static HUD HudAsset_Internal(HUD hud)
        {
            if (hud == null)
            {
                return null;
            }

            var persistent = Scaffold.Persistent(hud) as HUD;

            if (persistent != null && EditorUtility.IsPersistent(persistent))
            {
                return persistent;
            }

            // The prefab this copy comes from (a variant, not the base the variant is made from).
            var source = PrefabUtility.GetCorrespondingObjectFromSource(hud);

            return source != null && EditorUtility.IsPersistent(source) ? source : null;
        }

        private static bool Fits_Internal(Type viewModelType, Type candidate)
        {
            return candidate != null && viewModelType.IsAssignableFrom(candidate);
        }
    }
}
