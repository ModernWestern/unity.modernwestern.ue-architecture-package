using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor.ProjectWindowCallback;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Every editor API that moved between Unity 2021.3 and 6000.x, in one place. New editor code calls these instead of
    /// the Unity API directly, so a Unity upgrade is fixed here once.
    /// </summary>
    internal static class EditorCompat
    {
        /// <summary>
        /// Starts the Project window's "type a name" field for a new asset (or creates it at once with the default name
        /// when no Project window is open), then calls <paramref name="action"/> with the chosen path.
        /// </summary>
        public static void StartNameEditing(NameOnCreateAction action, string defaultName, Texture2D icon, string resourceFile = null)
        {
#if UNITY_6000_4_OR_NEWER
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, action, defaultName, icon, resourceFile);
#else
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, action, defaultName, icon, resourceFile);
#endif
        }

        /// <summary>
        /// A key that names an object across domain reloads and Editor restarts (a GlobalObjectId string), or an empty
        /// string when the object has none: null, or in a scene that was never saved.
        /// </summary>
        public static string ObjectKey(Object target)
        {
            if (target == null || IsInUnsavedScene(target))
            {
                return string.Empty;
            }

            var id = GlobalObjectId.GetGlobalObjectIdSlow(target);

            return id.identifierType == 0 ? string.Empty : id.ToString();
        }

        /// <summary>
        /// The object an <see cref="ObjectKey"/> names, or null when it is gone or its scene is not loaded.
        /// </summary>
        public static Object FromObjectKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !GlobalObjectId.TryParse(key, out var id))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
        }

        /// <summary>
        /// The asset GUID inside an <see cref="ObjectKey"/> (for a scene object, its scene's GUID), or empty.
        /// </summary>
        public static string AssetGuidOfObjectKey(string key)
        {
            return !string.IsNullOrEmpty(key) && GlobalObjectId.TryParse(key, out var id) ? id.assetGUID.ToString() : string.Empty;
        }

        /// <summary>
        /// True when the key names an object inside a scene (not an asset).
        /// </summary>
        public static bool IsSceneObjectKey(string key)
        {
            return !string.IsNullOrEmpty(key) && GlobalObjectId.TryParse(key, out var id) && id.identifierType == 2;
        }

        /// <summary>
        /// A key for an object that only lives while the Editor runs (Play mode rows, foldout state). Never stored.
        /// </summary>
        public static int SessionKey(object target)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target);
        }

        /// <summary>
        /// Highlights an object in the Project or Hierarchy window. Does nothing in batch mode.
        /// </summary>
        public static void Ping(Object target)
        {
            if (target == null || Application.isBatchMode)
            {
                return;
            }

            EditorGUIUtility.PingObject(target);
        }

        /// <summary>
        /// Every loaded object of a type in the open scenes, in no particular order.
        /// </summary>
        public static T[] FindAll<T>(bool includeInactive = false) where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            // Unity 6000.4 deprecated the sort mode, as instance ids give way to EntityId.
            return Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>(includeInactive);
#endif
        }

        /// <summary>
        /// True for a GameObject or component in a loaded scene that has never been saved (it has no file yet). Objects
        /// of a prefab open in Prefab Mode (or of any preview scene) are not: their file is the prefab asset.
        /// </summary>
        public static bool IsInUnsavedScene(Object target)
        {
            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (gameObject == null || EditorUtility.IsPersistent(gameObject))
            {
                return false;
            }

            var scene = gameObject.scene;

            return scene.IsValid() && string.IsNullOrEmpty(scene.path) && !EditorSceneManager.IsPreviewScene(scene) && PrefabStageUtility.GetPrefabStage(gameObject) == null;
        }

        /// <summary>
        /// True for an object of a level (a scene that is not a preview scene such as Prefab Mode), as opposed to an asset
        /// or a prefab being edited. Play mode throws these changes away when it stops.
        /// </summary>
        public static bool IsLevelObject(Object target)
        {
            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (gameObject == null || EditorUtility.IsPersistent(gameObject))
            {
                return false;
            }

            var scene = gameObject.scene;

            return scene.IsValid() && !EditorSceneManager.IsPreviewScene(scene);
        }

        /// <summary>
        /// Adds the UI input module that fits the project to an EventSystem object: Unity 6's factory, otherwise the
        /// Input System's InputSystemUIInputModule when the new Input System is active, else StandaloneInputModule. Found by
        /// type name, so neither the Unity UI package nor the Input System is a hard dependency. Returns the module (an
        /// existing one is kept), or null when no module type is available.
        /// </summary>
        public static Component AddInputModule(GameObject eventSystem)
        {
            if (eventSystem == null)
            {
                return null;
            }

            var baseModule = FindType_Internal("UnityEngine.EventSystems.BaseInputModule", "UnityEngine.UI");

            if (baseModule != null && eventSystem.GetComponent(baseModule) != null)
            {
                return eventSystem.GetComponent(baseModule);
            }

#if UNITY_6000_0_OR_NEWER
            var factory = FindType_Internal("UnityEditor.EventSystems.InputModuleComponentFactory", "UnityEditor.UI");

            var add = factory?.GetMethod("AddInputModule", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(GameObject) }, null);

            if (add != null)
            {
                var added = add.Invoke(null, new object[] { eventSystem }) as Component;

                if (added != null)
                {
                    return added;
                }
            }
