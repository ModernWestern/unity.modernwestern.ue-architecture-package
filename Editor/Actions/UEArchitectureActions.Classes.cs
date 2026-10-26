using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Fills a class slot (a field such as GameMode.playerController, WorldSettings.gameModeOverride or
        /// Pawn.aiController), like Unreal's class pickers: with <paramref name="prefab"/> when given; else with the prefab
        /// of <paramref name="type"/>, made at once when the class has none (no compile, named and placed as set in
        /// Project Settings > UE Architecture > Editor); with neither, the field is cleared. One Undo step; a second run
        /// creates nothing.
        /// </summary>
        public static ActionResult AssignClass(Object owner, string propertyPath, Type type = null, GameObject prefab = null)
        {
            return AssignClass_Internal(new[] { owner }, propertyPath, type, prefab, null);
        }

        /// <summary>
        /// Makes a prefab variant of the prefab a class slot holds now (Unreal's child Blueprint) and puts it in the slot.
        /// With a level (<paramref name="sceneName"/>, or when the owner is a level's own override) the variant goes to that
        /// level's override folder, e.g. Assets/Prefabs/Modes/Boss/ArenaPawn.prefab; otherwise next to the current prefab
        /// as "&lt;Name&gt;Variant.prefab". A second run reuses the variant.
        /// </summary>
        public static ActionResult CreateVariantAndAssign(Object owner, string propertyPath, string sceneName = null)
        {
            return CreateVariantAndAssign_Internal(new[] { owner }, propertyPath, sceneName);
        }

        /// <summary>
        /// For a class slot that holds an object of the level instead of a prefab (the framework copies the slot at
        /// runtime, so the original would stay in the level too): saves that object as a prefab (or uses the prefab it is
        /// a copy of) and puts the prefab in the slot.
        /// </summary>
        public static ActionResult SaveAsPrefabAndAssign(Object owner, string propertyPath, string prefabPath = null)
        {
            var current = CurrentValue_Internal(owner, propertyPath);

            return UseSceneObject_Internal(new[] { owner }, propertyPath, current, prefabPath);
        }

        /// <summary>
        /// Puts a prefab override back to the value of the prefab it comes from (a level's variant row back to the shared
        /// GameMode's value). Undo-able.
        /// </summary>
        public static ActionResult RevertOverride(Object owner, string propertyPath)
        {
            using (var op = Scaffold.Begin($"Revert {ObjectNames.NicifyVariableName(propertyPath ?? string.Empty)}", "revert-override"))
            {
                if (owner == null)
                {
                    op.Problem("There is no object to revert.");

                    return op.Result;
                }

                var target = Scaffold.Writable(owner);

                if (!op.CheckEditable(target))
                {
                    return op.Result;
                }

                var serialized = new SerializedObject(target);

                var property = serialized.FindProperty(propertyPath ?? string.Empty);

                if (property == null)
                {
                    op.Problem($"{target.GetType().Name} has no field \"{propertyPath}\".");

                    return op.Result;
                }

                if (!property.prefabOverride)
                {
                    op.Result.Message = $"{ObjectNames.NicifyVariableName(propertyPath)} already has the prefab's value.";

                    return op.Result;
                }

                PrefabUtility.RevertPropertyOverride(property, Application.isBatchMode ? InteractionMode.AutomatedAction : InteractionMode.UserAction);

                op.Touch(target);

                op.Result.AddChanged($"{ActionResult.Describe(target)}: {propertyPath} (reverted)");

                op.Result.Message = $"{ObjectNames.NicifyVariableName(propertyPath)} is back to the prefab's value.";

                return op.Result;
            }
        }

        /// <summary>
        /// Fills a class slot of the project's settings (gameInstance, loadingScreen or defaultGameMode) like
        /// <see cref="AssignClass"/>, creating Assets/Resources/UEArchitectureSettings.asset first when the project has none
        /// ("Create Prefab and Use as > Project Game Instance"). One Undo step; a second run creates nothing.
        /// </summary>
        public static ActionResult AssignProjectClass(string propertyPath, Type type = null, GameObject prefab = null)
        {
            var what = prefab != null ? prefab.name : type != null ? type.Name : "None";

            using (var op = Scaffold.Begin($"Use {what} in Project Settings", "assign-project-class"))
            {
                if (Scaffold.FieldType(typeof(UEArchitectureSettings), propertyPath ?? string.Empty) == null)
                {
                    op.Problem($"The project settings have no field \"{propertyPath}\". Use gameInstance, loadingScreen or defaultGameMode.");

                    return op.Result;
                }

                var settings = EnsureSettings_Internal(op);

                if (settings != null)
                {
                    AssignClass_Internal(op, new Object[] { settings }, propertyPath, type, prefab, null);
                }

                return op.Result;
            }
        }

        internal static ActionResult AssignClass_Internal(IReadOnlyList<Object> owners, string propertyPath, Type type, GameObject prefab, string prefabPath)
        {
            var what = prefab != null ? prefab.name : type != null ? type.Name : "None";

            using (var op = Scaffold.Begin($"Use {what} as {ObjectNames.NicifyVariableName(propertyPath ?? string.Empty)}", "assign-class"))
            {
                AssignClass_Internal(op, owners, propertyPath, type, prefab, prefabPath);

                return op.Result;
            }
        }

        internal static bool AssignClass_Internal(ScaffoldOp op, IReadOnlyList<Object> owners, string propertyPath, Type type, GameObject prefab, string prefabPath)
        {
            var targets = Owners_Internal(op, owners, propertyPath, out var fieldType);

            if (targets == null)
            {
                return false;
            }

            Object value = null;

            if (prefab != null)
            {
                if (!EditorUtility.IsPersistent(prefab))
                {
                    op.Problem($"{prefab.name} is not a prefab asset. Save it as a prefab first (or use \"Save as prefab and use\").");

                    return false;
                }

                var component = prefab.GetComponent(fieldType);

                if (component == null || type != null && !type.IsInstanceOfType(component))
                {
                    op.Problem($"{AssetDatabase.GetAssetPath(prefab)} has no {(type ?? fieldType).Name} on its root.");

                    return false;
                }

                value = component;
            }
            else if (type != null)
            {
                if (!fieldType.IsAssignableFrom(type))
                {
                    op.Problem($"{type.Name} is not a {fieldType.Name}, so it cannot go into {ObjectNames.NicifyVariableName(propertyPath)}.");

                    return false;
                }

                var made = CreateClassPrefab_Internal(op, type, prefabPath);

                if (made == null)
                {
                    return false;
                }

                value = made.GetComponent(fieldType);
            }

            var changedBefore = op.Result.Changed.Count;

            var ok = true;

            foreach (var owner in targets)
            {
                ok &= op.Assign(owner, propertyPath, value);
            }

            if (!ok)
            {
                return false;
            }

            var slot = $"{ObjectNames.NicifyVariableName(propertyPath)} of {(targets.Length == 1 ? OwnerName_Internal(targets[0]) : $"{targets.Length} objects")}";

            var file = value != null ? Path.GetFileName(AssetDatabase.GetAssetPath(value)) : string.Empty;

            var changed = op.Result.Changed.Count > changedBefore;

            var created = op.Result.Created.Any(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) ? $"Created {file}. " : string.Empty;

            op.Result.Message = value == null
                                    ? changed ? $"Cleared {slot}." : $"{slot} was already empty."
                                    : created + (changed ? $"{slot} now uses {file}." : $"{slot} already uses {file}.");

            if (value != null)
            {
                op.Result.Result = value;

                op.PingWhenDone(((Component)value).gameObject);
            }

            return true;
        }

        internal static ActionResult CreateVariantAndAssign_Internal(IReadOnlyList<Object> owners, string propertyPath, string sceneName)
        {
            var first = owners?.FirstOrDefault(owner => owner != null);

            var current = CurrentValue_Internal(first, propertyPath) as Component;

            using (var op = Scaffold.Begin($"Variant of {(current != null ? current.gameObject.name : "prefab")}", "create-variant"))
            {
                var targets = Owners_Internal(op, owners, propertyPath, out _);

                if (targets == null)
                {
                    return op.Result;
                }

                if (current == null || !EditorUtility.IsPersistent(current))
                {
                    op.Problem($"Put a prefab in {ObjectNames.NicifyVariableName(propertyPath)} first: a variant is made from the prefab it holds.");

                    return op.Result;
                }

                var path = VariantPathFor_Internal(first, current, sceneName);

                if (path == AssetDatabase.GetAssetPath(current))
                {
                    op.Problem($"{path} is already this level's own copy: change it directly.");

                    return op.Result;
                }

                var variant = op.CreateVariant(current.transform.root.gameObject, path);

                if (variant == null)
                {
                    return op.Result;
                }

                var component = variant.GetComponentsInChildren(current.GetType(), true).FirstOrDefault();

                foreach (var owner in targets)
                {
                    op.Assign(owner, propertyPath, component);
                }

                op.Result.Result = component;

                op.PingWhenDone(variant);

                op.Result.Message = op.Result.Created.Contains(path) ? $"Created {path}, a variant of {current.gameObject.name}." : $"Using the existing variant {path}.";

                return op.Result;
            }
        }

        /// <summary>
        /// Where "Variant of &lt;current&gt;" goes for this owner (see <see cref="GeneratorNaming.VariantPath"/>).
        /// </summary>
        internal static string VariantPathFor_Internal(Object owner, Component current, string sceneName)
        {
            var conventions = UEArchitectureEditorSettings.Current;

            var ownerAsset = owner != null ? AssetDatabase.GetAssetPath(Scaffold.Persistent(owner)) : string.Empty;

            return GeneratorNaming.VariantPath(AssetDatabase.GetAssetPath(current), current.gameObject.name, current.GetType().Name, ownerAsset, sceneName,
                                               GeneratorNaming.PrefabFolder(current.GetType(), conventions), conventions);
        }

        internal static ActionResult UseSceneObject_Internal(IReadOnlyList<Object> owners, string propertyPath, Object sceneObject, string prefabPath)
        {
            using (var op = Scaffold.Begin($"Save {(sceneObject != null ? sceneObject.name : "object")} as a prefab", "save-as-prefab"))
            {
                var targets = Owners_Internal(op, owners, propertyPath, out var fieldType);

                if (targets == null)
                {
                    return op.Result;
                }

                var component = sceneObject is GameObject asObject ? asObject.GetComponent(fieldType) : sceneObject as Component;

                if (component == null || !fieldType.IsInstanceOfType(component))
                {
                    op.Problem($"{(sceneObject != null ? sceneObject.name : "Nothing")} has no {fieldType.Name} to use in {ObjectNames.NicifyVariableName(propertyPath)}.");

                    return op.Result;
                }

                if (EditorUtility.IsPersistent(component))
                {
                    op.Result.AddNote($"{ActionResult.Describe(component)} is already a prefab.");

                    AssignClass_Internal(op, targets, propertyPath, null, component.transform.root.gameObject, null);

                    return op.Result;
                }

                if (!op.CheckEditable(component))
                {
                    return op.Result;
                }

                var gameObject = component.gameObject;

                Component prefabComponent;

                if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
                {
                    if (!PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
                    {
                        op.Problem($"{gameObject.name} is inside a copy of another prefab. Open that prefab and save {gameObject.name} as a prefab of its own.");

                        return op.Result;
                    }

                    prefabComponent = PrefabUtility.GetCorrespondingObjectFromSource(component);

                    if (prefabComponent == null)
                    {
                        op.Problem($"{gameObject.name}'s {fieldType.Name} was added in the level, not in its prefab. Apply it to the prefab first.");

                        return op.Result;
                    }

                    op.Result.AddNote($"Used the prefab {gameObject.name} is a copy of ({AssetDatabase.GetAssetPath(prefabComponent)}); changes made only on the copy in the level are not part of it.");
                }
                else
                {
                    var explicitPath = !string.IsNullOrEmpty(prefabPath);

                    var path = explicitPath ? GeneratorNaming.CleanFolder(prefabPath) : Scaffold.PrefabPathFor(component.GetType());

                    if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        op.Problem($"\"{path}\" is not a prefab path (it must end in .prefab).");

                        return op.Result;
                    }

                    var existing = explicitPath ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;

                    if (existing != null)
                    {
                        // An existing prefab is never overwritten: it is used when it is a prefab of the same class.
                        prefabComponent = existing.GetComponent(component.GetType());

                        if (prefabComponent == null || prefabComponent.GetType() != component.GetType())
                        {
                            op.Problem($"{path} already exists and its root has no {component.GetType().Name}. Pick another path: an existing prefab is never overwritten.");

                            return op.Result;
                        }

                        op.Result.AddNote($"Used the existing prefab {path}; {gameObject.name} stays in the level as it is (an existing prefab is never overwritten).");
                    }
                    else
                    {
                        if (explicitPath && File.Exists(path))
                        {
                            op.Problem($"{path} exists on disk but is not a prefab Unity has imported. Pick another path.");

                            return op.Result;
                        }

                        if (!op.EnsureFolder(GeneratorNaming.FolderOf(path)))
                        {
                            return op.Result;
                        }

                        if (!explicitPath)
                        {
                            // A new prefab next to the ones of other classes: never over an existing file.
                            path = AssetDatabase.GenerateUniqueAssetPath(path);
                        }

                        var saved = PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, path, Application.isBatchMode ? InteractionMode.AutomatedAction : InteractionMode.UserAction,
                                                                              out var success);

                        if (!success || saved == null)
                        {
                            op.Problem($"Unity could not save {path}. See the Console for the reason.");

                            return op.Result;
                        }

                        op.Result.AddCreated(path);

                        FrameworkIndex.PrefabSaved_Internal(path);

                        prefabComponent = saved.GetComponent(component.GetType());

                        op.Result.AddNote($"{gameObject.name} stays in the level, now as a copy of {path}. Delete it if only the framework should make one.");
                    }
                }

                foreach (var owner in targets)
                {
                    op.Assign(owner, propertyPath, prefabComponent);
                }

                op.Result.Result = prefabComponent;

                op.PingWhenDone(prefabComponent != null ? prefabComponent.gameObject : null);

                op.Result.Message = $"{ObjectNames.NicifyVariableName(propertyPath)} now uses {AssetDatabase.GetAssetPath(prefabComponent)}.";

                return op.Result;
            }
        }

        /// <summary>
        /// How an owner is named in messages: "ArenaGameMode" (a prefab or an object of a level), "ArenaGameMode (project
        /// default)" for the default GameMode, or "Project Settings".
        /// </summary>
        internal static string OwnerName_Internal(Object owner)
        {
            if (owner == null)
            {
                return "nothing";
            }

            if (owner is UEArchitectureSettings)
            {
                return "Project Settings";
            }

            var name = owner is Component component ? (EditorUtility.IsPersistent(component) ? component.transform.root.name : component.gameObject.name) : owner.name;

            var settings = FrameworkIndex.Settings;

            return owner is GameMode gameMode && settings != null && settings.DefaultGameMode == gameMode ? name + " (project default)" : name;
        }

        private static Object CurrentValue_Internal(Object owner, string propertyPath)
        {
            if (owner == null || string.IsNullOrEmpty(propertyPath))
            {
                return null;
            }

            var property = new SerializedObject(Scaffold.Writable(owner)).FindProperty(propertyPath);

            return property != null && property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : null;
        }

        private static Object[] Owners_Internal(ScaffoldOp op, IReadOnlyList<Object> owners, string propertyPath, out Type fieldType)
        {
            fieldType = null;

            var targets = owners?.Where(owner => owner != null).Distinct().ToArray() ?? Array.Empty<Object>();

            if (targets.Length == 0)
            {
                op.Problem("There is no object to fill.");

                return null;
            }

            fieldType = Scaffold.FieldType(targets[0].GetType(), propertyPath);

            if (fieldType == null || new SerializedObject(targets[0]).FindProperty(propertyPath ?? string.Empty) == null)
            {
                op.Problem($"{targets[0].GetType().Name} has no field \"{propertyPath}\".");

                return null;
            }

            if (!typeof(Component).IsAssignableFrom(fieldType))
            {
                op.Problem($"{targets[0].GetType().Name}.{propertyPath} does not hold a component ({fieldType.Name}).");

                return null;
            }

            // Refused before anything is made: objects of a level cannot keep changes made in Play mode.
            foreach (var target in targets)
            {
                if (!op.CheckEditable(Scaffold.Writable(target)))
                {
                    return null;
                }
            }

            return targets;
        }
    }
}
