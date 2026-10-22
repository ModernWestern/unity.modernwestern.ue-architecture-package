#if UEA_NAVIGATION
using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Path Following in the Inspector: its settings and, in Play mode, the move in progress and how the last one ended.
    /// A selected agent draws its path, where arrival is measured and the acceptance radius in the Scene view.
    /// </summary>
    [CustomEditor(typeof(PathFollowing))]
    internal sealed class PathFollowingEditor : Editor
    {
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var pathFollowing = (PathFollowing)target;

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("Moves with MoveTo, from code or from the Pawn's controller. Every move ends with exactly one MoveCompleted: Success, Partial, Blocked, Invalid or Aborted.", UEArchitectureStyles.MutedLabel);

                return;
            }

            UEArchitectureStyles.Section("Move");

            var status = pathFollowing.Status;

            UEArchitectureStyles.Row("Status", status.ToString(), status == MoveStatus.Idle);

            if (pathFollowing.IsMoving)
            {
                var actor = pathFollowing.Target;

                UEArchitectureStyles.Row("Move", "#" + pathFollowing.CurrentMove.Id);

                UEArchitectureStyles.Row(actor != null ? "Actor" : "Destination", actor != null ? actor.name : Format_Internal(pathFollowing.Destination));

                UEArchitectureStyles.Row("Acceptance radius", pathFollowing.AcceptanceRadius.ToString("0.00") + " m");

                var still = pathFollowing.SecondsWithoutProgress;

                UEArchitectureStyles.Row("Without progress", $"{still:0.0} of {pathFollowing.BlockTime:0.0} s", still < 0.05f);
            }

            var last = pathFollowing.LastResult;

            if (last.Request.IsValid)
            {
                UEArchitectureStyles.Row("Last move", $"#{last.Request.Id}  {last}  ·  {last.Duration:0.0} s");
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawGizmos_Internal(PathFollowing pathFollowing, GizmoType type)
        {
            if (!Application.isPlaying || !pathFollowing.IsMoving)
            {
                return;
            }

            var agent = pathFollowing.Agent;

            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return;
            }

            var up = agent.transform.up;

            Handles.color = UEArchitectureStyles.Accent;

            if (agent.hasPath)
            {
                // Editor only: NavMeshAgent.path makes a copy every time.
                Handles.DrawAAPolyLine(3f, agent.path.corners);
            }

            var arrival = pathFollowing.ArrivalPoint_Internal;

            Handles.DrawWireDisc(arrival, up, Mathf.Max(0.05f, pathFollowing.AcceptanceRadius));

            Handles.DrawLine(arrival, arrival + up * agent.height * 0.5f);

            Handles.Label(agent.transform.position + up * (agent.height + 0.2f), pathFollowing.Status.ToString(), EditorStyles.whiteMiniLabel);
        }

        private static string Format_Internal(Vector3 point)
        {
            return $"({point.x:0.0}, {point.y:0.0}, {point.z:0.0})";
        }
    }
}
#endif
