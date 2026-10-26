using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The Unity UI side of the HUD and loading screen tools. This assembly compiles only with the Unity UI package
    /// (com.unity.ugui) installed; it registers its builders in <see cref="UiBuilders"/> when the Editor loads. Never
    /// creates an EventSystem: a HUD prefab is spawned once per player, and the level holds the EventSystem.
    /// </summary>
    internal static class UguiBuilders
    {
        private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.08f, 1f);

        [InitializeOnLoadMethod]
        private static void Register_Internal()
        {
            UiBuilders.Register(AddScreenCanvas, BuildLoadingScreen);
        }

        /// <summary>
        /// A Screen Space Overlay Canvas on <paramref name="root"/>, scaled with the screen size (match 0.5), with a Graphic
        /// Raycaster. Components it already has are kept as they are.
        /// </summary>
        internal static void AddScreenCanvas(GameObject root, Vector2Int resolution, bool undo)
        {
            if (root.GetComponent<Canvas>() == null)
            {
                var canvas = Add_Internal<Canvas>(root, undo);

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            if (root.GetComponent<CanvasScaler>() == null)
            {
                var scaler = Add_Internal<CanvasScaler>(root, undo);

                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

                scaler.referenceResolution = new Vector2(resolution.x, resolution.y);

                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

                scaler.matchWidthOrHeight = 0.5f;
            }

            if (root.GetComponent<GraphicRaycaster>() == null)
            {
                Add_Internal<GraphicRaycaster>(root, undo);
            }
        }

        /// <summary>
        /// A loading screen under a LoadingScreen object: a Canvas drawn over the level (sorting order 100), a Canvas Group
        /// at alpha 0 that the LoadingScreen fades, a full-screen background and a Filled progress bar ("Progress Bar": set
        /// its fillAmount in OnProgress of a class of yours). Parts that exist are kept.
        /// </summary>
        internal static void BuildLoadingScreen(GameObject root, Vector2Int resolution, bool undo)
        {
            var hadCanvas = root.GetComponent<Canvas>() != null;

            AddScreenCanvas(root, resolution, undo);

            if (!hadCanvas)
            {
                root.GetComponent<Canvas>().sortingOrder = 100;
            }

            var group = root.GetComponent<CanvasGroup>();

            if (group == null)
            {
                group = Add_Internal<CanvasGroup>(root, undo);

                group.alpha = 0f;
            }

            var loadingScreen = root.GetComponent<LoadingScreen>();

            if (loadingScreen != null)
            {
                var serialized = new SerializedObject(loadingScreen);

                var field = serialized.FindProperty("canvasGroup");

                if (field != null && field.objectReferenceValue == null)
                {
                    field.objectReferenceValue = group;

                    if (undo)
                    {
                        serialized.ApplyModifiedProperties();
                    }
                    else
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            if (root.transform.Find("Background") == null)
            {
                var background = Child_Internal(root, "Background", undo);

                Stretch_Internal(background);

                var image = Add_Internal<Image>(background.gameObject, undo);

                image.color = BackgroundColor;
            }

            if (root.transform.Find("Progress Bar") == null)
            {
                var bar = Child_Internal(root, "Progress Bar", undo);

                bar.anchorMin = new Vector2(0.5f, 0f);

                bar.anchorMax = new Vector2(0.5f, 0f);

                bar.pivot = new Vector2(0.5f, 0f);

                bar.sizeDelta = new Vector2(resolution.x * 0.6f, 12f);

                bar.anchoredPosition = new Vector2(0f, resolution.y * 0.1f);

                var image = Add_Internal<Image>(bar.gameObject, undo);

                // A Filled image needs a sprite to fill; without one it draws whole.
                image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

                image.type = Image.Type.Filled;

                image.fillMethod = Image.FillMethod.Horizontal;

                image.fillOrigin = (int)Image.OriginHorizontal.Left;

                image.fillAmount = 0f;

                image.raycastTarget = false;
            }
        }

        private static RectTransform Child_Internal(GameObject root, string name, bool undo)
        {
            var child = new GameObject(name, typeof(RectTransform));

            // Parenting moves it into the root's scene (a preview scene, or the prefab open in Prefab Mode). Registered
            // for Undo only then: registered while still in the active level, it would mark that level as changed.
            child.transform.SetParent(root.transform, false);

            child.layer = root.layer;

            if (undo)
            {
                Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            }

            return (RectTransform)child.transform;
        }

        private static void Stretch_Internal(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;

            rect.anchorMax = Vector2.one;

            rect.offsetMin = Vector2.zero;

            rect.offsetMax = Vector2.zero;
        }

        private static T Add_Internal<T>(GameObject gameObject, bool undo) where T : Component
        {
            var component = undo ? Undo.AddComponent<T>(gameObject) : gameObject.AddComponent<T>();

            ScaffoldOp.ApplyDefaultPresets_Internal(component);

            return component;
        }
    }
}
