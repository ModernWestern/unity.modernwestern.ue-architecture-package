using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// How a <see cref="ClassField"/> behaves where it is shown.
    /// </summary>
    internal sealed class ClassFieldOptions
    {
        /// <summary>
        /// The level the field is shown for (the World Settings panel): "Variant of X" then goes to that level's own
        /// folder ("Variant for this level").
        /// </summary>
        public string SceneName;

        /// <summary>
        /// Returns the object to write to, made when the first value is set (the settings page creates the settings
        /// asset this way). Null: the inspected object.
        /// </summary>
        public Func<Object> Materialize;
    }

    /// <summary>
    /// Unreal's class picker for a framework slot (a GameMode's Player Controller, Default Pawn and HUD, World Settings'
    /// GameMode Override, the project's Game Instance, Default GameMode and Loading Screen, a Pawn's AI Controller, and
    /// users' own fields of those types): the object field plus a "+ ▾" picker with the prefabs, the classes that have no
    /// prefab yet (picking one builds its prefab at once), "New script…", "Variant of &lt;current&gt;", "Open prefab" and
    /// "Open script". Dropping a script builds and assigns its prefab. A slot that holds an object of the level instead
    /// of a prefab is flagged with a one-click fix, and a slot waiting for a script to compile shows a chip.
    /// <para>Used by <see cref="FrameworkClassDrawer"/> for every field of those types; custom editors call
    /// <see cref="Layout"/>.</para>
    /// </summary>
    internal static class ClassField
    {
        private const float PlusWidth = 30f;

        private const float ProblemHeight = 34f;

        private const float ProblemButtonWidth = 150f;

        private const double StepsRefreshSeconds = 0.5;

        private const double PopupLifetimeSeconds = 3.0;

        private static readonly Dictionary<string, AdvancedDropdownState> _dropdownStates = new(StringComparer.Ordinal);

        private static readonly Dictionary<int, string> _ownerKeys = new();

        private static IReadOnlyList<PendingStep> _steps = Array.Empty<PendingStep>();

        private static double _stepsCheckedAt = -1.0;

        private static PopupRequest _popup;

        private static GUIContent _plusContent;

        private static GUIContent _lockedPlusContent;

        private static GUIContent PlusContent => _plusContent ??= new GUIContent(EditorGUIUtility.IconContent("Toolbar Plus More"))
        {
            tooltip = "Pick a prefab, make one from a class (no compile), write a new script, or make a variant of the current prefab."
        };

        private static GUIContent LockedPlusContent => _lockedPlusContent ??= new GUIContent(EditorGUIUtility.IconContent("Toolbar Plus More"))
        {
            tooltip = "Leave Play mode to change this slot: changes to the level made in Play mode are thrown away when it stops."
        };

        /// <summary>
        /// The height of the field, with its chip and problem lines.
        /// </summary>
        public static float GetHeight(SerializedProperty property, Type fieldType = null, ClassFieldOptions options = null)
        {
            return property == null ? EditorGUIUtility.singleLineHeight : Height_Internal(Analyze_Internal(property, fieldType, options));
        }

        /// <summary>
        /// Draws the field in a layout (custom editors and settings pages).
        /// </summary>
        public static void Layout(SerializedProperty property, GUIContent label = null, Type fieldType = null, ClassFieldOptions options = null)
        {
            if (property == null)
            {
                return;
            }

            var state = Analyze_Internal(property, fieldType, options);

            var rect = EditorGUILayout.GetControlRect(true, Height_Internal(state));

            Draw_Internal(rect, property, label ?? new GUIContent(property.displayName, property.tooltip), state, options);
        }

        /// <summary>
        /// Draws the field in a rect (property drawers).
        /// </summary>
        public static void Draw(Rect position, SerializedProperty property, GUIContent label, Type fieldType = null, ClassFieldOptions options = null)
        {
            Draw_Internal(position, property, label, Analyze_Internal(property, fieldType, options), options);
        }

        /// <summary>
        /// True when the '+' picker is offered: not for mixed values, and only on framework class slots or on fields of
        /// prefab assets (a user's field on a level object usually points to an object of that level).
        /// </summary>
        public static bool ShowsPicker(bool frameworkField, bool ownerIsAsset, bool mixedValues)
        {
            return !mixedValues && (frameworkField || ownerIsAsset);
        }

        /// <summary>
        /// True while the '+' picker, drops and fixes are off: in Play mode, for objects of a level (Play mode throws their
        /// changes away when it stops). Assets and the prefab open in Prefab Mode stay editable.
        /// </summary>
        public static bool IsLocked(IReadOnlyList<Object> owners)
        {
            return EditorApplication.isPlayingOrWillChangePlaymode && owners != null && owners.Any(EditorCompat.IsLevelObject);
        }

        /// <summary>
        /// What "None" means in a framework slot, e.g. "None (players start without a Pawn)".
        /// </summary>
        public static string NoneLabel(string propertyPath, bool frameworkField)
        {
            if (!frameworkField)
            {
                return "None";
            }

            switch (propertyPath)
            {
                case "playerController":
                    return "None (a plain Player Controller)";

                case "defaultPawn":
                    return "None (players start without a Pawn)";

                case "hud":
                    return "None (no HUD)";

                case "gameModeOverride":
                    return "None (use the project default)";

                case "gameInstance":
                    return "None (a plain Game Instance)";

                case "defaultGameMode":
                    return "None (no GameMode)";

                case "loadingScreen":
                    return "None (no loading screen)";

                case "aiController":
                    return "None (a plain AI Controller)";

                default:
                    return ClassPickerDropdown.DefaultNoneLabel;
            }
        }

        /// <summary>
        /// Why an object of the level in a class slot is a mistake, in the owner's words.
        /// </summary>
        public static string SceneObjectProblem(Object owner)
        {
            switch (owner)
            {
                case GameMode _:
                    return "The GameMode makes a copy of this when the level begins, and the original stays in the level too. Use a prefab.";

                case WorldSettings _:
                    return "The level makes a copy of this GameMode when it begins, and this one stays in the level too. Use a prefab.";

                case Pawn _:
                    return "The Pawn makes a copy of this AI controller, and the original stays in the level too. Use a prefab.";

                default:
                    return "This slot is copied at runtime, and the original stays in the level too. Use a prefab.";
            }
        }

        private static FieldState Analyze_Internal(SerializedProperty property, Type fieldType, ClassFieldOptions options)
        {
            var owners = property.serializedObject.targetObjects;

            var owner = owners.Length > 0 ? owners[0] : null;

            var ownerType = owner != null ? owner.GetType() : typeof(Object);

            var field = Scaffold.FieldOf(ownerType, property.propertyPath);

            fieldType ??= Scaffold.FieldType(ownerType, property.propertyPath) ?? typeof(Component);

            var frameworkField = Scaffold.IsFrameworkField(field);

            var persistent = owner != null && EditorUtility.IsPersistent(owner);

            var inPrefabMode = owner is Component component && PrefabStageUtility.GetPrefabStage(component.gameObject) != null;

            var mixed = property.hasMultipleDifferentValues;

            var isReference = property.propertyType == SerializedPropertyType.ObjectReference;

            var value = mixed || !isReference ? null : property.objectReferenceValue;

            var state = new FieldState
            {
                Owners = owners,
                Path = property.propertyPath,
                FieldType = fieldType,
                FrameworkField = frameworkField,
                ShowPlus = isReference && typeof(Component).IsAssignableFrom(fieldType) && ShowsPicker(frameworkField, persistent || inPrefabMode || options?.Materialize != null, mixed),
                AllowSceneObjects = !persistent && options?.Materialize == null,
                Locked = IsLocked(owners),
                Value = value,
                Current = value is Component current && EditorUtility.IsPersistent(current) ? current : null,
                Title = ObjectNames.NicifyVariableName(fieldType.Name),
                Level = options?.SceneName ?? GeneratorNaming.SceneOfOverrideFolder(GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(Scaffold.Persistent(owner))), UEArchitectureEditorSettings.Current)
            };

            if (frameworkField && value != null && !EditorUtility.IsPersistent(value))
            {
                state.Problem = SceneObjectProblem(owner);
            }

            state.Step = owner != null ? PendingFor_Internal(owner, property.propertyPath) : null;

            return state;
        }

        private static float Height_Internal(FieldState state)
        {
            var height = EditorGUIUtility.singleLineHeight;

            if (state.Step != null)
            {
                height += EditorGUIUtility.singleLineHeight + 2f;
            }

            if (state.Problem != null)
            {
                height += ProblemHeight + 2f;
            }

            return height;
        }

        private static void Draw_Internal(Rect position, SerializedProperty property, GUIContent label, FieldState state, ClassFieldOptions options)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.LabelField(line, label, new GUIContent($"{property.propertyType} is not an object field."));

                return;
            }

            var fieldRect = state.ShowPlus ? new Rect(line.x, line.y, line.width - PlusWidth - 2f, line.height) : line;

            var plusRect = new Rect(line.xMax - PlusWidth, line.y, PlusWidth, line.height);

            RejectCrossSceneDrop_Internal(fieldRect, property.serializedObject.targetObject);

            if (!state.Locked)
            {
                HandleDrop_Internal(fieldRect, state, options);
            }

            label = EditorGUI.BeginProperty(line, label, property);

            EditorGUI.BeginChangeCheck();

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            var picked = EditorGUI.ObjectField(fieldRect, label, property.objectReferenceValue, state.FieldType, state.AllowSceneObjects);

            EditorGUI.showMixedValue = false;

            // This ObjectField overload has no property, so Unity's own cross-scene check is done here.
            if (EditorGUI.EndChangeCheck() && !RefusesCrossScene(picked, property.serializedObject.targetObject))
            {
                property.objectReferenceValue = picked;
            }

            EditorGUI.EndProperty();

            if (state.ShowPlus)
            {
                using (new EditorGUI.DisabledScope(state.Locked))
                {
                    if (EditorGUI.DropdownButton(plusRect, state.Locked ? LockedPlusContent : PlusContent, FocusType.Passive, EditorStyles.miniButton))
                    {
                        OpenPicker_Internal(plusRect, state, options);
                    }
                }

                ShowRequestedPopup_Internal(plusRect, state);
            }

            var content = new Rect(position.x + EditorGUIUtility.labelWidth, line.yMax + 2f, Mathf.Max(60f, position.width - EditorGUIUtility.labelWidth), line.height);

            if (state.Step != null)
            {
                DrawStep_Internal(content, state.Step);

                content.y += line.height + 2f;
            }

            if (state.Problem != null)
            {
                DrawProblem_Internal(new Rect(content.x, content.y, content.width, ProblemHeight), state, options);
            }
        }

        /// <summary>
        /// True when Unity's default field would refuse <paramref name="picked"/> for <paramref name="owner"/>: both are
        /// objects of different open levels while cross-scene references are prevented
        /// (EditorSceneManager.preventCrossSceneReferences, on by default). Unity cannot save such a reference, so the
        /// field would look set and lose the value.
        /// </summary>
        internal static bool RefusesCrossScene(Object picked, Object owner)
        {
            if (!EditorSceneManager.preventCrossSceneReferences)
            {
                return false;
            }

            var first = GameObjectOf_Internal(picked);

            var second = GameObjectOf_Internal(owner);

            if (first == null || second == null || EditorUtility.IsPersistent(first) || EditorUtility.IsPersistent(second))
            {
                return false;
            }

            return first.scene.IsValid() && second.scene.IsValid() && first.scene != second.scene;
        }

        private static GameObject GameObjectOf_Internal(Object target)
        {
            return target is Component component ? component.gameObject : target as GameObject;
        }

        /// <summary>
        /// Shows a drag from another open level as refused, like Unity's default field.
        /// </summary>
        private static void RejectCrossSceneDrop_Internal(Rect fieldRect, Object owner)
        {
            var evt = Event.current;

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform || !fieldRect.Contains(evt.mousePosition))
            {
                return;
            }

            var dragged = DragAndDrop.objectReferences;

            if (dragged.Length != 1 || !RefusesCrossScene(dragged[0], owner))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;

            evt.Use();
        }

        /// <summary>
        /// "Revert to ArenaGameMode", added to Unity's own right-click menu of an overridden class slot (Apply to Prefab,
        /// Revert, Copy and Paste stay there). Null when the property is not such a slot.
        /// </summary>
        internal static GUIContent RevertLabel(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || !property.prefabOverride || property.serializedObject.isEditingMultipleObjects)
            {
                return null;
            }

            var owner = property.serializedObject.targetObject;

            var fieldType = owner != null ? Scaffold.FieldType(owner.GetType(), property.propertyPath) : null;

            if (fieldType == null || !FrameworkIndex.IndexedTypes.Any(indexed => indexed.IsAssignableFrom(fieldType)))
            {
                return null;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(owner) as Component;

            return new GUIContent($"Revert to {(source != null ? source.transform.root.name : "the prefab")}");
        }

        private static void HandleDrop_Internal(Rect fieldRect, FieldState state, ClassFieldOptions options)
        {
            var evt = Event.current;

            if (!state.ShowPlus || !GUI.enabled || evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform || !fieldRect.Contains(evt.mousePosition))
            {
                return;
            }

            var dragged = DragAndDrop.objectReferences;

            if (dragged.Length != 1)
            {
                return;
            }

            var target = new FieldTarget(state, options);

            if (dragged[0] is MonoScript script)
            {
                var type = script.GetClass();

                if (type == null || type.IsAbstract || type.ContainsGenericParameters || !state.FieldType.IsAssignableFrom(type))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;

                    evt.Use();

                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Link;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    EditorApplication.delayCall += () => Run_Internal(() => UEArchitectureActions.AssignClass_Internal(target.Resolve(), target.Path, type, null, null));
                }

                evt.Use();

                return;
            }

            if (state.AllowSceneObjects || EditorUtility.IsPersistent(dragged[0]))
            {
                // The object field takes it.
                return;
            }

            var sceneObject = dragged[0] is GameObject gameObject ? gameObject.GetComponent(state.FieldType) : dragged[0] as Component;

            if (sceneObject == null || !state.FieldType.IsInstanceOfType(sceneObject))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                var menu = new GenericMenu();

                menu.AddItem(new GUIContent($"Save \"{sceneObject.name}\" as a prefab and use it"), false,
                             () => Run_Internal(() => UEArchitectureActions.UseSceneObject_Internal(target.Resolve(), target.Path, sceneObject, null)));

                menu.ShowAsContext();
            }

            evt.Use();
        }

        private static void OpenPicker_Internal(Rect plusRect, FieldState state, ClassFieldOptions options)
        {
            var target = new FieldTarget(state, options);

            var extras = new List<ClassPick>();

            if (state.Current != null)
            {
                var variantPath = UEArchitectureActions.VariantPathFor_Internal(state.Owners[0], state.Current, options?.SceneName);

                if (variantPath != AssetDatabase.GetAssetPath(state.Current))
                {
                    var forLevel = !string.IsNullOrEmpty(options?.SceneName) ||
                                   GeneratorNaming.SceneOfOverrideFolder(GeneratorNaming.FolderOf(AssetDatabase.GetAssetPath(Scaffold.Persistent(state.Owners[0]))), UEArchitectureEditorSettings.Current) != null;

                    extras.Add(new ClassPick(ClassPickKind.Variant, state.Current.GetType(), state.Current.gameObject,
                                             $"Variant of {state.Current.gameObject.name}{(forLevel ? " for this level" : string.Empty)}"));
                }

                extras.Add(new ClassPick(ClassPickKind.OpenPrefab, state.Current.GetType(), state.Current.gameObject, "Open prefab"));

                if (state.Current is MonoBehaviour behaviour && MonoScript.FromMonoBehaviour(behaviour) != null)
                {
                    extras.Add(new ClassPick(ClassPickKind.OpenScript, state.Current.GetType(), state.Current.gameObject, "Open script"));
                }
            }

            var key = state.FieldType.FullName + "/" + state.Path;

            if (!_dropdownStates.TryGetValue(key, out var dropdownState))
            {
                dropdownState = new AdvancedDropdownState();

                _dropdownStates[key] = dropdownState;
            }

            var dropdown = new ClassPickerDropdown(dropdownState, state.FieldType, state.Title, pick => Picked_Internal(pick, target), NoneLabel(state.Path, state.FrameworkField), extras,
                                                   state.Level, Creators(state.FieldType));

            dropdown.Show(plusRect);
        }

        private static void Picked_Internal(ClassPick pick, FieldTarget target)
        {
            switch (pick.Kind)
            {
                case ClassPickKind.Prefab:
                    Run_Internal(() => UEArchitectureActions.AssignClass_Internal(target.Resolve(), target.Path, null, pick.Prefab, null));

                    break;

                case ClassPickKind.Class:
                    Run_Internal(() => UEArchitectureActions.AssignClass_Internal(target.Resolve(), target.Path, pick.Type, null, null));

                    break;

                case ClassPickKind.None:
                    Run_Internal(() => UEArchitectureActions.AssignClass_Internal(target.Resolve(), target.Path, null, null, null));

                    break;

                case ClassPickKind.Variant:
                    Run_Internal(() => UEArchitectureActions.CreateVariantAndAssign_Internal(target.Resolve(), target.Path, target.SceneName));

                    break;

                case ClassPickKind.OpenPrefab:
                    AssetDatabase.OpenAsset(pick.Prefab);

                    break;

                case ClassPickKind.OpenScript:
                    var behaviour = pick.Prefab != null ? pick.Prefab.GetComponent(pick.Type) as MonoBehaviour : null;

                    var script = behaviour != null ? MonoScript.FromMonoBehaviour(behaviour) : null;

                    if (script != null)
                    {
                        AssetDatabase.OpenAsset(script);
                    }

                    break;

                case ClassPickKind.NewScript:
                    RequestNamePopup_Internal(target);

                    break;

                case ClassPickKind.ReadyPrefab:
                    // Built and assigned now, with no compile to wait for: an unsaved level needs no saving first.
                    Run_Internal(() => UEArchitectureActions.CreateUiPrefabAndAssign_Internal(target.Resolve(), target.Path, pick.Type));

                    break;

                case ClassPickKind.GameModeKit:
                    OpenKit_Internal(target);

                    break;

                case ClassPickKind.ClassWindow:
                    var owner = target.Owners.FirstOrDefault(candidate => candidate != null);

                    NewClassWindow.Open(target.FieldType, owner is UEArchitectureSettings ? null : owner, target.Path,
                                        GeneratorNaming.ScriptFolderFor(ScriptPathOf_Internal(owner), UEArchitectureEditorSettings.Current));

                    break;
            }
        }

        /// <summary>
        /// The picker's ready-made choices for a field's class: "New HUD prefab (Canvas ready)", "New default loading
        /// screen", "New Game Mode set…", and "New class (more options)…" everywhere.
        /// </summary>
        internal static List<ClassPick> Creators(Type fieldType)
        {
            var creators = new List<ClassPick>();

            if (fieldType == null)
            {
                return creators;
            }

            if (typeof(HUD).IsAssignableFrom(fieldType) && !fieldType.IsAbstract && UiBuilders.Available)
            {
                creators.Add(new ClassPick(ClassPickKind.ReadyPrefab, fieldType, null, "New HUD prefab (Canvas ready)"));
            }

            if (typeof(LoadingScreen).IsAssignableFrom(fieldType) && !fieldType.IsAbstract && UiBuilders.Available)
            {
                creators.Add(new ClassPick(ClassPickKind.ReadyPrefab, fieldType, null, "New default loading screen"));
            }

            if (fieldType == typeof(GameMode))
            {
                creators.Add(new ClassPick(ClassPickKind.GameModeKit, fieldType, null, "New Game Mode set…"));
            }

            if (ClassRequestTypes.CanBeParent(fieldType))
            {
                creators.Add(new ClassPick(ClassPickKind.ClassWindow, fieldType, null, "New class (more options)…"));
            }

            return creators;
        }

        /// <summary>
        /// "New Game Mode set…": the kit, used by the field's level (World Settings) or as the project default.
        /// </summary>
        private static void OpenKit_Internal(FieldTarget target)
        {
            var owner = target.Owners.FirstOrDefault(candidate => candidate != null);

            if (owner is WorldSettings worldSettings && worldSettings.gameObject.scene.IsValid())
            {
                GameModeKitWindow.Open(worldSettings.gameObject.scene, KitDestination.ThisLevel);

                return;
            }

            GameModeKitWindow.Open(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
                                   owner is UEArchitectureSettings || owner == null ? KitDestination.ProjectDefault : KitDestination.Nothing);
        }

        private static void RequestNamePopup_Internal(FieldTarget target)
        {
            var conventions = UEArchitectureEditorSettings.Current;

            var owner = target.Owners.FirstOrDefault(candidate => candidate != null);

            var ownerType = owner != null ? owner.GetType() : null;

            var initialName = GeneratorNaming.InitialClassName(ownerType?.Name, ownerType == null || Scaffold.IsFrameworkType(ownerType), PlayerSettings.productName, target.FieldType.Name);

            var folder = GeneratorNaming.ScriptFolderFor(ScriptPathOf_Internal(owner), conventions);

            var popup = NamePopup.ForScript($"New {target.Title} class", initialName, target.FieldType, folder, true, name => Run_Internal(() =>
            {
                var owners = target.Resolve();

                return SaveUntitledScenes_Internal(owners) ?? UEArchitectureActions.NewScript_Internal(name, target.FieldType, folder, true, owners, target.Path);
            }), name => NewScriptCommand(name, target.FieldType, folder, owner, target.Path));

            _popup = new PopupRequest(owner != null ? EditorCompat.SessionKey(owner) : 0, target.Path, popup, EditorApplication.timeSinceStartup);

            InternalEditorUtility.RepaintAllViews();
        }

        private static void ShowRequestedPopup_Internal(Rect plusRect, FieldState state)
        {
            if (_popup == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _popup.Time > PopupLifetimeSeconds)
            {
                _popup = null;

                return;
            }

            if (_popup.OwnerKey != EditorCompat.SessionKey(state.Owners[0]) || _popup.Path != state.Path)
            {
                return;
            }

            var request = _popup;

            _popup = null;

            PopupWindow.Show(plusRect, request.Content);
        }

        /// <summary>
        /// The command line that does what "New script…" does for this field, for "Copy CLI command".
        /// </summary>
        internal static string NewScriptCommand(string className, Type parent, string folder, Object owner, string propertyPath)
        {
            var pairs = new List<KeyValuePair<string, string>>
            {
                new("class", CreateFrameworkScripts.ClassName(className)),
                new("parent", parent?.FullName),
                new("folder", folder)
            };

            var address = owner != null ? UEArchitectureCli.Address(owner) : string.Empty;

            if (address.Length > 0)
            {
                pairs.Add(new KeyValuePair<string, string>("assign-to", address));

                pairs.Add(new KeyValuePair<string, string>("property", propertyPath));

                if (owner is Component)
                {
                    pairs.Add(new KeyValuePair<string, string>("component", owner.GetType().FullName));
                }
            }
            else
            {
                pairs.Add(new KeyValuePair<string, string>("prefab", "true"));
            }

            return UEArchitectureCli.CommandLine("new-script", pairs.ToArray());
        }

        private static void DrawStep_Internal(Rect rect, PendingStep step)
        {
            var blocked = step.status == PendingStatus.Blocked;

            var buttons = blocked ? 2 : 1;

            var chipRect = new Rect(rect.x, rect.y, rect.width - buttons * 84f, rect.height);

            var text = blocked ? "Blocked: " + step.message : step.Describe();

            UEArchitectureStyles.DrawChip(chipRect, text, blocked ? UEArchitectureStyles.Error : UEArchitectureStyles.Warning);

            GUI.Label(chipRect, new GUIContent(string.Empty, text + "\nThe field fills itself once the script compiles."));

            if (!blocked)
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUI.Button(new Rect(chipRect.xMax + 4f, rect.y, 80f, rect.height),
                                   new GUIContent("Finish now", "Run the waiting steps now, e.g. once their level is open. They also run by themselves after a compile."),
                                   EditorStyles.miniButton))
                    {
                        EditorApplication.delayCall += () => Run_Internal(UEArchitectureActions.CompletePending);

                        _stepsCheckedAt = -1.0;
                    }
                }

                return;
            }

            var script = step.scriptPaths.Length > 0 ? AssetDatabase.LoadAssetAtPath<MonoScript>(step.scriptPaths[0]) : null;

            using (new EditorGUI.DisabledScope(script == null))
            {
                if (GUI.Button(new Rect(chipRect.xMax + 4f, rect.y, 80f, rect.height), "Open script", EditorStyles.miniButton))
                {
                    AssetDatabase.OpenAsset(script);
                }
            }

            if (GUI.Button(new Rect(chipRect.xMax + 88f, rect.y, 80f, rect.height), new GUIContent("Cancel", "Forget this step. The script stays."), EditorStyles.miniButton))
            {
                var id = step.id;

                EditorApplication.delayCall += () => Run_Internal(() => UEArchitectureActions.CancelPending(id));

                _stepsCheckedAt = -1.0;
            }
        }

        private static void DrawProblem_Internal(Rect rect, FieldState state, ClassFieldOptions options)
        {
            var gameObject = state.Value is Component component ? component.gameObject : state.Value as GameObject;

            var usesPrefab = gameObject != null && PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject);

            var showButton = GUI.enabled && !state.Locked && state.Owners.Length == 1;

            var helpRect = showButton ? new Rect(rect.x, rect.y, rect.width - ProblemButtonWidth - 4f, rect.height) : rect;

            EditorGUI.HelpBox(helpRect, state.Problem, MessageType.Error);

            if (!showButton)
            {
                return;
            }

            var buttonRect = new Rect(rect.xMax - ProblemButtonWidth, rect.y + (rect.height - 20f) * 0.5f, ProblemButtonWidth, 20f);

            if (GUI.Button(buttonRect, usesPrefab ? new GUIContent("Use its prefab", "Put the prefab this object is a copy of in the slot.") : new GUIContent("Save as prefab and use", "Save this object as a prefab (it stays in the level as a copy of it) and put the prefab in the slot.")))
            {
                var target = new FieldTarget(state, options);

                var value = state.Value;

                EditorApplication.delayCall += () => Run_Internal(() => UEArchitectureActions.UseSceneObject_Internal(target.Resolve(), target.Path, value, null));
            }
        }

        private static PendingStep PendingFor_Internal(Object owner, string propertyPath)
        {
            var now = EditorApplication.timeSinceStartup;

            if (_stepsCheckedAt < 0.0 || now - _stepsCheckedAt > StepsRefreshSeconds)
            {
                var steps = PendingSteps.Store.Snapshot();

                if (!ReferenceEquals(steps, _steps))
                {
                    _ownerKeys.Clear();
                }

                _steps = steps;

                _stepsCheckedAt = now;
            }

            if (_steps.Count == 0)
            {
                return null;
            }

            var id = EditorCompat.SessionKey(owner);

            if (!_ownerKeys.TryGetValue(id, out var key))
            {
                key = EditorCompat.ObjectKey(Scaffold.Persistent(owner));

                _ownerKeys[id] = key;
            }

            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (var step in _steps)
            {
                if (step.kind != UEArchitectureActions.PrefabForScriptKind)
                {
                    continue;
                }

                foreach (var target in step.targets)
                {
                    if (target.objectKey == key && target.propertyPath == propertyPath)
                    {
                        return step;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The field is filled after a compile, which finds its object again by its saved file: a level that was never
        /// saved is saved first (Unity asks where). Objects of a prefab open in Prefab Mode are found by the prefab asset,
        /// so their preview scene is never saved. Null when every owner can be found again, otherwise why not.
        /// </summary>
        internal static ActionResult SaveUntitledScenes_Internal(IReadOnlyList<Object> owners)
        {
            foreach (var owner in owners ?? Array.Empty<Object>())
            {
                var persistent = Scaffold.Persistent(owner);

                if (persistent == null || !EditorCompat.IsInUnsavedScene(persistent))
                {
                    continue;
                }

                var scene = persistent is Component component ? component.gameObject.scene : ((GameObject)persistent).scene;

                if (Application.isBatchMode || EditorSceneManager.IsPreviewScene(scene) || !EditorSceneManager.SaveScene(scene) || string.IsNullOrEmpty(scene.path))
                {
                    return ActionResult.Fail("new-script", "Save the level first: the new class's prefab goes into this field after the compile, which finds the field by its saved level.");
                }
            }

            return null;
        }

        private static string ScriptPathOf_Internal(Object owner)
        {
            MonoScript script = null;

            if (owner is MonoBehaviour behaviour)
            {
                script = MonoScript.FromMonoBehaviour(behaviour);
            }
            else if (owner is ScriptableObject scriptable)
            {
                script = MonoScript.FromScriptableObject(scriptable);
            }

            return script != null ? AssetDatabase.GetAssetPath(script) : string.Empty;
        }

        /// <summary>
        /// Runs an action from the UI: problems go to the Console (the field shows the rest).
        /// </summary>
        internal static void Run_Internal(Func<ActionResult> action)
        {
            ActionResult result;

            try
            {
                result = action();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                return;
            }

            _stepsCheckedAt = -1.0;

            if (result != null && !result.Ok)
            {
                Debug.LogWarning("UE Architecture: " + string.Join("\n", result.Problems));
            }
            else if (result != null && result.Created.Any(path => !path.Contains("#")))
            {
                LastCreatedFiles.Remember(result);

                Debug.Log($"UE Architecture: {result} (Undo does not remove files: Assets > UE Architecture > Remove Files Made by the Last Action does.)");
            }

            InternalEditorUtility.RepaintAllViews();
        }

        private sealed class FieldState
        {
            public Object[] Owners;

            public string Path;

            public Type FieldType;

            public bool FrameworkField;

            public bool ShowPlus;

            public bool AllowSceneObjects;

            public bool Locked;

            public Object Value;

            public Component Current;

            public string Problem;

            public PendingStep Step;

            public string Title;

            public string Level;
        }

        /// <summary>
        /// What a picker callback writes to, captured when the picker opens (the SerializedProperty is gone by then).
        /// </summary>
        private sealed class FieldTarget
        {
            private readonly Func<Object> _materialize;

            public FieldTarget(FieldState state, ClassFieldOptions options)
            {
                Owners = state.Owners.ToArray();

                Path = state.Path;

                FieldType = state.FieldType;

                Title = state.Title;

                SceneName = options?.SceneName;

                _materialize = options?.Materialize;
            }

            public Object[] Owners { get; }

            public string Path { get; }

            public Type FieldType { get; }

            public string Title { get; }

            public string SceneName { get; }

            /// <summary>
            /// The objects to write to now (the settings asset is created at this point when it was missing).
            /// </summary>
            public Object[] Resolve()
            {
                if (_materialize == null)
                {
                    return Owners;
                }

                var made = _materialize();

                return made != null ? new[] { made } : Array.Empty<Object>();
            }
        }

        private sealed class PopupRequest
        {
            public PopupRequest(int ownerKey, string path, PopupWindowContent content, double time)
            {
                OwnerKey = ownerKey;

                Path = path;

                Content = content;

                Time = time;
            }

            public int OwnerKey { get; }

            public string Path { get; }

            public PopupWindowContent Content { get; }

            public double Time { get; }
        }
    }

    /// <summary>
    /// The <see cref="ClassField"/> on every field of a framework class type (GameMode, PlayerController, Pawn, HUD,
    /// AIController, GameInstance, LoadingScreen and their subclasses), in every inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(GameMode), true)]
    [CustomPropertyDrawer(typeof(PlayerController), true)]
    [CustomPropertyDrawer(typeof(AIController), true)]
    [CustomPropertyDrawer(typeof(Pawn), true)]
    [CustomPropertyDrawer(typeof(HUD), true)]
    [CustomPropertyDrawer(typeof(GameInstance), true)]
    [CustomPropertyDrawer(typeof(LoadingScreen), true)]
    internal sealed class FrameworkClassDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return ClassField.GetHeight(property, ElementType(fieldInfo?.FieldType));
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ClassField.Draw(position, property, label, ElementType(fieldInfo?.FieldType));
        }

        /// <summary>
        /// The type one element of a field holds: the element of an array or a List&lt;T&gt; (the only collections Unity
        /// serializes), otherwise the field's own type, generic or not (a VehiclePawn&lt;CarConfig&gt; field holds a
        /// VehiclePawn&lt;CarConfig&gt;).
        /// </summary>
        internal static Type ElementType(Type fieldType)
        {
            if (fieldType == null)
            {
                return null;
            }

            if (fieldType.IsArray)
            {
                return fieldType.GetElementType();
            }

            return fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) ? fieldType.GetGenericArguments()[0] : fieldType;
        }
    }

    /// <summary>
    /// Adds "Revert to ArenaGameMode" to Unity's own right-click menu of an overridden class slot, so Apply to Prefab,
    /// Revert, Copy and Paste stay where they are.
    /// </summary>
    [InitializeOnLoad]
    internal static class ClassFieldContextMenu
    {
        static ClassFieldContextMenu()
        {
            EditorApplication.contextualPropertyMenu -= OnPropertyMenu_Internal;

            EditorApplication.contextualPropertyMenu += OnPropertyMenu_Internal;
        }

        private static void OnPropertyMenu_Internal(GenericMenu menu, SerializedProperty property)
        {
            var label = ClassField.RevertLabel(property);

            if (label == null)
            {
                return;
            }

            var owner = property.serializedObject.targetObject;

            var path = property.propertyPath;

            if (ClassField.IsLocked(new[] { owner }))
            {
                menu.AddDisabledItem(label);

                return;
            }

            menu.AddItem(label, false, () => ClassField.Run_Internal(() => UEArchitectureActions.RevertOverride(owner, path)));
        }
    }
}
