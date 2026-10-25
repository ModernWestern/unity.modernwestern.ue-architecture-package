using System.Linq;
using UnityEditor;
using UnityEngine;
using ModernWestern.UI.ObserverVM;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The Framework Binder inspector: a "Player 1/2…" popup and, in Edit mode, what the widget will probably get; in
    /// Play mode the live object with Ping.
    /// </summary>
    [CustomEditor(typeof(FrameworkBinder))]
    [CanEditMultipleObjects]
    internal sealed class FrameworkBinderEditor : Editor
    {
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            var index = serializedObject.FindProperty("playerIndex");

            var binder = (FrameworkBinder)target;

            var widget = binder.GetComponent<Widget>();

            var binding = widget != null ? BindingPreview.Of(widget) : null;

            var gameMode = binding?.Set?.GameModeObject;

            // The players the GameMode starts with: a binder only sees players that exist when the level begins.
            var players = gameMode != null ? gameMode.PlayersAtStart : 0;

            var count = Mathf.Max(players, index.intValue + 1) + 1;

            var labels = Enumerable.Range(0, count).Select(i => new GUIContent(i < players || gameMode == null ? $"Player {i + 1}" : $"Player {i + 1} (not there when the level begins)")).ToArray();

            EditorGUI.showMixedValue = index.hasMultipleDifferentValues;

            EditorGUI.BeginChangeCheck();

            var picked = EditorGUILayout.Popup(new GUIContent("Player", index.tooltip), Mathf.Clamp(index.intValue, 0, count - 1), labels);

            if (EditorGUI.EndChangeCheck())
            {
                index.intValue = picked;

                // The answer below is cached: read it again for the new player.
                ProblemProviders.Invalidate();
            }

            EditorGUI.showMixedValue = false;

            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1 || widget == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                DrawLive_Internal(widget, index.intValue);

                return;
            }

            if (BindingPreview.BoundByParent(widget))
            {
                EditorGUILayout.HelpBox("A parent widget gives this widget its ViewModel too: remove this binder, or the two fight over it.", MessageType.Warning);

                return;
            }

            // The answer was read before the popup changed the player: read it again.
            binding = BindingPreview.Of(widget);

            if (binding.BinderInsideHud)
            {
                EditorGUILayout.HelpBox($"{binding.Text}. {binding.Tooltip}", MessageType.Warning);

                return;
            }

            var text = binding.Prediction.Matches ? $"Will probably bind to {BindingPreview.Describe(binding.Prediction, binding.Set)}." : binding.Text + ".";

            EditorGUILayout.LabelField(new GUIContent(text, binding.Tooltip), UEArchitectureStyles.Wrapped);

            if (binding.Severity >= 2 && !string.IsNullOrEmpty(binding.Tooltip))
            {
                EditorGUILayout.HelpBox(binding.Tooltip.Trim(), MessageType.Warning);
            }
        }

        private static void DrawLive_Internal(Widget widget, int playerIndex)
        {
            var live = World.HasBegunPlay ? FrameworkViewModels.Resolve(widget.ViewModelType, World.GetPlayer(playerIndex)) : null;

            using (new EditorGUILayout.HorizontalScope())
            {
                var text = live == null ? World.HasBegunPlay ? "nothing (no framework object of this type now)" : "nothing yet: the level has not begun" : live is Object unityObject ? unityObject.name : live.GetType().Name;

                UEArchitectureStyles.Row("Bound to", text, live == null);

                if (live is Object pingable && UEArchitectureStyles.SmallButton(new GUIContent("Ping", "Shows it in the Hierarchy.")))
                {
                    EditorCompat.Ping(pingable);
                }
            }

            var bound = widget.BoundViewModel;

            if (bound != null && !ReferenceEquals(bound, live))
            {
                EditorGUILayout.HelpBox($"The widget shows {(bound is Object unityObject ? unityObject.name : bound.GetType().Name)}, set by something else.", MessageType.Info);
            }
        }
    }
}
