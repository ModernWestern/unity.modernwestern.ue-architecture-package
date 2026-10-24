using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// A field of a component that points to an object of the level (a Component or GameObject field, or one element of
    /// an array of them): "wagonController", "links[2]".
    /// </summary>
    internal sealed class ReferenceSlot
    {
        public ReferenceSlot(string propertyPath, string fieldName, Type fieldType, Object current, string arrayPath = null)
        {
            PropertyPath = propertyPath;

            FieldName = fieldName;

            FieldType = fieldType;

            Current = current;

            ArrayPath = arrayPath;
        }

        public string PropertyPath { get; }

        /// <summary>
        /// The C# field name ("wagonController"; for an array element, the array's name).
        /// </summary>
        public string FieldName { get; }

        public Type FieldType { get; }

        public Object Current { get; }

        /// <summary>
        /// The array the slot is an element of, or null.
        /// </summary>
        public string ArrayPath { get; }

        public bool IsEmpty => Current == null;

        /// <summary>
        /// "Wagon Controller", or "Links [2]" for an array element.
        /// </summary>
        public string Label
        {
            get
            {
                if (ArrayPath == null)
                {
                    return ObjectNames.NicifyVariableName(FieldName);
                }

                var open = PropertyPath.LastIndexOf('[');

                return ObjectNames.NicifyVariableName(FieldName) + " " + PropertyPath.Substring(open);
            }
        }
    }

    /// <summary>
    /// An object of the level a field could point to: a GameObject, or one of its components.
    /// </summary>
    internal sealed class ReferenceCandidate
    {
        public ReferenceCandidate(Object value, Type type, string name, string hierarchyPath)
        {
            Value = value;

            Type = type;

            Name = name ?? string.Empty;

            HierarchyPath = hierarchyPath ?? Name;
        }

        public Object Value { get; }

        /// <summary>
        /// typeof(GameObject), or the component's class.
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// The GameObject's name.
        /// </summary>
        public string Name { get; }

        public string HierarchyPath { get; }

        /// <summary>
        /// "WagonController (Train/Wagon)" for menus.
        /// </summary>
        public string MenuLabel => Type == typeof(GameObject) ? HierarchyPath : $"{Type.Name} ({HierarchyPath})";
    }

    /// <summary>
    /// What filling does with one field.
    /// </summary>
    internal enum FillOutcome
    {
        /// <summary>
        /// Exactly one object fits: it is assigned.
        /// </summary>
        Assign,

        /// <summary>
        /// Several objects fit: left for the user to pick.
        /// </summary>
        Ambiguous,

        /// <summary>
        /// Nothing in the level fits.
        /// </summary>
        None,

        /// <summary>
        /// The field is already set: never overwritten.
        /// </summary>
        Kept
    }

    /// <summary>
    /// The plan for one field.
    /// </summary>
    internal sealed class FillRow
    {
        public FillRow(ReferenceSlot slot, FillOutcome outcome, ReferenceCandidate pick, IReadOnlyList<ReferenceCandidate> matches, string reason)
        {
            Slot = slot;

            Outcome = outcome;

            Pick = pick;

            Matches = matches ?? Array.Empty<ReferenceCandidate>();

            Reason = reason ?? string.Empty;
        }

        public ReferenceSlot Slot { get; }

        public FillOutcome Outcome { get; internal set; }

        public ReferenceCandidate Pick { get; internal set; }

        public IReadOnlyList<ReferenceCandidate> Matches { get; }

        public string Reason { get; internal set; }

        /// <summary>
        /// "wagonController: Assign WagonController (Train/Wagon), the only one", for notes and the command line.
        /// </summary>
        public override string ToString()
        {
            switch (Outcome)
            {
                case FillOutcome.Assign:
                    return $"{Slot.PropertyPath}: {Pick.MenuLabel} ({Reason})";

                case FillOutcome.Ambiguous:
                    return $"{Slot.PropertyPath}: pick one of {Matches.Count} ({string.Join(", ", Matches.Take(5).Select(match => match.MenuLabel))}{(Matches.Count > 5 ? ", ..." : string.Empty)}){(Reason.Length > 0 ? " - " + Reason : string.Empty)}";

                case FillOutcome.Kept:
                    return $"{Slot.PropertyPath}: kept {(Slot.Current != null ? Slot.Current.name : "its value")}";

                default:
                    return $"{Slot.PropertyPath}: nothing in the level is a {Slot.FieldType.Name}";
            }
        }
    }

    /// <summary>
    /// Fills the level references of a component (a GameMode placed in a level with fields for its wagon, timer, UI...)
    /// from its own level: <see cref="Plan"/> is pure and decides, per empty field, the one object that fits (by type,
    /// with the field's name as a tie-break), or that several fit. Fields that are set are never overwritten, and
    /// several matches are never guessed.
    /// </summary>
    internal static class SceneReferenceFill
    {
        /// <summary>
        /// The plan for <paramref name="slots"/> with the objects of the level, <paramref name="candidates"/>.
        /// </summary>
        public static List<FillRow> Plan(IReadOnlyList<ReferenceSlot> slots, IReadOnlyList<ReferenceCandidate> candidates)
        {
            var rows = new List<FillRow>();

            slots ??= Array.Empty<ReferenceSlot>();

            candidates ??= Array.Empty<ReferenceCandidate>();

            var inUse = new HashSet<Object>(slots.Where(slot => slot.Current != null).Select(slot => slot.Current));

            foreach (var slot in slots)
            {
                if (!slot.IsEmpty)
                {
                    rows.Add(new FillRow(slot, FillOutcome.Kept, null, null, "already set"));

                    continue;
                }

                var matches = Matches(slot, candidates);

                if (slot.ArrayPath != null)
                {
                    var siblings = new HashSet<Object>(slots.Where(other => other.ArrayPath == slot.ArrayPath && other.Current != null).Select(other => other.Current));

                    var free = matches.Where(match => match.Value == null || !siblings.Contains(match.Value)).ToList();

                    rows.Add(free.Count == 1 ? new FillRow(slot, FillOutcome.Assign, free[0], free, "the only one left")
                             : free.Count == 0 ? new FillRow(slot, FillOutcome.None, null, matches, string.Empty)
                             : new FillRow(slot, FillOutcome.Ambiguous, null, free, "list elements are never guessed"));

                    continue;
                }

                var key = Normalize(slot.FieldName);

                var byName = matches.Where(match => Normalize(match.Name) == key).ToList();

                if (slot.FieldType == typeof(GameObject))
                {
                    // GameObject fields match by a unique name only: every object is a GameObject.
                    rows.Add(byName.Count == 1 ? new FillRow(slot, FillOutcome.Assign, byName[0], byName, "same name")
                             : byName.Count > 1 ? new FillRow(slot, FillOutcome.Ambiguous, null, byName, "several objects have that name")
                             : new FillRow(slot, FillOutcome.None, null, matches, string.Empty));

                    continue;
                }

                if (byName.Count == 1)
                {
                    rows.Add(new FillRow(slot, FillOutcome.Assign, byName[0], matches, "same name"));
                }
                else if (matches.Count == 1)
                {
                    rows.Add(new FillRow(slot, FillOutcome.Assign, matches[0], matches, "the only one"));
                }
                else if (matches.Count == 0)
                {
                    rows.Add(new FillRow(slot, FillOutcome.None, null, matches, string.Empty));
                }
                else
                {
                    rows.Add(new FillRow(slot, FillOutcome.Ambiguous, null, matches, string.Empty));
                }
            }

            // An object picked only by its type is a guess when another field already points to it (or also wants it).
            var picks = rows.Where(row => row.Outcome == FillOutcome.Assign).ToList();

            var guesses = new List<KeyValuePair<FillRow, string>>();

            foreach (var row in picks.Where(row => row.Reason != "same name"))
            {
                var value = row.Pick.Value;

                var other = picks.FirstOrDefault(candidate => candidate != row && Same_Internal(candidate.Pick, row.Pick));

                var usedBy = value != null && inUse.Contains(value) ? slots.First(slot => slot.Current == value) : null;

                if (usedBy != null)
                {
                    guesses.Add(new KeyValuePair<FillRow, string>(row, $"{row.Pick.Name} is already in {usedBy.Label}"));
                }
                else if (other != null)
                {
                    guesses.Add(new KeyValuePair<FillRow, string>(row, $"{row.Pick.Name} also fits {other.Slot.Label}"));
                }
            }

            foreach (var guess in guesses)
            {
                guess.Key.Outcome = FillOutcome.Ambiguous;

                guess.Key.Reason = guess.Value;

                guess.Key.Pick = null;
            }

            return rows;
        }

        private static bool Same_Internal(ReferenceCandidate a, ReferenceCandidate b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            return a != null && b != null && !ReferenceEquals(a.Value, null) && a.Value == b.Value;
        }

        /// <summary>
        /// The candidates whose type fits a field, best name first.
        /// </summary>
        public static List<ReferenceCandidate> Matches(ReferenceSlot slot, IReadOnlyList<ReferenceCandidate> candidates)
        {
            var key = Normalize(slot.FieldName);

            return candidates.Where(candidate => slot.FieldType == typeof(GameObject) ? candidate.Type == typeof(GameObject) : candidate.Type != typeof(GameObject) && slot.FieldType.IsAssignableFrom(candidate.Type))
                             .OrderByDescending(candidate => Normalize(candidate.Name) == key)
                             .ThenByDescending(candidate => key.Length > 0 && Normalize(candidate.Name).Contains(key))
                             .ThenBy(candidate => candidate.HierarchyPath, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        }

        /// <summary>
        /// A name reduced for comparing: "m_PlayerCharacter", "Player Character" and "player_character" all give
        /// "playercharacter".
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var text = name.StartsWith("m_", StringComparison.Ordinal) ? name.Substring(2) : name;

            var clean = new StringBuilder(text.Length);

            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                {
                    clean.Append(char.ToLowerInvariant(character));
                }
            }

            return clean.ToString();
        }

        /// <summary>
        /// The level-reference fields of a component: its Component and GameObject fields (array elements included), except
        /// the framework's own class slots (a GameMode's Player Controller, Default Pawn and HUD are prefabs).
        /// </summary>
        public static List<ReferenceSlot> Slots(SerializedObject serialized)
        {
            var slots = new List<ReferenceSlot>();

            if (serialized == null || serialized.targetObject == null)
            {
                return slots;
            }

            var ownerType = serialized.targetObject.GetType();

            var iterator = serialized.GetIterator();

            var enter = true;

            while (iterator.NextVisible(enter))
            {
                enter = iterator.propertyType == SerializedPropertyType.Generic && iterator.hasVisibleChildren;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                var field = Scaffold.FieldOf(ownerType, iterator.propertyPath);

                var fieldType = Scaffold.FieldType(ownerType, iterator.propertyPath);

                if (field == null || fieldType == null || Scaffold.IsFrameworkField(field))
                {
                    continue;
                }

                if (fieldType != typeof(GameObject) && !typeof(Component).IsAssignableFrom(fieldType))
                {
                    continue;
                }

                var path = iterator.propertyPath;

                var arrayAt = path.LastIndexOf(".Array.data[", StringComparison.Ordinal);

                slots.Add(new ReferenceSlot(path, field.Name, fieldType, iterator.objectReferenceValue, arrayAt >= 0 ? path.Substring(0, arrayAt) : null));
            }

            return slots;
        }

        /// <summary>
        /// Every GameObject of a level (inactive ones too) and its components, except <paramref name="exclude"/>.
        /// </summary>
        public static List<ReferenceCandidate> Candidates(Scene scene, Object exclude = null)
        {
            var candidates = new List<ReferenceCandidate>();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return candidates;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var gameObject = transform.gameObject;

                    if ((gameObject.hideFlags & (HideFlags.HideInHierarchy | HideFlags.DontSave)) != 0)
                    {
                        continue;
                    }

                    var path = ActionResult.HierarchyPath(transform);

                    candidates.Add(new ReferenceCandidate(gameObject, typeof(GameObject), gameObject.name, path));

                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (component != null && component != exclude)
                        {
                            candidates.Add(new ReferenceCandidate(component, component.GetType(), gameObject.name, path));
                        }
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// The level a component's references come from: its own scene (the prefab's stage in Prefab Mode). Invalid for
        /// prefab assets, which cannot point into a level.
        /// </summary>
        public static Scene LevelOf(Component owner)
        {
            return owner != null && !EditorUtility.IsPersistent(owner) ? owner.gameObject.scene : default;
        }
    }
}
