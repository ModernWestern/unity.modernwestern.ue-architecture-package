using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Shared look of the UE Architecture editor UI: sober, readable in the light and dark editor themes, one solid accent
    /// (the same as ObserverVM's, so both tools look like one).
    /// </summary>
    internal static class UEArchitectureStyles
    {
        public static readonly Color Accent = new Color32(0x7C, 0x5C, 0xFF, 0xFF);

        public static readonly Color Error = new Color32(0xE5, 0x48, 0x4D, 0xFF);

        public static readonly Color Warning = new Color32(0xE8, 0xA3, 0x3D, 0xFF);

        private static GUIStyle _sectionTitle;

        private static GUIStyle _muted;

        private static GUIStyle _wrapped;

        private static GUIStyle _chip;

        public static Color Muted => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.45f) : new Color(0f, 0f, 0f, 0.5f);

        public static Color Line => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.12f);

        public static GUIStyle SectionTitle => _sectionTitle ??= new GUIStyle(EditorStyles.boldLabel) { margin = new RectOffset(3, 3, 10, 2) };

        public static GUIStyle MutedLabel => _muted ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Muted } };

        public static GUIStyle Wrapped => _wrapped ??= new GUIStyle(EditorStyles.label) { wordWrap = true };

        public static GUIStyle Chip => _chip ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 1, 2),
            fontStyle = FontStyle.Bold
        };

        public static void Section(string title)
        {
            EditorGUILayout.LabelField(title, SectionTitle);

            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, Line);

            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// A label and a value on one line, the value in the muted color when it is a default.
        /// </summary>
        public static void Row(string label, string value, bool muted = false)
        {
            var rect = EditorGUILayout.GetControlRect();

            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);

            var valueRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);

            EditorGUI.LabelField(labelRect, label);

            if (muted)
            {
                EditorGUI.LabelField(valueRect, value, MutedLabel);
            }
            else
            {
                EditorGUI.LabelField(valueRect, value);
            }
        }

        public static void DrawChip(Rect rect, string text, Color color)
        {
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, EditorGUIUtility.isProSkin ? 0.22f : 0.15f));

            var previous = Chip.normal.textColor;

            Chip.normal.textColor = color;

            GUI.Label(rect, text, Chip);

            Chip.normal.textColor = previous;
        }

        public static Vector2 ChipSize(string text) => Chip.CalcSize(new GUIContent(text));

        /// <summary>
        /// A notice with an optional fix button. Returns true when the button was pressed.
        /// </summary>
        public static bool Notice(string message, MessageType type, string button = null)
        {
            EditorGUILayout.HelpBox(message, type);

            if (string.IsNullOrEmpty(button))
            {
                return false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                return GUILayout.Button(button, GUILayout.Width(Mathf.Max(140f, EditorStyles.miniButton.CalcSize(new GUIContent(button)).x + 20f)));
            }
        }
    }
}
