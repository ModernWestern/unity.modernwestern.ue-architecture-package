using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Window > UE Architecture > Debugger: the running framework at a glance, like Unreal's gameplay debugger. The game and
    /// its subsystems, the current level with its GameMode, GameState and players, the level's subsystems, the moves in
    /// progress, the active timers and who listens to which message. Expand anything to see its public values, live.
    /// </summary>
    internal sealed class FrameworkDebuggerWindow : EditorWindow
    {
        private readonly HashSet<string> _expanded = new();

        private Vector2 _scroll;

        [MenuItem("Window/UE Architecture/Debugger")]
        public static void Open()
        {
            var window = GetWindow<FrameworkDebuggerWindow>();

            window.titleContent = new GUIContent("Framework Debugger", EditorGUIUtility.IconContent("d_UnityEditor.DebugInspectorWindow").image);

            window.Show();
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(8);

                EditorGUILayout.HelpBox("Press Play to see the running GameInstance, the level, its players, subsystems, timers and messages.", MessageType.Info);

                if (GUILayout.Button("Project Settings > UE Architecture"))
                {
                    SettingsService.OpenProjectSettings(UEArchitectureSettingsProvider.SettingsPath);
                }

                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawGame_Internal();

            DrawLevel_Internal();

#if UEA_NAVIGATION
            DrawNavigation_Internal();
#endif

            DrawTimersAndMessages_Internal();

            EditorGUILayout.EndScrollView();
        }

        private void DrawGame_Internal()
        {
            UEArchitectureStyles.Section("Game");

            if (!GameInstance.IsRunning)
            {
                EditorGUILayout.LabelField("Not running (Start Automatically is off and nobody called GameInstance.Launch()).", UEArchitectureStyles.MutedLabel);

                return;
            }

            DrawObject_Internal("GameInstance", GameInstance.Current, GameInstance.Current != null ? GameInstance.Current.GetType().Name : "none");

            var gameTypes = GameInstance.Subsystems.Select(subsystem => subsystem.GetType()).ToArray();

            foreach (var subsystem in GameInstance.Subsystems)
            {
                DrawObject_Internal("game/" + subsystem.GetType().FullName, subsystem, Describe_Internal(subsystem, "game subsystem"), null, UEArchitectureStyles.TypeName(subsystem.GetType(), gameTypes));
            }

            if (GameInstance.Subsystems.Count == 0)
            {
                EditorGUILayout.LabelField("No game subsystems.", UEArchitectureStyles.MutedLabel);
            }
        }

        private void DrawLevel_Internal()
        {
            UEArchitectureStyles.Section(World.HasBegunPlay ? $"Level: {World.Scene.name}  ·  {World.TimeSeconds:0.0} s" : "Level");

            if (!World.HasBegunPlay)
            {
                EditorGUILayout.LabelField("No level is being played.", UEArchitectureStyles.MutedLabel);

                return;
            }

            DrawObject_Internal("GameMode", World.GameMode, World.GameMode != null ? World.GameMode.GetType().Name : "none");

            if (World.GameState != null)
            {
                DrawObject_Internal("GameState", World.GameState, World.GameState.GetType().Name);
            }

            foreach (var player in World.Players)
            {
                if (player == null)
                {
                    continue;
                }

                var playerState = player.PlayerState;

                var summary = player.Pawn != null ? $"controls {player.Pawn.name}" : "no Pawn";

                DrawObject_Internal("player/" + player.PlayerIndex, playerState, $"{playerState?.PlayerName}  ·  {summary}", player);
            }

            foreach (var ai in World.AIControllers)
            {
                if (ai == null)
                {
                    continue;
                }

                var bot = ai.PlayerState != null ? ai.PlayerState.PlayerName + "  ·  " : string.Empty;

                var summary = bot + (ai.Pawn != null ? "controls " + ai.Pawn.name : "no Pawn");

#if UEA_NAVIGATION
                if (ai.MoveStatus != MoveStatus.Idle)
                {
                    summary += "  ·  " + ai.MoveStatus;
                }
#endif

                // A per-object key without GetInstanceID, a compile error since Unity 6.5.
                DrawObject_Internal("ai/" + EditorCompat.SessionKey(ai), ai, summary, ai, "AI  " + ai.GetType().Name);
            }

            var worldTypes = World.AllSubsystems.Select(subsystem => subsystem.GetType()).ToArray();

            foreach (var subsystem in World.AllSubsystems)
            {
                DrawObject_Internal("world/" + subsystem.GetType().FullName, subsystem, Describe_Internal(subsystem, "level subsystem"), null, UEArchitectureStyles.TypeName(subsystem.GetType(), worldTypes));
            }
        }

#if UEA_NAVIGATION
        private void DrawNavigation_Internal()
        {
            var moving = NavigationSystem.Moving;

            UEArchitectureStyles.Section(moving.Count == 1 ? "Navigation  ·  1 move" : $"Navigation  ·  {moving.Count} moves");

            if (moving.Count == 0)
            {
                EditorGUILayout.LabelField("No moves in progress.", UEArchitectureStyles.MutedLabel);

                return;
            }

            foreach (var pathFollowing in moving)
            {
                if (pathFollowing == null)
                {
                    continue;
                }

                var target = pathFollowing.Target;

                var destination = target != null ? target.name : $"({pathFollowing.Destination.x:0.0}, {pathFollowing.Destination.y:0.0}, {pathFollowing.Destination.z:0.0})";

                var still = pathFollowing.SecondsWithoutProgress;

                var stuck = still >= 0.5f ? $"  ·  {still:0.0} s without progress" : string.Empty;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(pathFollowing.name, GUILayout.Width(280f));

                    EditorGUILayout.LabelField($"{pathFollowing.Status} to {destination}{stuck}", GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52)))
                    {
                        Selection.activeObject = pathFollowing.gameObject;

                        EditorCompat.Ping(pathFollowing.gameObject);
                    }
                }
            }
        }
