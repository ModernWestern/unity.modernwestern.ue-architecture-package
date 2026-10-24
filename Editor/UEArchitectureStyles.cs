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
        /// A type's name, plus the last part of its namespace when another type of the list has the same name
        /// (e.g. two samples with a BestScore class: "BestScore (Arena)" and "BestScore (ArenaObserverVM)").
        /// </summary>
        public static string TypeName(System.Type type, System.Collections.Generic.IEnumerable<System.Type> among)
        {
            var duplicated = System.Linq.Enumerable.Count(among, other => other.Name == type.Name) > 1;

            if (!duplicated || string.IsNullOrEmpty(type.Namespace))
            {
                return type.Name;
            }

            var dot = type.Namespace.LastIndexOf('.');

            return $"{type.Name} ({type.Namespace.Substring(dot + 1)})";
        }

        /// <summary>
        /// The main action of a section (like "Override for this level"), in the accent color. Returns true when pressed.
        /// </summary>
        public static bool AccentButton(GUIContent content, params GUILayoutOption[] options)
        {
            var previousBackground = GUI.backgroundColor;

            var previousContent = GUI.contentColor;

            GUI.backgroundColor = Accent;

            GUI.contentColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.15f, 0.08f, 0.35f, 1f);

            try
            {
                return GUILayout.Button(content, options);
            }
            finally
            {
                GUI.backgroundColor = previousBackground;

                GUI.contentColor = previousContent;
            }
        }

        /// <summary>
        /// A small secondary button sized to its text. Returns true when pressed.
        /// </summary>
        public static bool SmallButton(GUIContent content)
        {
            return GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(EditorStyles.miniButton.CalcSize(content).x + 12f));
        }

        /// <summary>
        /// True when a settings page search matches one of the words (an empty search matches everything).
        /// </summary>
        public static bool Matches(string search, params string[] words)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            foreach (var word in words)
            {
                if (!string.IsNullOrEmpty(word) && word.IndexOf(search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

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
