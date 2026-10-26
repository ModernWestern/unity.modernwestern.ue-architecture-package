using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using ModernWestern.UI.ObserverVM;
using ModernWestern.UI.ObserverVM.EditorTools;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The HUD inspector's "Widgets in this HUD": which GameModes use the HUD (a ▾ picks the one to preview when several
    /// do), each widget with what it will probably get (or "nothing matches" with Open script), and "Add Widget for ▾"
    /// with the framework classes that have something to show.
    /// </summary>
    internal static class HudWidgetsSection
    {
        private static readonly Dictionary<int, string> _previews = new();

        private static int _version = -1;

        private static HUD _rowsOf;

        private static string _rowsPreview;

        private static List<HudWidgetRow> _rows = new();

        private static List<Type> _types = new();

        public static void Draw(HUD hud, SerializedObject serializedObject)
        {
            if (hud == null)
            {
                return;
            }

            UEArchitectureStyles.Section("Widgets in this HUD");

            var users = BindingPreview.GameModesUsing(hud);

            var preview = Preview_Internal(hud, users);

            DrawUsers_Internal(hud, users, preview);

            Refresh_Internal(hud, preview);

            if (_rows.Count == 0)
            {
                EditorGUILayout.LabelField("No widgets yet. Add one for the framework object it should show.", UEArchitectureStyles.MutedLabel);
            }

            foreach (var row in _rows)
            {
                DrawRow_Internal(row);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(hud)))
                {
                    var content = new GUIContent("Add Widget for ▾", _types.Count == 0
                                                                        ? "Give the GameState, PlayerState, Pawn or a subsystem an Observable, an ObservableList or a Command first."
                                                                        : "A widget inside this HUD for a framework object: the HUD binds it by itself.");

                    if (UEArchitectureStyles.AccentButton(content, GUILayout.Width(150f)))
                    {
                        ShowAddMenu_Internal(hud, preview);
                    }
                }
            }
        }

        private static GameMode Preview_Internal(HUD hud, IReadOnlyList<GameMode> users)
        {
            if (users.Count < 2)
            {
                return users.FirstOrDefault();
            }

            var key = EditorCompat.SessionKey(hud);

            var wanted = _previews.TryGetValue(key, out var path) ? path : null;

            return users.FirstOrDefault(user => AssetDatabase.GetAssetPath(user) == wanted) ?? users[0];
        }

        private static void DrawUsers_Internal(HUD hud, IReadOnlyList<GameMode> users, GameMode preview)
        {
            if (users.Count == 0)
            {
                EditorGUILayout.LabelField("Used by no GameMode yet: widgets are checked against the framework's base classes.", UEArchitectureStyles.MutedLabel);

                return;
            }

            if (users.Count == 1)
            {
                UEArchitectureStyles.Row("Used by", $"{users[0].gameObject.name} ({BindingPreview.Usage_Internal(users[0])})");

                return;
            }

            var labels = users.Select(user => new GUIContent($"{user.gameObject.name} ({BindingPreview.Usage_Internal(user)})")).ToArray();

            var index = Mathf.Max(0, users.ToList().IndexOf(preview));

            var picked = EditorGUILayout.Popup(new GUIContent("Preview with", $"{users.Count} GameModes use this HUD: pick the one whose classes the widgets are checked against."), index, labels);

            if (picked != index)
            {
                _previews[EditorCompat.SessionKey(hud)] = AssetDatabase.GetAssetPath(users[picked]);

                _version = -1;
            }
        }

        private static void Refresh_Internal(HUD hud, GameMode preview)
        {
            var previewPath = preview != null ? AssetDatabase.GetAssetPath(preview) : string.Empty;

            if (_version == ProblemProviders.Version && _rowsOf == hud && _rowsPreview == previewPath)
            {
                return;
            }

            _version = ProblemProviders.Version;

            _rowsOf = hud;

            _rowsPreview = previewPath;

            _rows = FrameworkWidgetActions.HudWidgets(hud, preview);

            _types = FrameworkWidgetActions.WidgetTypesForHud(hud, preview);
        }

        private static void DrawRow_Internal(HudWidgetRow row)
        {
            var rect = EditorGUILayout.GetControlRect();

            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);

            var misplaced = row.Binding.BinderInsideHud || row.Binding.HudNotSpawned;

            var button = row.Matches || row.GivenByParent || misplaced ? 0f : 80f;

            var valueRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth - button, rect.height);

            if (GUI.Button(labelRect, new GUIContent(row.Widget.name, "Select it"), EditorStyles.label))
            {
                Selection.activeObject = row.Widget.gameObject;
            }

            string text;

            Color color;

            if (row.GivenByParent)
            {
                text = "given by its parent widget";

                color = UEArchitectureStyles.Muted;
            }
            else if (row.Binding.BinderInsideHud)
            {
                text = "→ its Framework Binder binds it first: remove the binder";

                color = UEArchitectureStyles.Warning;
            }
            else if (row.Binding.HudNotSpawned)
            {
                text = "→ nothing: this HUD is placed in the level";

                color = UEArchitectureStyles.Error;
            }
            else if (row.Matches)
            {
                text = $"→ {BindingPreview.SlotLabel(row.Binding.Prediction.Slot)} ({row.Binding.Prediction.Type.Name})";

                color = row.Binding.Prediction.DependsOnShouldCreate ? UEArchitectureStyles.Muted : UEArchitectureStyles.Accent;
            }
            else
            {
                text = row.Binding.DependsOnPawn ? "→ the Pawn, once the player has one" : "→ nothing matches";

                color = row.Binding.DependsOnPawn ? UEArchitectureStyles.Warning : UEArchitectureStyles.Error;
            }

            var previous = GUI.contentColor;

            GUI.contentColor = color;

            EditorGUI.LabelField(valueRect, new GUIContent(text, row.Binding.Text));

            GUI.contentColor = previous;

            if (button > 0f && GUI.Button(new Rect(valueRect.xMax, rect.y, button, rect.height), new GUIContent("Open script", "Change the ViewModel class this widget expects."), EditorStyles.miniButton))
            {
                var script = MonoScript.FromMonoBehaviour(row.Widget);

                if (script != null)
                {
                    AssetDatabase.OpenAsset(script);
                }
            }
        }

        private static void ShowAddMenu_Internal(HUD hud, GameMode preview)
        {
            var menu = new GenericMenu();

            var set = BindingPreview.SetForHud(hud, preview);

            if (_types.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Nothing to show: give the GameState or PlayerState an Observable first"));
            }

            foreach (var type in _types)
            {
                var slot = BindingPreview.Predict(type, set).Slot;

                var group = slot == FrameworkSlot.WorldSubsystem || slot == FrameworkSlot.GameInstanceSubsystem ? "Subsystems/" : string.Empty;

                var existing = ViewModelCatalog.WidgetsFor(type);

                var suffix = existing.Count == 1 ? $" ({existing[0].Name})" : existing.Count == 0 ? " (new widget class)" : string.Empty;

                var label = group.Length > 0 ? $"{group}{type.Name}{suffix}" : $"{BindingPreview.SlotLabel(slot)}: {type.Name}{suffix}";

                var captured = type;

                if (existing.Count > 1)
                {
                    foreach (var widgetType in existing)
                    {
                        var widgetCaptured = widgetType;

                        menu.AddItem(new GUIContent($"{label}/{widgetType.Name}"), false, () => ClassField.Run_Internal(() => FrameworkWidgetActions.AddHudWidget(hud, captured, widgetCaptured)));
                    }

                    continue;
                }

                menu.AddItem(new GUIContent(label), false, () => ClassField.Run_Internal(() => FrameworkWidgetActions.AddHudWidget(hud, captured)));
            }

            menu.ShowAsContext();
        }
    }
}
