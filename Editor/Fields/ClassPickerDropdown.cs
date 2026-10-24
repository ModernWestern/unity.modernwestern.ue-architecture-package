using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What the user picked in a <see cref="ClassPickerDropdown"/>.
    /// </summary>
    internal enum ClassPickKind
    {
        /// <summary>
        /// An existing prefab: assign it.
        /// </summary>
        Prefab,

        /// <summary>
        /// A class without a prefab: build its prefab (no compile), then assign it.
        /// </summary>
        Class,

        /// <summary>
        /// Write a new script (and its prefab after the compile).
        /// </summary>
        NewScript,

        /// <summary>
        /// Clear the field.
        /// </summary>
        None,

        /// <summary>
        /// Make a prefab variant of the current prefab (a child Blueprint) and assign it.
        /// </summary>
        Variant,

        /// <summary>
        /// Open the current prefab in Prefab Mode.
        /// </summary>
        OpenPrefab,

        /// <summary>
        /// Open the script of the current prefab's class.
        /// </summary>
        OpenScript
    }

    /// <summary>
    /// One choice of the class picker.
    /// </summary>
    internal readonly struct ClassPick
    {
        public readonly ClassPickKind Kind;

        public readonly Type Type;

        public readonly GameObject Prefab;

        public readonly string Label;

        public ClassPick(ClassPickKind kind, Type type, GameObject prefab, string label)
        {
            Kind = kind;

            Type = type;

            Prefab = prefab;

            Label = label;
        }
    }

    /// <summary>
    /// The choices of the class picker for a base class, in two groups: the prefabs that have the class (or a subclass)
    /// on their root, and the classes that have no prefab yet. Pure, so it is tested without a window.
    /// </summary>
    internal static class ClassPickerModel
    {
        /// <summary>
        /// The classes a picker for <paramref name="baseType"/> offers: the class and its subclasses, without abstract or
        /// generic classes and without test assemblies.
        /// </summary>
        public static Type[] CandidateTypes(Type baseType)
        {
            if (baseType == null)
            {
                return Type.EmptyTypes;
            }

            return TypeCache.GetTypesDerivedFrom(baseType)
                            .Append(baseType)
                            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && !UEArchitectureSettingsProvider.IsTestType_Internal(type))
                            .Distinct()
                            .ToArray();
        }

        /// <summary>
        /// The "Prefabs" group: every prefab whose root has <paramref name="baseType"/> (or a subclass), labelled
        /// "Arena Player · ArenaPlayerController" when the prefab and class names differ.
        /// </summary>
        public static List<ClassPick> Prefabs(Type baseType, IEnumerable<GameObject> prefabs, IReadOnlyCollection<Type> types)
        {
            return Prefabs(baseType, prefabs, types, null, null, null, out _);
        }

        /// <summary>
        /// The "Prefabs" group, telling apart prefabs named alike (with the Sitizens naming, a class's prefab and every
        /// level's copy of it are all called after the class): the current level's own copies are labelled
        /// "GameplayGameMode (this level)", other prefabs with the same label get their folder, and the copies other levels
        /// own go to <paramref name="otherLevels"/> ("GameplayGameMode (Boss's own)"), so they are not picked by mistake.
        /// <paramref name="pathOf"/> gives a prefab's path and <paramref name="levelOfFolder"/> the level whose override
        /// folder a folder is (null for none); without them every prefab is in the main group.
        /// </summary>
        public static List<ClassPick> Prefabs(Type baseType, IEnumerable<GameObject> prefabs, IReadOnlyCollection<Type> types, Func<GameObject, string> pathOf,
                                              Func<string, string> levelOfFolder, string currentLevel, out List<ClassPick> otherLevels)
        {
            var picks = new List<ClassPick>();

            otherLevels = new List<ClassPick>();

            var folders = new Dictionary<GameObject, string>();

            foreach (var prefab in prefabs ?? Array.Empty<GameObject>())
            {
                var component = prefab != null ? prefab.GetComponent(baseType) : null;

                if (component == null)
                {
                    continue;
                }

                var typeName = UEArchitectureStyles.TypeName(component.GetType(), types.Append(component.GetType()).Distinct());

                var label = prefab.name == component.GetType().Name ? prefab.name : $"{prefab.name} · {typeName}";

                var folder = GeneratorNaming.FolderOf(pathOf?.Invoke(prefab));

                var level = folder.Length > 0 ? levelOfFolder?.Invoke(folder) : null;

                folders[prefab] = folder;

                if (level == null)
                {
                    picks.Add(new ClassPick(ClassPickKind.Prefab, component.GetType(), prefab, label));
                }
                else if (level == currentLevel)
                {
                    picks.Add(new ClassPick(ClassPickKind.Prefab, component.GetType(), prefab, label + " (this level)"));
                }
                else
                {
                    otherLevels.Add(new ClassPick(ClassPickKind.Prefab, component.GetType(), prefab, $"{label} ({level}'s own)"));
                }
            }

            picks = Disambiguate_Internal(picks, folders);

            otherLevels = Disambiguate_Internal(otherLevels, folders);

            return picks;
        }

        private static List<ClassPick> Disambiguate_Internal(List<ClassPick> picks, Dictionary<GameObject, string> folders)
        {
            var repeated = new HashSet<string>(picks.GroupBy(pick => pick.Label, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key),
                                               StringComparer.OrdinalIgnoreCase);

            return picks.Select(pick => repeated.Contains(pick.Label) && folders.TryGetValue(pick.Prefab, out var folder) && folder.Length > 0
                                            ? new ClassPick(pick.Kind, pick.Type, pick.Prefab, $"{pick.Label}  ({folder})")
                                            : pick)
                        .OrderBy(pick => pick.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        /// <summary>
        /// The "Classes without a prefab" group: the candidate classes no prefab has on its root (as that exact class).
        /// </summary>
        public static List<ClassPick> ClassesWithoutPrefab(IReadOnlyCollection<Type> types, IEnumerable<GameObject> prefabs, Type baseType)
        {
            var withPrefab = new HashSet<Type>();

            foreach (var prefab in prefabs ?? Array.Empty<GameObject>())
            {
                if (prefab == null)
                {
                    continue;
                }

                foreach (var component in prefab.GetComponents(baseType))
                {
                    if (component != null)
                    {
                        withPrefab.Add(component.GetType());
                    }
                }
            }

            return types.Where(type => !withPrefab.Contains(type))
                        .Select(type => new ClassPick(ClassPickKind.Class, type, null, UEArchitectureStyles.TypeName(type, types)))
                        .OrderBy(pick => pick.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }
    }

    /// <summary>
    /// The searchable picker behind a class field's '▾', like Unreal's class picker: "Prefabs", "Classes without a
    /// prefab", "New script…" and "None". Built from <see cref="FrameworkIndex"/> and TypeCache when it opens.
    /// </summary>
    internal sealed class ClassPickerDropdown : AdvancedDropdown
    {
        public const string NewScriptLabel = "New script…";

        public const string DefaultNoneLabel = "None (use the base class)";

        private readonly Dictionary<AdvancedDropdownItem, ClassPick> _picks = new();

        private readonly Type _baseType;

        private readonly string _title;

        private readonly string _noneLabel;

        private readonly Action<ClassPick> _picked;

        private readonly IReadOnlyList<ClassPick> _extras;

        private readonly string _level;

        /// <summary>
        /// A picker for <paramref name="baseType"/>. <paramref name="extras"/> (actions on the current value, such as
        /// "Variant of Arena Pawn", "Open prefab" and "Open script") come first. <paramref name="level"/> is the level the
        /// field belongs to (its own copies are labelled "this level"; other levels' copies are listed apart).
        /// </summary>
        public ClassPickerDropdown(AdvancedDropdownState state, Type baseType, string title, Action<ClassPick> picked, string noneLabel = DefaultNoneLabel,
                                   IReadOnlyList<ClassPick> extras = null, string level = null) : base(state ?? new AdvancedDropdownState())
        {
            _baseType = baseType;

            _title = string.IsNullOrEmpty(title) ? ObjectNames.NicifyVariableName(baseType?.Name ?? "Class") : title;

            _picked = picked;

            _noneLabel = noneLabel;

            _extras = extras ?? Array.Empty<ClassPick>();

            _level = level;

            minimumSize = new Vector2(320f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            _picks.Clear();

            var root = new AdvancedDropdownItem(_title);

            if (_extras.Count > 0)
            {
                var extraIcon = EditorGUIUtility.IconContent("Prefab Icon").image as Texture2D;

                foreach (var extra in _extras)
                {
                    Add_Internal(root, extra, extra.Kind == ClassPickKind.OpenScript ? EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D : extraIcon);
                }

                root.AddSeparator();
            }

            var types = ClassPickerModel.CandidateTypes(_baseType);

            var prefabs = FrameworkIndex.PrefabsWith(_baseType);

            var prefabIcon = EditorGUIUtility.IconContent("Prefab Icon").image as Texture2D;

            var scriptIcon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;

            var conventions = UEArchitectureEditorSettings.Current;

            var mainPrefabs = ClassPickerModel.Prefabs(_baseType, prefabs, types, AssetDatabase.GetAssetPath, folder => GeneratorNaming.SceneOfOverrideFolder(folder, conventions), _level,
                                                       out var otherLevels);

            AddGroup_Internal(root, "Prefabs", mainPrefabs, prefabIcon, "No prefab yet");

            if (otherLevels.Count > 0)
            {
                AddGroup_Internal(root, "Other levels' own copies (changing one changes that level)", otherLevels, prefabIcon, string.Empty);
            }

            root.AddSeparator();

            AddGroup_Internal(root, "Classes without a prefab", ClassPickerModel.ClassesWithoutPrefab(types, prefabs, _baseType), scriptIcon, "Every class has a prefab");

            root.AddSeparator();

            Add_Internal(root, new ClassPick(ClassPickKind.NewScript, _baseType, null, NewScriptLabel), scriptIcon);

            if (!string.IsNullOrEmpty(_noneLabel))
            {
                Add_Internal(root, new ClassPick(ClassPickKind.None, null, null, _noneLabel), null);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_picks.TryGetValue(item, out var pick))
            {
                _picked?.Invoke(pick);
            }
        }

        private void AddGroup_Internal(AdvancedDropdownItem root, string header, List<ClassPick> picks, Texture2D icon, string empty)
        {
            root.AddChild(new AdvancedDropdownItem(header) { enabled = false });

            if (picks.Count == 0)
            {
                root.AddChild(new AdvancedDropdownItem("   " + empty) { enabled = false });

                return;
            }

            foreach (var pick in picks)
            {
                Add_Internal(root, pick, icon);
            }
        }

        private void Add_Internal(AdvancedDropdownItem parent, ClassPick pick, Texture2D icon)
        {
            var item = new AdvancedDropdownItem(pick.Label) { icon = icon };

            _picks[item] = pick;

            parent.AddChild(item);
        }
    }
}
