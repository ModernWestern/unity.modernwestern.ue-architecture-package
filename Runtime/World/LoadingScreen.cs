using UnityEngine;
using System.Collections;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Shown by <see cref="Levels.Open"/> while a level loads. Put it on a prefab with your loading UI (a Canvas, a progress
    /// bar...) and pick the prefab in Project Settings > UE Architecture. It survives the level change by itself.
    /// <para>By default it fades a CanvasGroup in and out; override <see cref="Show"/>, <see cref="Hide"/> and
    /// <see cref="OnProgress"/> for your own animation.</para>
    /// </summary>
    [AddComponentMenu("UE Architecture/Loading Screen")]
    public class LoadingScreen : MonoBehaviour
    {
        [SerializeField, Tooltip("Faded in and out. Empty = the screen just turns on and off.")]
        private CanvasGroup canvasGroup;

        [SerializeField, Min(0f)] private float fadeSeconds = 0.25f;

        [SerializeField, Min(0f), Tooltip("Stays at least this long, so quick loads do not flash.")]
        private float minimumSeconds = 0.5f;

        /// <summary>
        /// Loading progress, from 0 to 1.
        /// </summary>
        public float Progress { get; private set; }

        public float MinimumSeconds => minimumSeconds;

        /// <summary>
        /// Called every frame while loading, with the progress from 0 to 1.
        /// </summary>
        protected virtual void OnProgress(float progress)
        {
        }

        /// <summary>
        /// Plays when the load starts. The old level is still there until it finishes.
        /// </summary>
        protected virtual IEnumerator Show()
        {
            gameObject.SetActive(true);

            yield return Fade_Internal(1f);
        }

        /// <summary>
        /// Plays once the new level began.
        /// </summary>
        protected virtual IEnumerator Hide()
        {
            yield return Fade_Internal(0f);

            gameObject.SetActive(false);
        }

        internal IEnumerator Show_Internal()
        {
            SetProgress_Internal(0f);

            return Show();
        }

        internal IEnumerator Hide_Internal()
        {
            return Hide();
        }

        internal void SetProgress_Internal(float progress)
        {
            Progress = progress;

            OnProgress(progress);
        }

        private IEnumerator Fade_Internal(float target)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            var start = canvasGroup.alpha;

            // Unscaled time: the game may be paused while loading.
            for (var time = 0f; time < fadeSeconds; time += Time.unscaledDeltaTime)
            {
                canvasGroup.alpha = Mathf.Lerp(start, target, time / fadeSeconds);

                yield return null;
            }

            canvasGroup.alpha = target;
        }
    }
}