#endif

        private void DrawTimersAndMessages_Internal()
        {
            UEArchitectureStyles.Section("Timers and messages");

            UEArchitectureStyles.Row("Active timers", Timers.Count.ToString(), Timers.Count == 0);

            IMessageChannel[] channels;

            lock (Messages.Channels)
            {
                channels = Messages.Channels.Where(channel => channel.Count > 0).ToArray();
            }

            if (channels.Length == 0)
            {
                UEArchitectureStyles.Row("Message listeners", "none", true);
            }

            foreach (var channel in channels.OrderBy(channel => channel.MessageType.Name))
            {
                UEArchitectureStyles.Row(channel.MessageType.Name, channel.Count == 1 ? "1 listener" : $"{channel.Count} listeners");
            }
        }

        private static string Describe_Internal(Subsystem subsystem, string kind)
        {
            if (!(subsystem is ITickable))
            {
                return kind;
            }

            var tick = (TickAttribute)Attribute.GetCustomAttribute(subsystem.GetType(), typeof(TickAttribute)) ?? new TickAttribute();

            var order = tick.Order != 0 ? $", order {tick.Order}" : string.Empty;

            var unscaled = tick.Unscaled ? ", unscaled" : string.Empty;

            return $"{kind}  ·  ticks in {tick.Group}{order}{unscaled}";
        }

        private void DrawObject_Internal(string key, object value, string summary, Object ping = null, string label = null)
        {
            if (label == null)
            {
                label = key.StartsWith("player/", StringComparison.Ordinal) ? "Player " + (int.Parse(key.Substring(7)) + 1) : key;
            }

            var expanded = _expanded.Contains(key);

            using (new EditorGUILayout.HorizontalScope())
            {
                // Fixed first column, so summaries line up whether or not the row has a Select button.
                var foldoutRect = GUILayoutUtility.GetRect(new GUIContent(label), EditorStyles.foldout, GUILayout.Width(280f));

                var now = EditorGUI.Foldout(foldoutRect, expanded, label, true);

                EditorGUILayout.LabelField(summary, GUILayout.ExpandWidth(true));

                var target = ping != null ? ping : value as Object;

                if (target != null && GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    Selection.activeObject = target;

                    EditorCompat.Ping(target);
                }

                if (now != expanded)
                {
                    if (now)
                    {
                        _expanded.Add(key);
                    }
                    else
                    {
                        _expanded.Remove(key);
                    }
                }
            }

            if (!expanded || value == null)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                var any = false;

                foreach (var (name, shown) in Values_Internal(value))
                {
                    UEArchitectureStyles.Row(name, shown);

                    any = true;
                }

                if (!any)
                {
                    EditorGUILayout.LabelField("No public values.", UEArchitectureStyles.MutedLabel);
                }
            }
        }

        /// <summary>
        /// Public fields and properties declared by the user's classes (not the framework's or Unity's).
        /// </summary>
        private static IEnumerable<(string name, string value)> Values_Internal(object target)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

            for (var type = target.GetType(); type != null && !IsFrameworkOrUnity_Internal(type); type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    yield return (ObjectNames.NicifyVariableName(field.Name), Show_Internal(Read_Internal(() => field.GetValue(target))));
                }

                foreach (var property in type.GetProperties(flags).Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
                {
                    yield return (ObjectNames.NicifyVariableName(property.Name), Show_Internal(Read_Internal(() => property.GetValue(target))));
                }
            }
        }

        private static bool IsFrameworkOrUnity_Internal(Type type)
        {
            return type.Assembly == typeof(GameInstance).Assembly || type.Assembly == typeof(Object).Assembly || type == typeof(object) || type == typeof(MonoBehaviour);
        }

        private static object Read_Internal(Func<object> read)
        {
            try
            {
                return read();
            }
            catch (Exception exception)
            {
                return "(" + exception.GetBaseException().GetType().Name + ")";
            }
        }

        private static string Show_Internal(object value)
        {
            switch (value)
            {
                case null:
                    return "null";

                case string text:
                    return "\"" + text + "\"";

                case Object unityObject:
                    return unityObject != null ? unityObject.name : "null (destroyed)";

                case ICollection collection:
                    return $"{collection.Count} items";

                default:
                    return value.ToString();
            }
        }
    }
}