#endif
            var moduleType = UsesInputSystem_Internal()
                                 ? FindType_Internal("UnityEngine.InputSystem.UI.InputSystemUIInputModule", "Unity.InputSystem")
                                 : FindType_Internal("UnityEngine.EventSystems.StandaloneInputModule", "UnityEngine.UI");

            moduleType ??= FindType_Internal("UnityEngine.EventSystems.StandaloneInputModule", "UnityEngine.UI");

            return moduleType != null ? Undo.AddComponent(eventSystem, moduleType) : null;
        }

        private static bool UsesInputSystem_Internal()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return true;
#else
            return false;
#endif
        }

        private static System.Type FindType_Internal(string fullName, string assemblyName)
        {
            var type = System.Type.GetType($"{fullName}, {assemblyName}", false);

            if (type != null)
            {
                return type;
            }

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
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

    /// <summary>
    /// An IMGUI tree view that compiles on every supported Unity: Unity 6.2 made TreeView generic over its item id
    /// (TreeView&lt;int&gt;) and deprecated the plain one. Subclasses list their rows in <see cref="AddRows"/> by depth.
    /// </summary>
#if UNITY_6000_2_OR_NEWER
    internal abstract class CompatTreeView : UnityEditor.IMGUI.Controls.TreeView<int>
    {
        protected CompatTreeView() : base(new UnityEditor.IMGUI.Controls.TreeViewState<int>())
        {
        }

        protected sealed override UnityEditor.IMGUI.Controls.TreeViewItem<int> BuildRoot()
        {
            var root = new UnityEditor.IMGUI.Controls.TreeViewItem<int>(0, -1, "Root");

            var rows = new System.Collections.Generic.List<UnityEditor.IMGUI.Controls.TreeViewItem<int>>();

            AddRows((id, depth, name) => rows.Add(new UnityEditor.IMGUI.Controls.TreeViewItem<int>(id, depth, name)));

            SetupParentsAndChildrenFromDepths(root, rows);

            return root;
        }
#else
    internal abstract class CompatTreeView : UnityEditor.IMGUI.Controls.TreeView
    {
        protected CompatTreeView() : base(new UnityEditor.IMGUI.Controls.TreeViewState())
        {
        }

        protected sealed override UnityEditor.IMGUI.Controls.TreeViewItem BuildRoot()
        {
            var root = new UnityEditor.IMGUI.Controls.TreeViewItem(0, -1, "Root");

            var rows = new System.Collections.Generic.List<UnityEditor.IMGUI.Controls.TreeViewItem>();

            AddRows((id, depth, name) => rows.Add(new UnityEditor.IMGUI.Controls.TreeViewItem(id, depth, name)));

            SetupParentsAndChildrenFromDepths(root, rows);

            return root;
        }
#endif

        /// <summary>
        /// How many rows are shown now (after <c>Reload()</c>).
        /// </summary>
        public int RowCount => GetRows().Count;

        /// <summary>
        /// The ids of the selected rows.
        /// </summary>
        public System.Collections.Generic.IList<int> SelectedIds => GetSelection();

        /// <summary>
        /// Lists the rows, depth first: <c>add(id, depth, label)</c> with ids above 0 and depth 0 for the top rows.
        /// </summary>
        protected abstract void AddRows(System.Action<int, int, string> add);
    }

#if UNITY_6000_4_OR_NEWER
    /// <summary>
    /// What happens once the user has typed a new asset's name in the Project window. Unity 6.4 replaced
    /// EndNameEditAction (int instance ids) with AssetCreationEndAction (EntityId); 6.5 made the old one a compile error.
    /// </summary>
    internal abstract class NameOnCreateAction : AssetCreationEndAction
    {
        public sealed override void Action(EntityId entityId, string pathName, string resourceFile)
        {
            OnNamed(pathName, resourceFile);
        }

        /// <summary>
        /// Called with the path the user typed (relative to the project, e.g. "Assets/Game/ArenaPawn.cs").
        /// </summary>
        protected abstract void OnNamed(string pathName, string resourceFile);
    }
#else
    /// <summary>
    /// What happens once the user has typed a new asset's name in the Project window. Unity 6.4 replaced
    /// EndNameEditAction (int instance ids) with AssetCreationEndAction (EntityId); 6.5 made the old one a compile error.
    /// </summary>
    internal abstract class NameOnCreateAction : EndNameEditAction
    {
        public sealed override void Action(int instanceId, string pathName, string resourceFile)
        {
            OnNamed(pathName, resourceFile);
        }

        /// <summary>
        /// Called with the path the user typed (relative to the project, e.g. "Assets/Game/ArenaPawn.cs").
        /// </summary>
        protected abstract void OnNamed(string pathName, string resourceFile);
    }
#endif
}
