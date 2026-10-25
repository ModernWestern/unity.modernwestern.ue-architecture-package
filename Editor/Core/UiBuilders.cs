using System;
using UnityEditor;
using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Builds the Unity UI parts of HUD and loading screen prefabs. The builders live in the UEArchitecture.Editor.UGUI
    /// assembly, which compiles only when the Unity UI package (com.unity.ugui) is installed, and register here when the
    /// Editor loads. Without that package <see cref="Available"/> is false and the buttons that need it say so.
    /// </summary>
    internal static class UiBuilders
    {
        /// <summary>
        /// What the tools say when the Unity UI package is missing.
        /// </summary>
        public const string NeedsUnityUI = "Needs the Unity UI package (com.unity.ugui): install it in the Package Manager.";

        private static Action<GameObject, Vector2Int, bool> _screenCanvas;

        private static Action<GameObject, Vector2Int, bool> _loadingScreen;

        /// <summary>
        /// True when the Unity UI builders are installed.
        /// </summary>
        public static bool Available => _screenCanvas != null && _loadingScreen != null;

        /// <summary>
        /// Called by the UEArchitecture.Editor.UGUI assembly. <paramref name="screenCanvas"/> adds a Screen Space Overlay
        /// Canvas, a Canvas Scaler (Scale With Screen Size) and a Graphic Raycaster to an object; <paramref name="loadingScreen"/>
        /// builds a loading screen under a LoadingScreen object. Both get the reference resolution and whether to record Undo.
        /// </summary>
        public static void Register(Action<GameObject, Vector2Int, bool> screenCanvas, Action<GameObject, Vector2Int, bool> loadingScreen)
        {
            _screenCanvas = screenCanvas;

            _loadingScreen = loadingScreen;
        }

        /// <summary>
        /// Makes <paramref name="root"/> a screen Canvas (Screen Space Overlay, scaled for <paramref name="resolution"/>,
        /// match 0.5, Graphic Raycaster). Parts it already has are kept. False when Unity UI is not installed.
        /// </summary>
        public static bool AddScreenCanvas(GameObject root, Vector2Int resolution, bool undo)
        {
            if (_screenCanvas == null || root == null)
            {
                return false;
            }

            _screenCanvas(root, resolution, undo);

            return true;
        }

        /// <summary>
        /// Gives a LoadingScreen object a working screen: a Canvas drawn over everything (sorting order 100), a Canvas Group
        /// it fades (alpha 0, set in the LoadingScreen), a background and a Filled progress bar. False when Unity UI is not
        /// installed.
        /// </summary>
        public static bool BuildLoadingScreen(GameObject root, Vector2Int resolution, bool undo)
        {
            if (_loadingScreen == null || root == null)
            {
                return false;
            }

            _loadingScreen(root, resolution, undo);

            return true;
        }
    }

    /// <summary>
    /// The UI classes the tools check for, found by name, so the main editor assembly has no reference to Unity UI.
    /// </summary>
    internal static class UiTypes
    {
        private static Type _canvas;

        private static Type _canvasGroup;

        private static Type _eventSystem;

        private static Type _graphicRaycaster;

        private static Type _uiDocument;

        private static bool _found;

        public static Type Canvas
        {
            get
            {
                Find_Internal();

                return _canvas;
            }
        }

        public static Type CanvasGroup
        {
            get
            {
                Find_Internal();

                return _canvasGroup;
            }
        }

        public static Type EventSystem
        {
            get
            {
                Find_Internal();

                return _eventSystem;
            }
        }

        public static Type GraphicRaycaster
        {
            get
            {
                Find_Internal();

                return _graphicRaycaster;
            }
        }

        /// <summary>
        /// UI Toolkit's UIDocument (built in since 2021.2).
        /// </summary>
        public static Type UIDocument
        {
            get
            {
                Find_Internal();

                return _uiDocument;
            }
        }

        /// <summary>
        /// True when <paramref name="root"/> or an object below it draws UI: a Canvas, or a UI Toolkit UIDocument.
        /// </summary>
        public static bool DrawsUi(GameObject root)
        {
            return CanvasIn(root) != null || root != null && UIDocument != null && root.GetComponentInChildren(UIDocument, true) != null;
        }

        /// <summary>
        /// The object's component of a class found by name, or null (also when the class is not installed).
        /// </summary>
        public static Component Get(GameObject gameObject, Type type)
        {
            return gameObject != null && type != null ? gameObject.GetComponent(type) : null;
        }

        /// <summary>
        /// The Canvas on <paramref name="root"/> or its nearest one below it, or null.
        /// </summary>
        public static Component CanvasIn(GameObject root)
        {
            if (root == null || Canvas == null)
            {
                return null;
            }

            var own = root.GetComponent(Canvas);

            return own != null ? own : root.GetComponentInChildren(Canvas, true);
        }

        /// <summary>
        /// A Canvas's sorting order, read through its serialized data (no reference to the UI module).
        /// </summary>
        public static int SortingOrder(Component canvas)
        {
            if (canvas == null)
            {
                return 0;
            }

            var property = new SerializedObject(canvas).FindProperty("m_SortingOrder");

            return property != null ? property.intValue : 0;
        }

        private static void Find_Internal()
        {
            if (_found)
            {
                return;
            }

            _found = true;

            _canvas = Find_Internal("UnityEngine.Canvas", "UnityEngine.UIModule");

            _canvasGroup = Find_Internal("UnityEngine.CanvasGroup", "UnityEngine.UIModule");

            _eventSystem = Find_Internal("UnityEngine.EventSystems.EventSystem", "UnityEngine.UI");

            _graphicRaycaster = Find_Internal("UnityEngine.UI.GraphicRaycaster", "UnityEngine.UI");

            _uiDocument = Find_Internal("UnityEngine.UIElements.UIDocument", "UnityEngine.UIElementsModule");
        }

        private static Type Find_Internal(string fullName, string assemblyName)
        {
            var type = Type.GetType($"{fullName}, {assemblyName}", false);

            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, false);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
