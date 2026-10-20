using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Where players appear, like Unreal's APlayerStart. Place one or more in the level; the GameMode spawns each player's
    /// Pawn at one of them, facing its blue arrow.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStart : MonoBehaviour
    {
        private static readonly List<PlayerStart> Enabled = new();

        [SerializeField, Tooltip("Optional name to pick this start from code: PlayerStart.Find(\"Red Team\").")]
        private string startTag;

        /// <summary>
        /// Every enabled PlayerStart of the loaded scenes, in the order they were enabled.
        /// </summary>
        public static IReadOnlyList<PlayerStart> All => Enabled;

        public string StartTag => startTag;

        /// <summary>
        /// The first enabled PlayerStart with this tag, or null.
        /// </summary>
        public static PlayerStart Find(string tag)
        {
            for (var i = 0; i < Enabled.Count; i++)
            {
                if (Enabled[i].startTag == tag)
                {
                    return Enabled[i];
                }
            }

            return null;
        }

        /// <summary>
        /// If you need OnEnable, override it and call base.OnEnable(): it registers the start.
        /// </summary>
        protected virtual void OnEnable()
        {
            Enabled.Add(this);
        }

        /// <summary>
        /// If you need OnDisable, override it and call base.OnDisable().
        /// </summary>
        protected virtual void OnDisable()
        {
            Enabled.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset_Internal()
        {
            Enabled.Clear();
        }

#if UNITY_EDITOR
        protected virtual void OnDrawGizmos()
        {
            var position = transform.position;

            var forward = transform.forward;

            Gizmos.color = new Color(0.49f, 0.36f, 1f);

            Gizmos.DrawWireSphere(position + Vector3.up * 0.5f, 0.5f);

            Gizmos.DrawWireSphere(position + Vector3.up * 1.5f, 0.5f);

            Gizmos.DrawLine(position + new Vector3(0.5f, 0.5f, 0f), position + new Vector3(0.5f, 1.5f, 0f));

            Gizmos.DrawLine(position + new Vector3(-0.5f, 0.5f, 0f), position + new Vector3(-0.5f, 1.5f, 0f));

            Gizmos.color = new Color(0.33f, 0.56f, 0.96f);

            var arrowStart = position + Vector3.up * 0.05f;

            var arrowEnd = arrowStart + forward;

            Gizmos.DrawLine(arrowStart, arrowEnd);

            Gizmos.DrawLine(arrowEnd, arrowEnd - forward * 0.3f + transform.right * 0.2f);

            Gizmos.DrawLine(arrowEnd, arrowEnd - forward * 0.3f - transform.right * 0.2f);
        }
#endif
    }
}
