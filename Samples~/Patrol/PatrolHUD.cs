using UnityEngine;

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // The player's screen, drawn with IMGUI so the sample needs no UI package: how every move ended (the guards' and
    // yours, sent as MoveEnded messages) and, over each walker, what it is doing.
    public sealed class PatrolHUD : HUD
    {
        private const int MaxLines = 7;

        private static readonly Color PanelColor = new(0.07f, 0.08f, 0.11f, 0.88f);

        private static readonly Color TextColor = new(0.95f, 0.96f, 0.98f);

        private static readonly Color MutedColor = new(0.62f, 0.66f, 0.74f);

        private static readonly Color AccentColor = new Color32(0x7C, 0x5C, 0xFF, 0xFF);

        private static readonly Color WarningColor = new Color32(0xE8, 0xA3, 0x3D, 0xFF);

        private static readonly Color ErrorColor = new Color32(0xE5, 0x48, 0x4D, 0xFF);

        private readonly string[] _lines = new string[MaxLines];

        private readonly MoveOutcome[] _outcomes = new MoveOutcome[MaxLines];

        private int _count;

        private GUIStyle _text;

        private GUIStyle _muted;

        private GUIStyle _chip;

        protected override void OnInit()
        {
            // Listeners of a destroyed HUD stop by themselves.
            Messages<MoveEnded>.Listen(this, static (hud, message) => hud.Add(message));
        }

        private void Add(MoveEnded message)
        {
            for (var i = Mathf.Min(_count, MaxLines - 1); i > 0; i--)
            {
                _lines[i] = _lines[i - 1];

                _outcomes[i] = _outcomes[i - 1];
            }

            var result = message.Result;

            var reason = result.Reason != MoveReason.None ? "  ·  " + Words_Internal(result.Reason.ToString()) : string.Empty;

            _lines[0] = $"{message.Who} to {message.Goal}  ·  {result.Duration:0.0} s{reason}";

            _outcomes[0] = result.Outcome;

            _count = Mathf.Min(_count + 1, MaxLines);
        }

        private void OnGUI()
        {
            if (_text == null)
            {
                _text = new GUIStyle(GUI.skin.label) { fontSize = 17, normal = { textColor = TextColor } };

                _muted = new GUIStyle(_text) { fontSize = 15, normal = { textColor = MutedColor } };

                _chip = new GUIStyle(_text) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            // Laid out for 1080 pixels of height, scaled to the screen.
            var scale = Screen.height / 1080f;

            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var width = Screen.width / scale;

            DrawLog_Internal();

            DrawCaught_Internal(width);

            DrawLabels_Internal(scale);

            var help = "Click to walk     ·     Guards chase you when they see you     ·     A gate closes now and then, and one point is on a platform nobody can reach";

            var helpWidth = _muted.CalcSize(new GUIContent(help)).x + 48f;

            Fill_Internal(new Rect((width - helpWidth) * 0.5f, 1080f - 76f, helpWidth, 44f), PanelColor);

            GUI.Label(new Rect((width - helpWidth) * 0.5f + 24f, 1080f - 66f, helpWidth, 26f), help, _muted);
        }

        private void DrawLog_Internal()
        {
            var panel = new Rect(32f, 32f, 560f, 64f + Mathf.Max(1, _count) * 32f);

            Fill_Internal(panel, PanelColor);

            GUI.Label(new Rect(panel.x + 20f, panel.y + 16f, 520f, 26f), "How each move ended", _muted);

            if (_count == 0)
            {
                GUI.Label(new Rect(panel.x + 20f, panel.y + 48f, 520f, 26f), "Nothing yet", _text);

                return;
            }

            for (var i = 0; i < _count; i++)
            {
                var y = panel.y + 48f + i * 32f;

                var outcome = _outcomes[i];

                var chip = new Rect(panel.x + 20f, y + 2f, 84f, 22f);

                Fill_Internal(chip, Fade_Internal(Color_Internal(outcome), 0.25f));

                _chip.normal.textColor = Color_Internal(outcome);

                GUI.Label(chip, outcome.ToString(), _chip);

                GUI.Label(new Rect(chip.xMax + 12f, y, 430f, 26f), _lines[i], i == 0 ? _text : _muted);
            }
        }

        private void DrawCaught_Internal(float width)
        {
            var gameState = World.GetGameState<PatrolGameState>();

            if (gameState == null)
            {
                return;
            }

            var panel = new Rect(width - 32f - 180f, 32f, 180f, 58f);

            Fill_Internal(panel, PanelColor);

            GUI.Label(new Rect(panel.x + 20f, panel.y + 16f, 140f, 26f), "Caught  " + gameState.TimesCaught, _text);
        }

        // What each walker is doing, over its head.
        private void DrawLabels_Internal(float scale)
        {
            var camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            foreach (var ai in World.AIControllers)
            {
                if (ai is GuardController guard && guard.Pawn != null)
                {
                    Label_Internal(camera, scale, guard.Pawn.transform.position, guard.Pawn.name + "  ·  " + guard.Activity);
                }
            }

            if (Owner is PatrolPlayerController player && player.Pawn != null)
            {
                Label_Internal(camera, scale, player.Pawn.transform.position, "You  ·  " + player.Activity);
            }
        }

        private void Label_Internal(Camera camera, float scale, Vector3 position, string text)
        {
            var screen = camera.WorldToScreenPoint(position + Vector3.up * 1.6f);

            if (screen.z <= 0f)
            {
                return;
            }

            var size = _muted.CalcSize(new GUIContent(text));

            var center = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);

            var box = new Rect(center.x - size.x * 0.5f - 10f, center.y - 28f, size.x + 20f, 26f);

            Fill_Internal(box, PanelColor);

            GUI.Label(new Rect(box.x + 10f, box.y + 1f, size.x + 4f, 24f), text, _muted);
        }

        private static Color Color_Internal(MoveOutcome outcome)
        {
            switch (outcome)
            {
                case MoveOutcome.Success:
                    return AccentColor;

                case MoveOutcome.Partial:
                    return WarningColor;

                case MoveOutcome.Aborted:
                    return MutedColor;

                default:
                    return ErrorColor;
            }
        }

        // "AlreadyThere" reads "Already there".
        private static string Words_Internal(string name)
        {
            var words = new System.Text.StringBuilder(name.Length + 4);

            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                {
                    words.Append(' ').Append(char.ToLowerInvariant(name[i]));
                }
                else
                {
                    words.Append(name[i]);
                }
            }

            return words.ToString();
        }

        private static Color Fade_Internal(Color color, float alpha)
        {
            color.a = alpha;

            return color;
        }

        private static void Fill_Internal(Rect rect, Color color)
        {
            var previous = GUI.color;

            GUI.color = color;

            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            GUI.color = previous;
        }
    }
}
