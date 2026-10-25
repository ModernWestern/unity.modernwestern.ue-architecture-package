using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Who controls a Pawn, as the Pawn inspector says it.
    /// </summary>
    internal enum PawnControl
    {
        /// <summary>
        /// Nobody yet: a player or code possesses it.
        /// </summary>
        Nobody,

        /// <summary>
        /// A player: it is a GameMode's Default Pawn, or the Pawn a placed GameMode points to.
        /// </summary>
        Player,

        /// <summary>
        /// An AI controller it creates by itself (Auto Possess AI).
        /// </summary>
        AI
    }

    /// <summary>
    /// The Pawn inspector: "Controlled by" (the player, AI or nobody yet), the AI section (Auto Possess AI and the AI
    /// Controller class field, with fixes), and with Unity's AI module a Navigation section (NavMeshAgent and Path
    /// Following in one click, Block Time and Block Distance inline, Off the NavMesh with Snap, No NavMesh in this
    /// level). Fields of subclasses are drawn as usual below.
    /// </summary>
    [CustomEditor(typeof(Pawn), true)]
    [CanEditMultipleObjects]
    internal sealed class PawnEditor : Editor
    {
        private const string AiFoldoutKey = "UEArchitecture.PawnEditor.AI";

        private static readonly string[] _excluded = { "m_Script", "autoPossessAI", "aiController" };

        private int _version = -1;

        private PawnControl _control;

        private string _controlledBy = string.Empty;

#if UEA_NAVIGATION
        private bool _offNavMesh;

        private bool _noNavMesh;
#endif

        private Vector3 _checkedAt;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            var single = targets.Length == 1 ? (Pawn)target : null;

            if (single != null)
            {
                Refresh_Internal(single);

                UEArchitectureStyles.Row("Controlled by", _controlledBy, _control == PawnControl.Nobody);
            }

            DrawAi_Internal(single);

            DrawPropertiesExcluding(serializedObject, _excluded);

            serializedObject.ApplyModifiedProperties();

            DrawNavigation_Internal(single);
        }

        /// <summary>
        /// "the player (Default Pawn of Arena Mode)", "AI (placed in world or spawned): GuardAI" or "nobody yet: a player
        /// or your code possesses it".
        /// </summary>
        internal static PawnControl ControlledBy(Pawn pawn, out string text)
        {
            text = "nobody yet: a player or your code possesses it";

            if (pawn == null)
            {
                return PawnControl.Nobody;
            }

            // A Pawn placed in a level: the GameMode spawns its own copy of its Default Pawn for the player, so a placed copy
            // of that prefab is not the player's, and the level possesses placed Pawns with Auto Possess AI "Placed in world".
            var placedInLevel = !EditorUtility.IsPersistent(pawn) && pawn.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(pawn.gameObject.scene);

            // The prefab asset itself, or the prefab open in Prefab Mode (its file; for a variant, the variant).
            var asset = placedInLevel ? null : Scaffold.Persistent(pawn) as Pawn;

            if (asset != null && EditorUtility.IsPersistent(asset))
            {
                var users = FrameworkIndex.GameModesUsing(asset.transform.root.gameObject).Where(gameMode => gameMode != null && gameMode.DefaultPawnPrefab != null &&
                                                                                                             gameMode.DefaultPawnPrefab.gameObject == asset.transform.root.gameObject).ToList();

                if (users.Count > 0)
                {
                    text = $"the player (Default Pawn of {users[0].gameObject.name}{(users.Count > 1 ? $" and {users.Count - 1} more" : string.Empty)})";

                    return PawnControl.Player;
                }
            }

            if (placedInLevel)
            {
                var placed = GameModeInfo.Find<GameMode>(pawn.gameObject.scene);

                if (placed != null && GameModeInfo.TryPlacedPawn(new SerializedObject(placed), out var field, out var found) && found == pawn)
                {
                    text = $"the player ({placed.gameObject.name}'s {field}, possessed by its code)";

                    return PawnControl.Player;
                }

                if (pawn.AutoPossessAI == AutoPossessAI.Spawned)
                {
                    text = "nobody yet (placed in the level; Auto Possess AI \"Spawned\" applies only to World.SpawnPawn)";

                    return PawnControl.Nobody;
                }
            }

            if (pawn.AutoPossessAI != AutoPossessAI.Disabled)
            {
                var controller = new SerializedObject(pawn).FindProperty("aiController").objectReferenceValue;

                text = $"AI ({ObjectNames.NicifyVariableName(pawn.AutoPossessAI.ToString()).ToLowerInvariant()}): {(controller != null ? controller.name : "a plain AIController")}";

                return PawnControl.AI;
            }

            var source = placedInLevel ? PrefabUtility.GetCorrespondingObjectFromSource(pawn) : null;

            if (source != null && FrameworkIndex.GameModesUsing(source.transform.root.gameObject).Any(gameMode => gameMode != null && gameMode.DefaultPawnPrefab != null &&
                                                                                                                  gameMode.DefaultPawnPrefab.gameObject == source.transform.root.gameObject))
            {
                text = "nobody yet: the GameMode spawns its own copy of this Default Pawn for the player; this placed copy is possessed only by your code";
            }

            return PawnControl.Nobody;
        }

        private void Refresh_Internal(Pawn pawn)
        {
            var moved = pawn.transform.position != _checkedAt;

            if (_version == ProblemProviders.Version && !moved)
            {
                return;
            }

            _version = ProblemProviders.Version;

            _checkedAt = pawn.transform.position;

            _control = ControlledBy(pawn, out _controlledBy);

#if UEA_NAVIGATION
            _offNavMesh = false;

            _noNavMesh = false;

            if (!EditorUtility.IsPersistent(pawn) && pawn.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(pawn.gameObject.scene) &&
                pawn.GetComponent<UnityEngine.AI.NavMeshAgent>() != null)
            {
                _noNavMesh = !BuiltInProblems.HasNavMesh_Internal();

                _offNavMesh = !_noNavMesh && !LevelSetup.TryNavMesh(pawn.transform.position, 0.5f, out _);
            }
#endif
        }

        private void DrawAi_Internal(Pawn single)
        {
            var autoPossess = serializedObject.FindProperty("autoPossessAI");

            var controller = serializedObject.FindProperty("aiController");

            // A Pawn the player controls rarely needs the AI rows: they fold away.
            if (single != null && _control == PawnControl.Player)
            {
                var open = SessionState.GetBool(AiFoldoutKey, false);

                var now = EditorGUILayout.Foldout(open, "AI", true);

                if (now != open)
                {
                    SessionState.SetBool(AiFoldoutKey, now);
                }

                if (!now)
                {
                    return;
                }
            }
            else
            {
                UEArchitectureStyles.Section("AI");
            }

            // Drawn by hand: PropertyField would repeat the field's [Header("AI")] under this section's own title.
            using (var change = new EditorGUI.ChangeCheckScope())
            {
                EditorGUI.showMixedValue = autoPossess.hasMultipleDifferentValues;

                var picked = (AutoPossessAI)EditorGUILayout.EnumPopup(new GUIContent("Auto Possess AI", autoPossess.tooltip), (AutoPossessAI)autoPossess.intValue);

                EditorGUI.showMixedValue = false;

                if (change.changed)
                {
                    autoPossess.intValue = (int)picked;
                }
            }

            EditorGUILayout.PropertyField(controller, new GUIContent("AI Controller", controller.tooltip));

            if (single == null)
            {
                return;
            }

            serializedObject.ApplyModifiedProperties();

            foreach (var problem in ProblemProviders.For(single))
            {
                if (problem.FixId != "turn-on-auto-possess")
                {
                    ProblemsSection.DrawProblem(problem);

                    continue;
                }

                EditorGUILayout.HelpBox(problem.Message, MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    foreach (var (label, mode) in new[] { ("Placed", AutoPossessAI.PlacedInWorld), ("Spawned", AutoPossessAI.Spawned), ("Both", AutoPossessAI.PlacedInWorldOrSpawned) })
                    {
                        if (UEArchitectureStyles.SmallButton(new GUIContent(label, $"Auto Possess AI: {ObjectNames.NicifyVariableName(mode.ToString())}.")))
                        {
                            ClassField.Run_Internal(() => UEArchitectureActions.SetAutoPossess(single, mode));

                            ProblemProviders.Invalidate();

                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawNavigation_Internal(Pawn single)
        {
#if UEA_NAVIGATION
            var pawns = targets.OfType<Pawn>().ToArray();

            var missing = pawns.Where(pawn => pawn.GetComponent<UnityEngine.AI.NavMeshAgent>() == null || pawn.GetComponent<PathFollowing>() == null).ToArray();

            if (single == null && missing.Length == 0)
            {
                return;
            }

            UEArchitectureStyles.Section("Navigation");

            if (missing.Length > 0)
            {
                var hasAgent = missing.All(pawn => pawn.GetComponent<UnityEngine.AI.NavMeshAgent>() != null);

                var label = (hasAgent ? "Add Path Following" : "Add NavMeshAgent + Path Following") + (missing.Length > 1 ? $" ({missing.Length})" : string.Empty);

                var message = hasAgent
                                  ? "MoveTo adds Path Following when it first runs; add it now to tune Block Time and Block Distance for this Pawn."
                                  : "No NavMeshAgent: MoveTo cannot move this Pawn.";

                if (UEArchitectureStyles.Notice(message, hasAgent ? MessageType.Info : MessageType.None, label))
                {
                    ClassField.Run_Internal(() => UEArchitectureActions.AddPathFollowing(missing.Cast<Object>()));

                    ProblemProviders.Invalidate();

                    GUIUtility.ExitGUI();
                }

                return;
            }

            var pathFollowing = single.GetComponent<PathFollowing>();

            var serialized = new SerializedObject(pathFollowing);

            serialized.Update();

            EditorGUILayout.PropertyField(serialized.FindProperty("blockTime"), new GUIContent("Block Time", serialized.FindProperty("blockTime").tooltip));

            EditorGUILayout.PropertyField(serialized.FindProperty("blockDistance"), new GUIContent("Block Distance", serialized.FindProperty("blockDistance").tooltip));

            serialized.ApplyModifiedProperties();

            if (_noNavMesh)
            {
                EditorGUILayout.HelpBox("No NavMesh in this level: bake one (or build it as the level begins) before MoveTo can move this Pawn.", MessageType.Info);
            }
            else if (_offNavMesh && UEArchitectureStyles.Notice("Off the NavMesh: MoveTo cannot move this Pawn until it stands on it.", MessageType.Warning, "Snap"))
            {
                ClassField.Run_Internal(() => UEArchitectureActions.SnapToNavMesh(single));

                ProblemProviders.Invalidate();

                GUIUtility.ExitGUI();
            }
#endif
        }
    }
}
