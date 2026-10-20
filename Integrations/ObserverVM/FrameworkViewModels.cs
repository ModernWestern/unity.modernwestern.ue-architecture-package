using System;
using UnityEngine;
using System.Collections.Generic;
using ModernWestern.UI.ObserverVM;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Connects ObserverVM Widgets to the gameplay framework (compiled only when ObserverVM is installed).
    /// A Widget is bound to the framework object that matches its ViewModel type, looked up in this order:
    /// the player's PlayerState, Pawn, PlayerController and HUD; the GameState and GameMode; a level subsystem; a game
    /// subsystem; the GameInstance. Widgets inside a HUD are bound by themselves; elsewhere add a <see cref="FrameworkBinder"/>.
    /// </summary>
    public static class FrameworkViewModels
    {
        private static readonly Dictionary<Type, WidgetSetter> Setters = new();

        // Makes IL2CPP generate the shared code that WidgetSetter<T> needs for any reference type.
        private static readonly WidgetSetter Canonical = new WidgetSetter<object>();

        /// <summary>
        /// The framework object a Widget expecting <paramref name="viewModelType"/> should show, for a player. Null if none.
        /// </summary>
        public static object Resolve(Type viewModelType, PlayerController player)
        {
            if (viewModelType == null)
            {
                return null;
            }

            if (player != null)
            {
                if (viewModelType.IsInstanceOfType(player.PlayerState))
                {
                    return player.PlayerState;
                }

                if (player.Pawn != null && viewModelType.IsInstanceOfType(player.Pawn))
                {
                    return player.Pawn;
                }

                if (viewModelType.IsInstanceOfType(player))
                {
                    return player;
                }

                if (player.HUD != null && viewModelType.IsInstanceOfType(player.HUD))
                {
                    return player.HUD;
                }
            }

            if (viewModelType.IsInstanceOfType(World.GameState))
            {
                return World.GameState;
            }

            if (World.GameMode != null && viewModelType.IsInstanceOfType(World.GameMode))
            {
                return World.GameMode;
            }

            if (typeof(WorldSubsystem).IsAssignableFrom(viewModelType))
            {
                return World.Subsystems.Get(viewModelType);
            }

            if (typeof(GameInstanceSubsystem).IsAssignableFrom(viewModelType))
            {
                return GameFramework.GameSubsystems.Get(viewModelType);
            }

            return GameInstance.Current != null && viewModelType.IsInstanceOfType(GameInstance.Current) ? GameInstance.Current : null;
        }

        /// <summary>
        /// True if the Widget expects something that depends on the player's current Pawn.
        /// </summary>
        public static bool DependsOnPawn(Widget widget)
        {
            return widget != null && typeof(Pawn).IsAssignableFrom(widget.ViewModelType);
        }

        /// <summary>
        /// Binds a Widget to the framework object matching its ViewModel type. Returns false (and leaves the Widget alone)
        /// if nothing matches.
        /// </summary>
        public static bool Bind(Widget widget, PlayerController player)
        {
            if (widget == null)
            {
                return false;
            }

            var viewModel = Resolve(widget.ViewModelType, player);

            if (viewModel == null && !DependsOnPawn(widget))
            {
                return false;
            }

            // A Pawn widget with no Pawn (dead, not spawned yet) is unbound until the player gets one.
            SetViewModel(widget, viewModel);

            return viewModel != null;
        }

        /// <summary>
        /// Binds every Widget under a HUD that nobody bound yet. Parents go first, so a Widget that hands ViewModels to
        /// its children (Bind.Child) keeps doing it.
        /// </summary>
        public static void BindHUD(HUD hud)
        {
            if (hud == null)
            {
                return;
            }

            foreach (var widget in hud.GetComponentsInChildren<Widget>(true))
            {
                if (widget.BoundViewModel == null)
                {
                    Bind(widget, hud.Owner);
                }
            }
        }

        /// <summary>
        /// Sets any ViewModel on a Widget without knowing its generic type. The value must match the Widget's ViewModel type.
        /// </summary>
        public static void SetViewModel(Widget widget, object viewModel)
        {
            var type = widget.ViewModelType;

            if (!Setters.TryGetValue(type, out var setter))
            {
                setter = (WidgetSetter)Activator.CreateInstance(typeof(WidgetSetter<>).MakeGenericType(type));

                Setters.Add(type, setter);
            }

            setter.Set(widget, viewModel);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook_Internal()
        {
            HUD.Initialized_Internal -= BindHUD;

            HUD.Initialized_Internal += BindHUD;

            Controller.AnyPawnChanged_Internal -= RebindPawnWidgets_Internal;

            Controller.AnyPawnChanged_Internal += RebindPawnWidgets_Internal;

            _ = Canonical;
        }

        private static void RebindPawnWidgets_Internal(Controller controller)
        {
            if (!(controller is PlayerController player) || player == null || player.HUD == null)
            {
                return;
            }

            foreach (var widget in player.HUD.GetComponentsInChildren<Widget>(true))
            {
                if (DependsOnPawn(widget))
                {
                    Bind(widget, player);
                }
            }
        }

        private abstract class WidgetSetter
        {
            public abstract void Set(Widget widget, object viewModel);
        }

        private sealed class WidgetSetter<T> : WidgetSetter where T : class
        {
            public override void Set(Widget widget, object viewModel)
            {
                ((Widget<T>)widget).SetViewModel((T)viewModel);
            }
        }
    }
}
