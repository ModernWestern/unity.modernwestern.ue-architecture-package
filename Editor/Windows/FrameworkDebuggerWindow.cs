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
    /// its subsystems, the current level with its GameMode, GameState and players, the level's subsystems, the active timers
    /// and who listens to which message. Expand anything to see its public values, live.
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

            foreach (var subsystem in GameInstance.Subsystems)
            {
                DrawObject_Internal("game/" + subsystem.GetType().FullName, subsystem, Describe_Internal(subsystem));
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

            foreach (var subsystem in World.AllSubsystems)
            {
                DrawObject_Internal("world/" + subsystem.GetType().FullName, subsystem, Describe_Internal(subsystem));
            }
        }

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

        private static string Describe_Internal(Subsystem subsystem)
        {
            var name = subsystem.GetType().Name;

            return subsystem is ITickable ? name + "  ·  ticks" : name;
        }

        private void DrawObject_Internal(string key, object value, string summary, Object ping = null)
        {
            var label = key.Contains('/') ? key.Substring(0, key.IndexOf('/')) : key;

            if (key.StartsWith("player/", StringComparison.Ordinal))
            {
                label = "Player " + (int.Parse(key.Substring(7)) + 1);
            }
            else if (key.StartsWith("game/", StringComparison.Ordinal) || key.StartsWith("world/", StringComparison.Ordinal))
            {
                label = "Subsystem";
            }

            var expanded = _expanded.Contains(key);

            using (new EditorGUILayout.HorizontalScope())
            {
                var now = EditorGUILayout.Foldout(expanded, label, true);

                EditorGUILayout.LabelField(summary);

                var target = ping != null ? ping : value as Object;

                if (target != null && GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    Selection.activeObject = target;

                    EditorGUIUtility.PingObject(target);
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
