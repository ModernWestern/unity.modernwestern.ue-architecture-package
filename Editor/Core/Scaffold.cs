using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEditor.Presets;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The create-and-wire core every tool uses: build prefabs and variants, add children, fill fields. Each gesture is one
    /// named Undo step: <c>using (var op = Scaffold.Begin("Override Arena Mode for Boss")) { ... }</c>. Every call is
    /// idempotent (running it again reuses what exists), prefab-aware and safe in batch mode (no dialogs, no pings).
    /// </summary>
    internal static class Scaffold
    {
        private static readonly Dictionary<KeyValuePair<Type, string>, KeyValuePair<Type, FieldInfo>> _fields = new();

        private static int _inPlaceDepth;

        private static int _oneStepDepth;

        /// <summary>
        /// Starts one gesture: an Undo group with this name, and a result that collects what was created and changed.
        /// </summary>
        public static ScaffoldOp Begin(string name, string verb = null)
        {
            return new ScaffoldOp(name, verb);
        }

        /// <summary>
        /// Runs several gestures ("Fix all safe", one tag on several Player Starts) as one Undo step named
        /// <paramref name="name"/>.
        /// </summary>
        public static void AsOneUndoStep(string name, Action gestures)
        {
            if (_oneStepDepth > 0)
            {
                gestures?.Invoke();

                return;
            }

            Undo.IncrementCurrentGroup();

            Undo.SetCurrentGroupName(name);

            OneStepGroup_Internal = Undo.GetCurrentGroup();

            _oneStepDepth++;

            try
            {
                gestures?.Invoke();
            }
            finally
            {
                _oneStepDepth--;

                Undo.CollapseUndoOperations(OneStepGroup_Internal);

                Undo.SetCurrentGroupName(name);
            }
        }

        /// <summary>
        /// True inside <see cref="AsOneUndoStep"/>: gestures join its Undo group instead of starting their own.
        /// </summary>
        internal static bool InOneUndoStep_Internal => _oneStepDepth > 0;

        internal static int OneStepGroup_Internal { get; private set; }

        /// <summary>
        /// True inside <see cref="EditPrefabsInPlace"/>: prefab assets are loaded, changed and saved instead of opened in
        /// Prefab Mode, as in batch mode.
        /// </summary>
        public static bool EditsPrefabsInPlace => _inPlaceDepth > 0;

        /// <summary>
        /// While the returned scope is open, edits of prefab assets that are not open in Prefab Mode change the saved file
        /// directly (like batch mode) instead of opening the prefab: batch fixes then never switch the Scene view to Prefab
        /// Mode halfway. Those changes are saved at once and Undo cannot take them back.
        /// </summary>
        public static IDisposable EditPrefabsInPlace()
        {
            _inPlaceDepth++;

            return new InPlaceScope();
        }

        private sealed class InPlaceScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                _inPlaceDepth--;
            }
        }

        /// <summary>
        /// The conventional prefab path of a class from the editor settings, e.g. "Assets/Prefabs/Gameplay/ArenaPawn.prefab".
        /// </summary>
        public static string PrefabPathFor(Type type)
        {
            return GeneratorNaming.PrefabPath(type, UEArchitectureEditorSettings.Current);
        }

        /// <summary>
        /// Where a level's override of a class goes, from the editor settings, e.g. "Assets/Prefabs/Modes/Boss/ArenaGameMode.prefab"
        /// for the scene Assets/Scenes/Boss.unity.
        /// </summary>
        public static string OverridePathFor(string scenePath, string className)
        {
            return GeneratorNaming.OverridePath(Path.GetFileNameWithoutExtension(scenePath ?? string.Empty), className, UEArchitectureEditorSettings.Current);
        }

        /// <summary>
        /// The type of the field a serialized path points to (the element type for array elements), or null.
        /// </summary>
        public static Type FieldType(Type ownerType, string propertyPath)
        {
            return Walk_Internal(ownerType, propertyPath, out _);
        }

        /// <summary>
        /// The C# field a serialized path ends in ("links.Array.data[2]" gives the field "links"), or null.
        /// </summary>
        public static FieldInfo FieldOf(Type ownerType, string propertyPath)
        {
            return Walk_Internal(ownerType, propertyPath, out var field) != null ? field : null;
        }

        /// <summary>
        /// True for a field declared by the framework itself (GameMode.playerController, WorldSettings.gameModeOverride,
        /// Pawn.aiController...): a "class slot" the framework copies at runtime, not a reference to a scene object.
        /// </summary>
        public static bool IsFrameworkField(FieldInfo field)
        {
            return field != null && IsFrameworkType(field.DeclaringType);
        }

        /// <summary>
        /// True for a class of the UE Architecture runtime (GameMode, Pawn, UEArchitectureSettings...), not a user subclass.
        /// </summary>
        public static bool IsFrameworkType(Type type)
        {
            return type != null && type.Assembly == typeof(GameMode).Assembly;
        }

        private static Type Walk_Internal(Type ownerType, string propertyPath, out FieldInfo last)
        {
            // Inspectors ask on every repaint: the answer only changes with a domain reload.
            var key = new KeyValuePair<Type, string>(ownerType, propertyPath ?? string.Empty);

            if (ownerType != null && _fields.TryGetValue(key, out var known))
            {
                last = known.Value;

                return known.Key;
            }

            var found = WalkUncached_Internal(ownerType, propertyPath, out last);

            if (ownerType != null)
            {
                _fields[key] = new KeyValuePair<Type, FieldInfo>(found, last);
            }

            return found;
        }

        private static Type WalkUncached_Internal(Type ownerType, string propertyPath, out FieldInfo last)
        {
            last = null;

            var current = ownerType;

            var parts = (propertyPath ?? string.Empty).Replace(".Array.data[", "[").Split('.');

            foreach (var rawPart in parts)
            {
                if (current == null)
                {
                    return null;
                }

                var part = rawPart;

                var isElement = false;

                var bracket = part.IndexOf('[');

                if (bracket >= 0)
                {
                    part = part.Substring(0, bracket);

                    isElement = true;
                }

                var field = FindField_Internal(current, part);

                if (field == null)
                {
                    return null;
                }

                last = field;

                current = field.FieldType;

                if (isElement)
                {
                    current = current.IsArray ? current.GetElementType() : current.IsGenericType ? current.GetGenericArguments()[0] : null;
                }
            }

            return current;
        }

        /// <summary>
        /// The copy of an object that edits should go to: the one in the open Prefab Mode stage when the object belongs to
        /// the prefab asset open there, otherwise the object itself.
        /// </summary>
        public static Object Writable(Object target)
        {
            if (target == null || !EditorUtility.IsPersistent(target))
            {
                return target;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage == null || stage.prefabContentsRoot == null || stage.assetPath != AssetDatabase.GetAssetPath(target))
            {
                return target;
            }

            return MapBetween_Internal(target, stage.prefabContentsRoot.transform);
        }

        /// <summary>
        /// The prefab asset copy of an object shown in Prefab Mode (the copy that is stored on disk), or the object itself.
        /// </summary>
        public static Object Persistent(Object target)
        {
            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (gameObject == null || EditorUtility.IsPersistent(gameObject))
            {
                return target;
            }

            var stage = PrefabStageUtility.GetPrefabStage(gameObject);

            if (stage == null || string.IsNullOrEmpty(stage.assetPath))
            {
                return target;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);

            return asset == null ? target : MapBetween_Internal(target, asset.transform);
        }

        /// <summary>
        /// "Canvas/Bar" for a child, relative to its root; empty for the root itself.
        /// </summary>
        internal static string RelativePath_Internal(Transform transform)
        {
            var path = string.Empty;

            for (var current = transform; current != null && current.parent != null; current = current.parent)
            {
                path = path.Length == 0 ? current.name : current.name + "/" + path;
            }

            return path;
        }

        /// <summary>
        /// The same object (by place among its siblings and component order) under another copy of its prefab's root, or
        /// the object itself when that copy does not have it.
        /// </summary>
        internal static Object MapBetween_Internal(Object target, Transform otherRoot)
        {
            var transform = target is Component component ? component.transform : ((GameObject)target).transform;

            var indices = new List<int>();

            for (var current = transform; current.parent != null; current = current.parent)
            {
                indices.Insert(0, current.GetSiblingIndex());
            }

            var mapped = otherRoot;

            foreach (var index in indices)
            {
                if (index >= mapped.childCount)
                {
                    return target;
                }

                mapped = mapped.GetChild(index);
            }

            if (mapped.name != transform.name)
            {
                return target;
            }

            if (target is GameObject)
            {
                return mapped.gameObject;
            }

            var type = target.GetType();

            var sameType = transform.GetComponents(type);

            var position = Array.IndexOf(sameType, target);

            var candidates = mapped.GetComponents(type);

            return position >= 0 && position < candidates.Length ? candidates[position] : target;
        }

        private static FieldInfo FindField_Internal(Type type, string name)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Which copy of a prefab-based object an edit goes to (see <see cref="ScaffoldOp.Edit"/>).
    /// </summary>
    internal enum EditIntent
    {
        /// <summary>
        /// The object itself: an object of a level, the copy open in Prefab Mode, a prefab instance (as overrides of that
        /// copy only, labelled so), or a prefab asset.
        /// </summary>
        ThisCopy,

        /// <summary>
        /// The prefab asset an instance is a copy of, so every copy changes. Only when asked for: an instance is never
        /// applied to its prefab silently. For an object that is not a prefab instance it is the object itself.
        /// </summary>
        SourcePrefab
    }

    /// <summary>
    /// One gesture of <see cref="Scaffold"/>: one Undo group, one <see cref="ActionResult"/>. Dispose it to close the
    /// group, save the assets it changed and mark the scenes it touched dirty.
    /// </summary>
    internal sealed class ScaffoldOp : IDisposable
    {
        private readonly HashSet<Object> _dirtyAssets = new();

        private readonly HashSet<Scene> _dirtyScenes = new();

        private readonly int _undoGroup;

        private Object _ping;

        private bool _disposed;

        private string _loadedAssetPath;

        private GameObject _loadedRoot;

        public ScaffoldOp(string name, string verb)
        {
            Name = string.IsNullOrEmpty(name) ? "UE Architecture" : name;

            Result = new ActionResult(verb ?? string.Empty);

            // Inside Scaffold.AsOneUndoStep ("Fix all safe") the gesture is part of that one step.
            if (Scaffold.InOneUndoStep_Internal)
            {
                _undoGroup = Scaffold.OneStepGroup_Internal;

                return;
            }

            Undo.IncrementCurrentGroup();

            Undo.SetCurrentGroupName(Name);

            _undoGroup = Undo.GetCurrentGroup();
        }

        /// <summary>
        /// The Undo step's name, e.g. "Override Arena Mode for Boss".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// What this gesture did so far.
        /// </summary>
        public ActionResult Result { get; }

        /// <summary>
        /// Adds a problem (the gesture continues; the result is not Ok).
        /// </summary>
        public void Problem(string message)
        {
            Result.AddProblem(message);
        }

        /// <summary>
        /// The object to highlight when the gesture ends (never in batch mode).
        /// </summary>
        public void PingWhenDone(Object target)
        {
            _ping = target;
        }

        /// <summary>
        /// False, with a problem, for an object of a level while the Editor is in Play mode: Play mode throws those changes
        /// away when it stops. Assets and the prefab open in Prefab Mode can be changed in Play mode.
        /// </summary>
        public bool CheckEditable(Object target)
        {
            if (target == null || !EditorApplication.isPlayingOrWillChangePlaymode || !EditorCompat.IsLevelObject(target))
            {
                return true;
            }

            Problem($"Leave Play mode first: {Describe_Internal(target)} is an object of the level, and changes made in Play mode are thrown away when it stops.");

            return false;
        }

        /// <summary>
        /// Runs <paramref name="edit"/> on the copy of <paramref name="target"/> that <paramref name="intent"/> names, so
        /// the caller never decides by hand which copy changes:
        /// <list type="bullet">
        /// <item>an object of a level, or of the prefab open in Prefab Mode: the object itself;</item>
        /// <item>a prefab instance: the instance (overrides of that copy only), or with <see cref="EditIntent.SourcePrefab"/>
        /// its prefab asset;</item>
        /// <item>a prefab asset: the copy open in Prefab Mode when it is open there; otherwise, in the Editor, the prefab is
        /// opened in Prefab Mode and its copy is edited (Undo-able, saved like any Prefab Mode change); headless or inside
        /// <see cref="Scaffold.EditPrefabsInPlace"/>, its contents are loaded, edited and saved (EditPrefabContentsScope).</item>
        /// </list>
        /// False, with a problem, when there is nothing to edit or the object cannot change now (Play mode).
        /// </summary>
        public bool Edit(Object target, EditIntent intent, Action<Object> edit)
        {
            if (target == null || edit == null)
            {
                Problem("There is nothing to change.");

                return false;
            }

            if (intent == EditIntent.SourcePrefab && !EditorUtility.IsPersistent(target) && PrefabUtility.IsPartOfPrefabInstance(target))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(target);

                if (source == null)
                {
                    Problem($"{Describe_Internal(target)} was added to this copy, so its prefab does not have it. Apply it to the prefab first.");

                    return false;
                }

                target = source;
            }

            if (!EditorUtility.IsPersistent(target))
            {
                if (!CheckEditable(target))
                {
                    return false;
                }

                edit(target);

                return true;
            }

            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (gameObject == null)
            {
                // Another kind of asset (a ScriptableObject...): edited in place, through SerializedObject and Undo.
                edit(target);

                return true;
            }

            var assetPath = AssetDatabase.GetAssetPath(gameObject);

            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage == null || stage.assetPath != assetPath)
            {
                if (_loadedRoot != null)
                {
                    Problem($"Cannot change {assetPath} while {_loadedAssetPath} is being changed.");

                    return false;
                }

                if (Application.isBatchMode || Scaffold.EditsPrefabsInPlace)
                {
                    return EditLoaded_Internal(target, assetPath, edit);
                }

                AssetDatabase.OpenAsset(gameObject.transform.root.gameObject);

                stage = PrefabStageUtility.GetCurrentPrefabStage();

                if (stage == null || stage.assetPath != assetPath)
                {
                    Problem($"Could not open {assetPath} in Prefab Mode.");

                    return false;
                }

                Result.AddNote($"Opened {assetPath} in Prefab Mode for the change: save the prefab to keep it.");
            }

            edit(Scaffold.Writable(target));

            return true;
        }

        private bool EditLoaded_Internal(Object target, string assetPath, Action<Object> edit)
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(assetPath))
            {
                _loadedAssetPath = assetPath;

                _loadedRoot = scope.prefabContentsRoot;

                try
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                    var mapped = asset != null ? Scaffold.MapBetween_Internal(target, scope.prefabContentsRoot.transform) : null;

                    if (mapped == null || EditorUtility.IsPersistent(mapped))
                    {
                        Problem($"Could not find {Describe_Internal(target)} inside {assetPath}.");

                        return false;
                    }

                    edit(mapped);

                    return true;
                }
                finally
                {
                    _loadedAssetPath = null;

                    _loadedRoot = null;
                }
            }
        }

        /// <summary>
        /// How an object is named in this gesture's results (objects of a prefab loaded for editing are named by the
        /// prefab's path).
        /// </summary>
        private string Describe_Internal(Object target)
        {
            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (_loadedRoot != null && gameObject != null && gameObject.transform.root == _loadedRoot.transform)
            {
                return gameObject.transform == _loadedRoot.transform ? _loadedAssetPath : _loadedAssetPath + "#" + Scaffold.RelativePath_Internal(gameObject.transform);
            }

            return ActionResult.Describe(target);
        }

        private bool UsesUndo_Internal(Object target)
        {
            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            return _loadedRoot == null || gameObject == null || gameObject.transform.root != _loadedRoot.transform;
        }

        /// <summary>
        /// Makes every missing folder of a path under Assets/ ("Assets/Prefabs/Modes/Boss"). False when the path is not
        /// under Assets/.
        /// </summary>
        public bool EnsureFolder(string folder)
        {
            var clean = GeneratorNaming.CleanFolder(folder);

            var problem = GeneratorNaming.FolderProblem(clean);

            if (problem.Length > 0)
            {
                Problem(problem);

                return false;
            }

            if (AssetDatabase.IsValidFolder(clean))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(clean)?.Replace('\\', '/');

            if (!EnsureFolder(parent))
            {
                return false;
            }

            var guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(clean));

            if (string.IsNullOrEmpty(guid))
            {
                Problem($"Could not create the folder {clean}.");

                return false;
            }

            Result.AddCreated(clean);

            return true;
        }

        /// <summary>
        /// The prefab at <paramref name="path"/> whose root has <paramref name="components"/>[0], made if missing: a root
        /// named <paramref name="rootName"/> with the components (default presets applied), then
        /// <paramref name="configure"/>. An existing prefab with the same root class is returned untouched; one with
        /// another root class is reported and left alone. Built in a preview scene, so no open scene is touched.
        /// </summary>
        public GameObject FindOrCreatePrefab(string path, string rootName, Type[] components, Action<GameObject> configure = null)
        {
            if (!CheckPrefabPath_Internal(path))
            {
                return null;
            }

            var rootType = components != null && components.Length > 0 ? components[0] : null;

            if (rootType != null && !CheckComponentType_Internal(rootType))
            {
                return null;
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
            {
                if (rootType == null || existing.GetComponents(rootType).Any(component => component != null && component.GetType() == rootType))
                {
                    Result.AddNote("Reused " + path);

                    Result.Result = existing;

                    return existing;
                }

                Problem($"{path} already exists, but its root has no {rootType.Name}. Pick another name or folder.");

                return null;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                Problem($"{path} exists on disk but is not a prefab Unity has imported. Move it away or refresh the project.");

                return null;
            }

            if (!EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/')))
            {
                return null;
            }

            var scene = EditorSceneManager.NewPreviewScene();

            try
            {
                var root = new GameObject(string.IsNullOrEmpty(rootName) ? Path.GetFileNameWithoutExtension(path) : rootName);

                SceneManager.MoveGameObjectToScene(root, scene);

                foreach (var type in components ?? Type.EmptyTypes)
                {
                    if (!AddComponent_Internal(root, type))
                    {
                        return null;
                    }
                }

                configure?.Invoke(root);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out var success);

                if (!success || saved == null)
                {
                    Problem($"Unity could not save {path}. See the Console for the reason.");

                    return null;
                }

                Result.AddCreated(path);

                Result.Result = saved;

                FrameworkIndex.PrefabSaved_Internal(path);

                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// A prefab variant of <paramref name="basePrefab"/> at <paramref name="path"/> (Unreal's child Blueprint): it
        /// keeps every value of the base until changed. Reused when the asset at the path is already a variant of that
        /// base; another asset there is reported and left alone.
        /// </summary>
        public GameObject CreateVariant(GameObject basePrefab, string path, Action<GameObject> configure = null)
        {
            if (basePrefab == null)
            {
                Problem("There is no prefab to make a variant of.");

                return null;
            }

            if (!EditorUtility.IsPersistent(basePrefab) || basePrefab.transform.parent != null)
            {
                Problem($"{basePrefab.name} is not a prefab asset's root. Variants are made from prefabs in the Project window.");

                return null;
            }

            if (!CheckPrefabPath_Internal(path))
            {
                return null;
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
            {
                if (IsVariantOf(existing, basePrefab))
                {
                    Result.AddNote("Reused " + path);

                    Result.Result = existing;

                    return existing;
                }

                Problem($"{path} already exists and is not a variant of {basePrefab.name}. Pick another name or folder.");

                return null;
            }

            if (AssetDatabase.GetAssetPath(basePrefab) == path)
            {
                Problem("A variant cannot replace its own base prefab.");

                return null;
            }

            if (!EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/')))
            {
                return null;
            }

            var scene = EditorSceneManager.NewPreviewScene();

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, scene);

                if (instance == null)
                {
                    Problem($"Unity could not place {basePrefab.name} to make its variant.");

                    return null;
                }

                configure?.Invoke(instance);

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, path, out var success);

                if (!success || saved == null)
                {
                    Problem($"Unity could not save {path}. See the Console for the reason.");

                    return null;
                }

                Result.AddCreated(path);

                Result.Result = saved;

                FrameworkIndex.PrefabSaved_Internal(path);

                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// True when <paramref name="prefab"/> is a variant whose base is <paramref name="basePrefab"/>.
        /// </summary>
        public static bool IsVariantOf(GameObject prefab, GameObject basePrefab)
        {
            return prefab != null && basePrefab != null &&
                   PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant &&
                   PrefabUtility.GetCorrespondingObjectFromSource(prefab) == basePrefab;
        }

        /// <summary>
        /// The direct child of <paramref name="parent"/> called <paramref name="name"/> with every one of
        /// <paramref name="types"/>, made if missing (Undo-able). A same-named child without them is reported, never
        /// duplicated. Works in scenes and in Prefab Mode; on a prefab instance the child is an override of that copy; on a
        /// prefab asset it goes through <see cref="Edit"/>: in the Editor the prefab opens in Prefab Mode and the child
        /// there is returned; headless the saved file is changed and the asset's child is returned.
        /// </summary>
        public GameObject FindOrCreateChild(GameObject parent, string name, params Type[] types)
        {
            if (parent == null)
            {
                Problem("There is no object to add \"" + name + "\" to.");

                return null;
            }

            if (EditorUtility.IsPersistent(parent))
            {
                return FindOrCreateChildOfAsset_Internal(parent, name, types);
            }

            if (!CheckEditable(parent))
            {
                return null;
            }

            types ??= Type.EmptyTypes;

            foreach (Transform child in parent.transform)
            {
                if (child.name != name)
                {
                    continue;
                }

                var missing = types.Where(type => child.GetComponent(type) == null).Select(type => type.Name).ToArray();

                if (missing.Length == 0)
                {
                    Result.AddNote("Reused " + Describe_Internal(child.gameObject));

                    return child.gameObject;
                }

                Problem($"{Describe_Internal(child.gameObject)} exists but has no {string.Join(", ", missing)}. Add it, or rename that object.");

                return null;
            }

            foreach (var type in types)
            {
                if (!CheckComponentType_Internal(type))
                {
                    return null;
                }
            }

            var undo = UsesUndo_Internal(parent);

            var created = new GameObject(name);

            if (parent.scene.IsValid() && created.scene != parent.scene)
            {
                SceneManager.MoveGameObjectToScene(created, parent.scene);
            }

            if (undo)
            {
                Undo.RegisterCreatedObjectUndo(created, Name);
            }

            GameObjectUtility.SetParentAndAlign(created, parent);

            foreach (var type in types)
            {
                if (created.GetComponent(type) != null)
                {
                    continue;
                }

                var component = undo ? Undo.AddComponent(created, type) : created.AddComponent(type);

                ApplyDefaultPresets_Internal(component);
            }

            var label = Describe_Internal(created);

            Result.AddCreated(PrefabUtility.IsPartOfPrefabInstance(parent) ? label + " (added to this copy of the prefab only)" : label);

            Touch_Internal(created);

            return created;
        }

        private GameObject FindOrCreateChildOfAsset_Internal(GameObject parent, string name, Type[] types)
        {
            var assetPath = AssetDatabase.GetAssetPath(parent);

            var parentPath = Scaffold.RelativePath_Internal(parent.transform);

            GameObject made = null;

            var madePath = string.Empty;

            var edited = Edit(parent, EditIntent.ThisCopy, copy =>
            {
                made = FindOrCreateChild(copy as GameObject, name, types);

                madePath = made != null ? Scaffold.RelativePath_Internal(made.transform) : string.Empty;
            });

            if (!edited || madePath.Length == 0)
            {
                return null;
            }

            // The copy open in Prefab Mode is still there; a copy loaded headless is gone now: use the saved asset's child.
            if (made != null)
            {
                return made;
            }

            FrameworkIndex.PrefabSaved_Internal(assetPath);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            var child = asset != null ? asset.transform.Find(madePath) : null;

            if (child == null)
            {
                Problem($"{assetPath} was saved, but \"{(parentPath.Length == 0 ? name : parentPath + "/" + name)}\" is not in it.");
            }

            return child != null ? child.gameObject : null;
        }

        /// <summary>
        /// A new root object in <paramref name="scene"/> with <paramref name="types"/> (default presets applied), Undo-able.
        /// Refused in preview scenes and for scenes that are not loaded.
        /// </summary>
        public GameObject CreateSceneObject(Scene scene, string name, params Type[] types)
        {
            return CreateSceneObject(scene, name, true, types);
        }

        /// <summary>
        /// <see cref="CreateSceneObject(Scene, string, Type[])"/>, without an Undo record when <paramref name="undo"/> is
        /// false (a level made by the action itself and closed again: there is nothing to undo in it).
        /// </summary>
        public GameObject CreateSceneObject(Scene scene, string name, bool undo, params Type[] types)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
            {
                Problem($"Cannot add \"{name}\": its level is not open.");

                return null;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Problem($"Leave Play mode first: \"{name}\" would be added to the level, and changes made in Play mode are thrown away when it stops.");

                return null;
            }

            types ??= Type.EmptyTypes;

            foreach (var type in types)
            {
                if (!CheckComponentType_Internal(type))
                {
                    return null;
                }
            }

            var created = new GameObject(name);

            if (created.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(created, scene);
            }

            if (undo)
            {
                Undo.RegisterCreatedObjectUndo(created, Name);
            }

            foreach (var type in types)
            {
                if (created.GetComponent(type) != null)
                {
                    continue;
                }

                ApplyDefaultPresets_Internal(undo ? Undo.AddComponent(created, type) : created.AddComponent(type));
            }

            Result.AddCreated(ActionResult.Describe(created));

            Touch_Internal(created);

            return created;
        }

        /// <summary>
        /// Marks an object as changed by this gesture: its asset is saved, or its scene marked dirty, when the gesture ends.
        /// </summary>
        public void Touch(Object target)
        {
            if (target != null)
            {
                Touch_Internal(target);
            }
        }

        /// <summary>
        /// Sets a serialized field through SerializedObject, like the Inspector (Undo, prefab overrides, dirty flags). For a
        /// component-typed field, a GameObject value is replaced by its component of the field's type. Writes to the copy
        /// open in Prefab Mode when the owner's prefab is open there. Does nothing when the value is already set.
        /// </summary>
        public bool Assign(Object owner, string propertyPath, object value)
        {
            if (owner == null)
            {
                Problem($"There is no object to set \"{propertyPath}\" on.");

                return false;
            }

            var target = Scaffold.Writable(owner);

            if (!CheckEditable(target))
            {
                return false;
            }

            var serialized = new SerializedObject(target);

            var property = serialized.FindProperty(propertyPath);

            if (property == null)
            {
                Problem($"{target.GetType().Name} has no field \"{propertyPath}\".");

                return false;
            }

            if (!Set_Internal(target, property, value))
            {
                return false;
            }

            if (!serialized.hasModifiedProperties)
            {
                return true;
            }

            if (UsesUndo_Internal(target))
            {
                serialized.ApplyModifiedProperties();
            }
            else
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var where = Describe_Internal(target);

            if (target != owner)
            {
                where += " (in Prefab Mode: save the prefab to keep it)";
            }
            else if (target is Component component && PrefabUtility.IsPartOfPrefabInstance(component))
            {
                where += " (override on this copy of the prefab)";
            }

            Result.AddChanged($"{where}: {propertyPath}");

            Touch_Internal(target);

            return true;
        }

        /// <summary>
        /// Closes the Undo group, saves the assets that changed, marks the touched scenes dirty and pings the result.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                foreach (var asset in _dirtyAssets)
                {
                    if (asset != null)
                    {
                        // A changed component of a prefab asset makes the whole file need saving.
                        EditorUtility.SetDirty(asset);

                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                }

                // Scenes cannot be marked dirty in Play mode (Unity throws); nothing of a level is changed then anyway.
                if (!EditorApplication.isPlaying)
                {
                    foreach (var scene in _dirtyScenes)
                    {
                        if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene))
                        {
                            EditorSceneManager.MarkSceneDirty(scene);
                        }
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(_undoGroup);
            }

            // The Hierarchy and Project events come on the next Editor update: problems must not be read stale before.
            if (Result.Created.Count + Result.Changed.Count > 0)
            {
                ProblemProviders.Invalidate();
            }

            if (Result.Ok)
            {
                EditorCompat.Ping(_ping != null ? _ping : Result.Result);
            }
        }

        private bool Set_Internal(Object owner, SerializedProperty property, object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return SetReference_Internal(owner, property, value as Object, value);

                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    if (!TryConvert_Internal(value, out long whole))
                    {
                        break;
                    }

                    if (property.longValue != whole)
                    {
                        property.longValue = whole;
                    }

                    return true;

                case SerializedPropertyType.Enum:
                    if (value is string enumName)
                    {
                        var index = Array.FindIndex(property.enumNames, name => string.Equals(name, enumName.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase));

                        if (index < 0)
                        {
                            index = Array.FindIndex(property.enumDisplayNames, name => string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (index < 0)
                        {
                            Problem($"\"{enumName}\" is not one of {string.Join(", ", property.enumDisplayNames)}.");

                            return false;
                        }

                        if (property.enumValueIndex != index)
                        {
                            property.enumValueIndex = index;
                        }

                        return true;
                    }

                    if (!TryConvert_Internal(value, out long enumValue))
                    {
                        break;
                    }

                    if (property.intValue != (int)enumValue)
                    {
                        property.intValue = (int)enumValue;
                    }

                    return true;

                case SerializedPropertyType.Boolean:
                    var isBool = value is bool;

                    var flag = isBool && (bool)value;

                    if (isBool || value is string flagText && bool.TryParse(flagText, out flag))
                    {
                        if (property.boolValue != flag)
                        {
                            property.boolValue = flag;
                        }

                        return true;
                    }

                    break;

                case SerializedPropertyType.Float:
                    if (value is IConvertible && TryConvertFloat_Internal(value, out var number))
                    {
                        if (!Mathf.Approximately(property.floatValue, number))
                        {
                            property.floatValue = number;
                        }

                        return true;
                    }

                    break;

                case SerializedPropertyType.String:
                    var stringValue = value?.ToString() ?? string.Empty;

                    if (property.stringValue != stringValue)
                    {
                        property.stringValue = stringValue;
                    }

                    return true;
            }

            Problem($"Cannot put {(value == null ? "nothing" : $"\"{value}\"")} into {owner.GetType().Name}.{property.propertyPath} ({property.propertyType}).");

            return false;
        }

        private bool SetReference_Internal(Object owner, SerializedProperty property, Object value, object raw)
        {
            if (value == null && raw != null && !(raw is Object))
            {
                Problem($"{owner.GetType().Name}.{property.propertyPath} needs an object, not \"{raw}\".");

                return false;
            }

            var fieldType = Scaffold.FieldType(owner.GetType(), property.propertyPath);

            if (value != null && fieldType != null)
            {
                value = Match_Internal(value, fieldType);

                if (value == null)
                {
                    Problem($"{ActionResult.Describe((Object)raw)} has no {fieldType.Name} for {owner.GetType().Name}.{property.propertyPath}.");

                    return false;
                }
            }

            if (value != null && EditorUtility.IsPersistent(owner) && !EditorUtility.IsPersistent(value))
            {
                Problem($"{ActionResult.Describe(owner)} is an asset, so it cannot point to {ActionResult.Describe(value)} in a scene. Save that object as a prefab and use the prefab.");

                return false;
            }

            if (value != null && owner is Component ownerComponent && !EditorUtility.IsPersistent(owner) && !EditorUtility.IsPersistent(value))
            {
                var valueObject = value is Component valueComponent ? valueComponent.gameObject : value as GameObject;

                if (valueObject != null && valueObject.scene != ownerComponent.gameObject.scene)
                {
                    Problem($"{ActionResult.Describe(value)} is in another scene; fields can only point to objects in their own scene or to assets.");

                    return false;
                }
            }

            if (property.objectReferenceValue != value)
            {
                property.objectReferenceValue = value;
            }

            return true;
        }

        private static Object Match_Internal(Object value, Type fieldType)
        {
            if (fieldType.IsInstanceOfType(value))
            {
                return value;
            }

            var gameObject = value is Component component ? component.gameObject : value as GameObject;

            if (gameObject == null)
            {
                return null;
            }

            if (fieldType == typeof(GameObject))
            {
                return gameObject;
            }

            return typeof(Component).IsAssignableFrom(fieldType) ? gameObject.GetComponent(fieldType) : null;
        }

        private static bool TryConvert_Internal(object value, out long whole)
        {
            whole = 0;

            switch (value)
            {
                case null:
                    return false;

                case string text:
                    return long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out whole);

                case Enum enumValue:
                    whole = Convert.ToInt64(enumValue, System.Globalization.CultureInfo.InvariantCulture);

                    return true;

                case IConvertible convertible:
                    try
                    {
                        whole = convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture);

                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }

                default:
                    return false;
            }
        }

        private static bool TryConvertFloat_Internal(object value, out float number)
        {
            number = 0f;

            if (value is string text)
            {
                return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
            }

            try
            {
                number = ((IConvertible)value).ToSingle(System.Globalization.CultureInfo.InvariantCulture);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool AddComponent_Internal(GameObject root, Type type)
        {
            if (!CheckComponentType_Internal(type))
            {
                return false;
            }

            if (type == typeof(Transform) || root.GetComponent(type) != null && Attribute.IsDefined(type, typeof(DisallowMultipleComponent), true))
            {
                return true;
            }

            var component = root.AddComponent(type);

            if (component == null)
            {
                Problem($"Unity could not add {type.Name}. See the Console for the reason.");

                return false;
            }

            if (component is MonoBehaviour behaviour && MonoScript.FromMonoBehaviour(behaviour) == null)
            {
                Problem($"{type.Name} must be in a file called {type.Name}.cs (Unity finds a component's script by its file name), so it cannot be saved on a prefab.");

                return false;
            }

            ApplyDefaultPresets_Internal(component);

            return true;
        }

        private bool CheckComponentType_Internal(Type type)
        {
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                Problem($"{type?.Name ?? "Nothing"} is not a component.");

                return false;
            }

            if (type.IsAbstract || type.ContainsGenericParameters)
            {
                Problem($"{type.Name} is abstract or generic, so it cannot be added to an object. Pick a class that derives from it.");

                return false;
            }

            return true;
        }

        private bool CheckPrefabPath_Internal(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Problem($"\"{path}\" is not a prefab path (it must end in .prefab).");

                return false;
            }

            var problem = GeneratorNaming.FolderProblem(Path.GetDirectoryName(path)?.Replace('\\', '/'));

            if (problem.Length > 0)
            {
                Problem(problem);

                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies the project's default presets to a component that was just added (Preset Manager), like Add Component.
        /// </summary>
        internal static void ApplyDefaultPresets_Internal(Component component)
        {
            if (component == null)
            {
                return;
            }

            foreach (var preset in Preset.GetDefaultPresetsForObject(component))
            {
                if (preset != null && preset.CanBeAppliedTo(component))
                {
                    preset.ApplyTo(component);
                }
            }
        }

        private void Touch_Internal(Object target)
        {
            if (EditorUtility.IsPersistent(target))
            {
                var main = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(target));

                _dirtyAssets.Add(main != null ? main : target);

                return;
            }

            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (gameObject != null && gameObject.scene.IsValid())
            {
                _dirtyScenes.Add(gameObject.scene);
            }
        }
    }
}
