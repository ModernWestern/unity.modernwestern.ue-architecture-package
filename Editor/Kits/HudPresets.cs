using System;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Extra content the Game Mode Kit can put in its HUD prefab, added by other editor assemblies (the ObserverVM
    /// integration adds "HUD with Widgets for Game State and Player State").
    /// </summary>
    internal sealed class HudPreset
    {
        public HudPreset(string id, string label, string tooltip, Func<Type, Type, bool> available, Action<ScaffoldOp, GameObject, Type, Type> apply)
        {
            Id = id ?? string.Empty;

            Label = label ?? Id;

            Tooltip = tooltip ?? string.Empty;

            Available = available ?? ((_, _) => true);

            Apply = apply;
        }

        /// <summary>
        /// The name used by the command line (hud-preset=&lt;id&gt;), e.g. "framework-widgets".
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// What the kit window shows, e.g. "HUD with Widgets for Game State and Player State".
        /// </summary>
        public string Label { get; }

        public string Tooltip { get; }

        /// <summary>
        /// True when the preset has something to add for this GameState and PlayerState class.
        /// </summary>
        public Func<Type, Type, bool> Available { get; }

        /// <summary>
        /// Fills the HUD prefab (its root) for the GameState and PlayerState classes, inside the kit's gesture.
        /// </summary>
        public Action<ScaffoldOp, GameObject, Type, Type> Apply { get; }
    }

    /// <summary>
    /// The HUD presets of the Game Mode Kit (see <see cref="HudPreset"/>).
    /// </summary>
    internal static class HudPresets
    {
        private static readonly List<HudPreset> _presets = new();

        public static IReadOnlyList<HudPreset> All => _presets;

        /// <summary>
        /// Adds a preset; one with the same id is replaced.
        /// </summary>
        public static void Register(HudPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.Id) || preset.Apply == null)
            {
                return;
            }

            _presets.RemoveAll(other => other.Id == preset.Id);

            _presets.Add(preset);
        }

        public static void Unregister(string id)
        {
            _presets.RemoveAll(preset => preset.Id == id);
        }

        /// <summary>
        /// The preset with this id, or null.
        /// </summary>
        public static HudPreset Find(string id)
        {
            return string.IsNullOrEmpty(id) ? null : _presets.Find(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
