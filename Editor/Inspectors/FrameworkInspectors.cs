using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The GameMode inspector: its default classes as class fields (pick, create, variant, open), its GameState and
    /// PlayerState classes, the rest of its fields, and for a GameMode placed in a level, its level references.
    /// </summary>
    [CustomEditor(typeof(GameMode), true)]
    [CanEditMultipleObjects]
    internal sealed class GameModeEditor : Editor
    {
        private static readonly string[] _panelFields = { "m_Script", "playerController", "defaultPawn", "hud", "playersAtStart" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            var gameMode = (GameMode)target;

            var single = targets.Length == 1;

            UEArchitectureStyles.Section("Default classes");

            SelectedGameModePanel.DrawRows(serializedObject, new GameModePanelContext
            {
                Mode = GameModePanelMode.Inspector,
                SceneName = single ? LevelOfOverride_Internal(gameMode) : null
            });

            DrawPropertiesExcluding(serializedObject, _panelFields);

            serializedObject.ApplyModifiedProperties();

            if (!single)
            {
                return;
            }

            SelectedGameModePanel.DrawFooter(gameMode);

            SceneReferencesSection.Draw(serializedObject);

            if (Application.isPlaying && gameMode.HasStartedPlay)
            {
                UEArchitectureStyles.Section("Playing");

                foreach (var player in World.Players)
                {
                    UEArchitectureStyles.Row(player.PlayerState?.PlayerName ?? player.name, player.Pawn != null ? "controls " + player.Pawn.name : "no Pawn", player.Pawn == null);
                }
            }
        }

        /// <summary>
        /// The level a GameMode prefab is the own override of ("Boss" for Assets/Prefabs/Modes/Boss/ArenaGameMode.prefab).
        /// </summary>
        private static string LevelOfOverride_Internal(GameMode gameMode)
        {
            var path = AssetDatabase.GetAssetPath(Scaffold.Persistent(gameMode));

            return string.IsNullOrEmpty(path) ? null : GeneratorNaming.SceneOfOverrideFolder(GeneratorNaming.FolderOf(path), UEArchitectureEditorSettings.Current);
        }
    }

    /// <summary>
    /// The World Settings inspector, like Unreal's: the GameMode Override as a class field, the Selected GameMode panel
    /// with "Override for this level", and the level's Player Starts.
    /// </summary>
    [CustomEditor(typeof(WorldSettings))]
    internal sealed class WorldSettingsEditor : Editor
    {
        private static readonly GUIContent _overrideLabel = new("GameMode Override", "The GameMode prefab of this level. Empty = the default GameMode from Project Settings.");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var worldSettings = (WorldSettings)target;

            var scene = worldSettings.gameObject.scene;

            var isLevel = !EditorUtility.IsPersistent(worldSettings) && scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene);

            // The level's override folder name: its own copies are "this level" in the picker, and variants go there.
            var sceneName = isLevel ? GameModeInfo.LevelKey(scene) : null;

            ClassField.Layout(serializedObject.FindProperty("gameModeOverride"), _overrideLabel, typeof(GameMode), new ClassFieldOptions { SceneName = sceneName });

            serializedObject.ApplyModifiedProperties();

            if (!isLevel)
            {
                EditorGUILayout.LabelField("Placed in a level, World Settings picks that level's GameMode.", UEArchitectureStyles.MutedLabel);

                return;
            }

            SelectedGameModePanel.DrawForLevel(scene);

            DrawLevel_Internal(scene);
        }

        private static void DrawLevel_Internal(UnityEngine.SceneManagement.Scene scene)
        {
            var gameMode = GameModeInfo.Effective(scene, out GameModeSource _);

            UEArchitectureStyles.Section("This level");

            var starts = GameModeInfo.Count<PlayerStart>(scene);

            UEArchitectureStyles.Row("Player Starts", starts.ToString(), starts == 0);

            if (gameMode != null && gameMode.DefaultPawnPrefab != null && starts == 0 &&
                UEArchitectureStyles.Notice("No Player Start in this level: players will spawn where the GameMode is.", MessageType.Warning, "Add Player Start"))
            {
                FrameworkMenus.CreatePlayerStart(scene);

                GUIUtility.ExitGUI();
            }

            if (GameModeInfo.Count<WorldSettings>(scene) > 1)
            {
                EditorGUILayout.HelpBox("This level has more than one World Settings; only the first one is used.", MessageType.Warning);
            }
        }
    }
}
