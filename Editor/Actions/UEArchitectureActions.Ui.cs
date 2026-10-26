using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// A HUD prefab that is a ready Canvas ("New HUD prefab (Canvas ready)"): the HUD on the root, a Screen Space
        /// Overlay Canvas scaled with the screen size (the reference resolution of the editor settings, match 0.5) and a
        /// Graphic Raycaster. Named after the class, in the UI prefab folder, or at <paramref name="path"/>. An existing
        /// prefab is reused (its missing Canvas added). Never an EventSystem: the level holds that. Needs Unity UI.
        /// </summary>
        public static ActionResult CreateHudPrefab(Type hudType = null, string path = null)
        {
            using (var op = Scaffold.Begin($"Create {(hudType ?? typeof(HUD)).Name} prefab (Canvas ready)", "create-hud-prefab"))
            {
                var prefab = CreateUiPrefab_Internal(op, hudType ?? typeof(HUD), typeof(HUD), path, false);

                if (prefab != null)
                {
                    op.Result.Message = op.Result.Created.Count > 0 ? $"Created {AssetDatabase.GetAssetPath(prefab)}, a HUD with its Canvas." : $"{AssetDatabase.GetAssetPath(prefab)} is ready.";
                }

                return op.Result;
            }
        }

        /// <summary>
        /// A working loading screen prefab ("New default loading screen"): a Canvas drawn over the level (sorting order
        /// 100), a Canvas Group at alpha 0 wired to the LoadingScreen so it fades, a background and a Filled progress bar
        /// (fill it in OnProgress of a class of yours). With <paramref name="useInProject"/> it becomes the project's
        /// loading screen (Project Settings > UE Architecture). An existing prefab is reused. Needs Unity UI.
        /// </summary>
        public static ActionResult CreateLoadingScreenPrefab(Type screenType = null, string path = null, bool useInProject = false)
        {
            using (var op = Scaffold.Begin($"Create {(screenType ?? typeof(LoadingScreen)).Name} prefab", "create-loading-screen"))
            {
                var prefab = CreateUiPrefab_Internal(op, screenType ?? typeof(LoadingScreen), typeof(LoadingScreen), path, true);

                if (prefab == null)
                {
                    return op.Result;
                }

                if (useInProject)
                {
                    var settings = EnsureSettings_Internal(op);

                    if (settings != null)
                    {
                        op.Assign(settings, "loadingScreen", prefab);
                    }
                }

                op.Result.Message = op.Result.Created.Count + op.Result.Changed.Count > 0
                                        ? $"{AssetDatabase.GetAssetPath(prefab)} is a working loading screen{(useInProject ? ", used by the project" : string.Empty)}."
                                        : $"{AssetDatabase.GetAssetPath(prefab)} is ready.";

                return op.Result;
            }
        }

        /// <summary>
        /// The '+' picker's "New HUD prefab (Canvas ready)" and "New default loading screen": a new prefab, then the field.
        /// The prefab is named after its class in the UI prefab folder; for a GameMode's HUD, after the GameMode's set
        /// ("ArenaGameMode" gives ArenaHUD.prefab). "New" never reuses a prefab that is there: the name gets a number.
        /// </summary>
        internal static ActionResult CreateUiPrefabAndAssign_Internal(IReadOnlyList<Object> owners, string propertyPath, Type type)
        {
            var loading = typeof(LoadingScreen).IsAssignableFrom(type);

            using (var op = Scaffold.Begin(loading ? "New default loading screen" : "New HUD prefab (Canvas ready)", loading ? "create-loading-screen" : "create-hud-prefab"))
            {
                var path = ReadyPrefabPath_Internal(owners?.FirstOrDefault(owner => owner != null), type);

                var prefab = CreateUiPrefab_Internal(op, type, loading ? typeof(LoadingScreen) : typeof(HUD), path, loading);

                if (prefab != null)
                {
                    AssignClass_Internal(op, owners, propertyPath, null, prefab, null);
                }

                return op.Result;
            }
        }

        /// <summary>
        /// Where the picker's new UI prefab goes: named after its class (LoadingScreen.prefab, ScoreHUD.prefab), or after
        /// the set of the GameMode that owns the field ("ArenaGameMode" gives ArenaHUD.prefab), in the UI prefab folder.
        /// A file already there is never reused: the new one gets the next free name ("ArenaHUD 1.prefab").
        /// </summary>
        internal static string ReadyPrefabPath_Internal(Object owner, Type type)
        {
            if (type == null)
            {
                return null;
            }

            var name = type.Name;

            if (owner is GameMode gameMode)
            {
                var ownerName = CreateFrameworkScripts.ClassName(((Component)Scaffold.Persistent(gameMode)).transform.root.name);

                var set = ownerName == nameof(GameMode) ? string.Empty : ClassTemplates.BaseName(ownerName, nameof(GameMode), "Mode");

                if (set.Length > 0 && !set.EndsWith(type.Name, StringComparison.Ordinal))
                {
                    name = set + type.Name;
                }
            }

            var path = GeneratorNaming.PrefabPath(name, type, UEArchitectureEditorSettings.Current);

            return AssetDatabase.LoadMainAssetAtPath(path) != null || System.IO.File.Exists(path) ? AssetDatabase.GenerateUniqueAssetPath(path) : path;
        }

        internal static GameObject CreateUiPrefab_Internal(ScaffoldOp op, Type type, Type baseType, string path, bool loadingScreen)
        {
            if (type == null || !baseType.IsAssignableFrom(type) || type.IsAbstract)
            {
                op.Problem($"{type?.Name ?? "Nothing"} is not a {ObjectNames.NicifyVariableName(baseType.Name)} class.");

                return null;
            }

            if (!UiBuilders.Available)
            {
                op.Problem(UiBuilders.NeedsUnityUI);

                return null;
            }

            var resolution = UEArchitectureEditorSettings.instance.ReferenceResolution;

            var target = string.IsNullOrWhiteSpace(path) ? Scaffold.PrefabPathFor(type) : GeneratorNaming.CleanFolder(path);

            var prefab = op.FindOrCreatePrefab(target, type.Name, new[] { type }, root =>
            {
                if (loadingScreen)
                {
                    UiBuilders.BuildLoadingScreen(root, resolution, false);
                }
                else
                {
                    UiBuilders.AddScreenCanvas(root, resolution, false);
                }
            });

            if (prefab == null)
            {
                return null;
            }

            // Reused without its Canvas: add the missing parts, never touching what is there.
            if (!op.Result.Created.Contains(target) && UiTypes.CanvasIn(prefab) == null)
            {
                op.Edit(prefab.GetComponent(type), EditIntent.ThisCopy, copy =>
                {
                    var root = ((Component)copy).gameObject;

                    if (loadingScreen)
                    {
                        UiBuilders.BuildLoadingScreen(root, resolution, UsesUndo_Internal(root));
                    }
                    else
                    {
                        UiBuilders.AddScreenCanvas(root, resolution, UsesUndo_Internal(root));
                    }

                    op.Touch(copy);

                    op.Result.AddChanged(ActionResult.Describe(copy) + ": Canvas");
                });
            }

            op.Result.Result = prefab;

            op.PingWhenDone(prefab);

            return prefab;
        }

        /// <summary>
        /// Makes a HUD's object a screen Canvas (Screen Space Overlay, scaled, Graphic Raycaster); parts it has are kept. A
        /// HUD prefab asset is changed in Prefab Mode in the Editor (save it to keep the change), and directly headless or
        /// from "Fix all safe" and fix-all.
        /// </summary>
        public static ActionResult AddHudCanvas(HUD hud)
        {
            using (var op = Scaffold.Begin("Add Canvas to HUD", "add-hud-canvas"))
            {
                if (hud == null)
                {
                    op.Problem("Pick a HUD.");

                    return op.Result;
                }

                if (!UiBuilders.Available)
                {
                    op.Problem(UiBuilders.NeedsUnityUI);

                    return op.Result;
                }

                if (UiTypes.CanvasIn(hud.gameObject) != null)
                {
                    op.Result.Message = $"{hud.gameObject.name} already has a Canvas.";

                    return op.Result;
                }

                op.Edit(hud, EditIntent.ThisCopy, copy =>
                {
                    var root = ((Component)copy).gameObject;

                    UiBuilders.AddScreenCanvas(root, UEArchitectureEditorSettings.instance.ReferenceResolution, UsesUndo_Internal(root));

                    op.Touch(copy);

                    op.Result.AddChanged(ActionResult.Describe(copy) + ": Canvas");
                });

                op.Result.Message = op.Result.Ok ? $"{hud.gameObject.name} is a Canvas now." : op.Result.Message;

                return op.Result;
            }
        }

        /// <summary>
        /// Gives a loading screen the default screen parts it lacks: Canvas (sorting order 100), Canvas Group wired for the
        /// fade, background and progress bar.
        /// </summary>
        public static ActionResult BuildLoadingScreen(LoadingScreen screen)
        {
            using (var op = Scaffold.Begin("Build the default loading screen", "build-loading-screen"))
            {
                if (screen == null)
                {
                    op.Problem("Pick a Loading Screen.");

                    return op.Result;
                }

                if (!UiBuilders.Available)
                {
                    op.Problem(UiBuilders.NeedsUnityUI);

                    return op.Result;
                }

                op.Edit(screen, EditIntent.ThisCopy, copy =>
                {
                    var root = ((Component)copy).gameObject;

                    UiBuilders.BuildLoadingScreen(root, UEArchitectureEditorSettings.instance.ReferenceResolution, UsesUndo_Internal(root));

                    op.Touch(copy);

                    op.Result.AddChanged(ActionResult.Describe(copy) + ": screen");
                });

                return op.Result;
            }
        }

        /// <summary>
        /// Fills a loading screen's empty Canvas Group field with the Canvas Group on its object (added at alpha 0 when
        /// there is none), so it fades instead of popping on and off.
        /// </summary>
        public static ActionResult UseCanvasGroup(LoadingScreen screen)
        {
            using (var op = Scaffold.Begin("Use Canvas Group", "use-canvas-group"))
            {
                if (screen == null || UiTypes.CanvasGroup == null)
                {
                    op.Problem(screen == null ? "Pick a Loading Screen." : UiBuilders.NeedsUnityUI);

                    return op.Result;
                }

                var current = new SerializedObject(screen).FindProperty("canvasGroup");

                if (current != null && current.objectReferenceValue != null)
                {
                    op.Result.Message = $"{screen.gameObject.name} already fades its Canvas Group.";

                    return op.Result;
                }

                op.Edit(screen, EditIntent.ThisCopy, copy =>
                {
                    var target = (Component)copy;

                    var group = target.GetComponent(UiTypes.CanvasGroup);

                    if (group == null)
                    {
                        group = UsesUndo_Internal(target.gameObject) ? Undo.AddComponent(target.gameObject, UiTypes.CanvasGroup) : target.gameObject.AddComponent(UiTypes.CanvasGroup);

                        var serializedGroup = new SerializedObject(group);

                        serializedGroup.FindProperty("m_Alpha").floatValue = 0f;

                        serializedGroup.ApplyModifiedPropertiesWithoutUndo();
                    }

                    op.Assign(target, "canvasGroup", group);
                });

                return op.Result;
            }
        }

        /// <summary>
        /// Sets the sorting order of a loading screen's Canvas, so the level's own UI never draws over it.
        /// </summary>
        public static ActionResult SetSortingOrder(LoadingScreen screen, int order = 100)
        {
            using (var op = Scaffold.Begin($"Set sorting order to {order}", "loading-screen-order"))
            {
                var canvas = screen != null ? UiTypes.CanvasIn(screen.gameObject) : null;

                if (canvas == null)
                {
                    op.Problem("The loading screen has no Canvas.");

                    return op.Result;
                }

                op.Edit(canvas, EditIntent.ThisCopy, copy => op.Assign(copy, "m_SortingOrder", order));

                return op.Result;
            }
        }

        /// <summary>
        /// Fills the empty Component and GameObject fields of a HUD (or any component) with the child named like the field
        /// ("scoreText" takes "Score Text" or "Score", when it has the field's component), like ObserverVM's auto-assign.
        /// Fields that are set are never changed, and fields several children match are listed, not filled. One Undo step.
        /// </summary>
        public static ActionResult FindChildrenByName(Component owner)
        {
            using (var op = Scaffold.Begin("Find children by name", "find-children"))
            {
                if (owner == null)
                {
                    op.Problem("Pick an object.");

                    return op.Result;
                }

                var target = Scaffold.Writable(owner) as Component;

                if (!op.CheckEditable(target))
                {
                    return op.Result;
                }

                var filled = 0;

                foreach (var (path, fieldType) in EmptyChildFields(target))
                {
                    var matches = ChildrenNamedLike(target.transform, Leaf_Internal(path), fieldType);

                    if (matches.Count == 1)
                    {
                        if (op.Assign(target, path, fieldType == typeof(GameObject) ? (Object)matches[0].gameObject : matches[0].GetComponent(fieldType)))
                        {
                            filled++;
                        }
                    }
                    else if (matches.Count > 1)
                    {
                        op.Result.AddNote($"{ObjectNames.NicifyVariableName(Leaf_Internal(path))}: several children fit ({string.Join(", ", matches.Select(match => match.name))}); pick one by hand.");
                    }
                }

                op.Result.Result = owner;

                op.Result.Message = filled == 0 ? "No empty field has a child with its name." : $"Filled {filled} field(s) from the children's names.";

                return op.Result;
            }
        }

        /// <summary>
        /// The empty Component and GameObject fields of a component's own class (not the framework's), as (path, type).
        /// </summary>
        internal static List<(string Path, Type Type)> EmptyChildFields(Component owner)
        {
            var fields = new List<(string, Type)>();

            if (owner == null)
            {
                return fields;
            }

            var serialized = new SerializedObject(owner);

            var iterator = serialized.GetIterator();

            var enter = true;

            while (iterator.NextVisible(enter))
            {
                enter = false;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.propertyPath == "m_Script" || iterator.objectReferenceValue != null)
                {
                    continue;
                }

                var field = Scaffold.FieldOf(owner.GetType(), iterator.propertyPath);

                var type = Scaffold.FieldType(owner.GetType(), iterator.propertyPath);

                if (field == null || Scaffold.IsFrameworkField(field) || type == null || !(type == typeof(GameObject) || typeof(Component).IsAssignableFrom(type)))
                {
                    continue;
                }

                fields.Add((iterator.propertyPath, type));
            }

            return fields;
        }

        /// <summary>
        /// The children (at any depth, inactive included) named like <paramref name="fieldName"/> that have
        /// <paramref name="type"/>: same letters and digits ignoring case, spaces and underscores, or the name plus the
        /// type's name ("scoreText" and "Score" for a Text).
        /// </summary>
        internal static List<Transform> ChildrenNamedLike(Transform root, string fieldName, Type type)
        {
            var found = new List<Transform>();

            var key = Normalize(fieldName);

            var typeName = Normalize(type.Name);

            if (typeName.StartsWith("tmp", StringComparison.Ordinal) && typeName.Length > 3)
            {
                typeName = typeName.Substring(3);
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root || type != typeof(GameObject) && child.GetComponent(type) == null)
                {
                    continue;
                }

                var name = Normalize(child.name);

                if (name.Length > 0 && (name == key || name + typeName == key || key + typeName == name))
                {
                    found.Add(child);
                }
            }

            return found;
        }

        /// <summary>
        /// A name reduced to lower-case letters and digits ("m_Score Text" gives "scoretext"), the rule ObserverVM's
        /// auto-assign uses (duplicated: UE Architecture does not reference ObserverVM).
        /// </summary>
        internal static string Normalize(string value)
        {
            value ??= string.Empty;

            if (value.StartsWith("m_", StringComparison.Ordinal))
            {
                value = value.Substring(2);
            }

            var text = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    text.Append(char.ToLowerInvariant(character));
                }
            }

            return text.ToString();
        }

        private static string Leaf_Internal(string propertyPath)
        {
            var dot = propertyPath.LastIndexOf('.');

            return dot < 0 ? propertyPath : propertyPath.Substring(dot + 1);
        }

        /// <summary>
        /// True for an object whose changes can be undone: in a level or in Prefab Mode, not a prefab whose contents are
        /// loaded to be changed and saved directly (headless, or by a batch fix).
        /// </summary>
        private static bool UsesUndo_Internal(GameObject gameObject)
        {
            return UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null ||
                   gameObject.scene.IsValid() && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene);
        }
    }
}
