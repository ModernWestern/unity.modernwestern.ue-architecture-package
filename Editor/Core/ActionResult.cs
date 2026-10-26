using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What an editor action did: the files and objects it created or changed, what is left for after the next script
    /// compile, and what stopped it, in plain words. Every UEArchitectureActions method returns one, and the command line
    /// prints it as one JSON line.
    /// </summary>
    [Serializable]
    public sealed class ActionResult
    {
        [SerializeField] private string verb = string.Empty;

        [SerializeField] private bool ok = true;

        [SerializeField] private bool pending;

        [SerializeField] private string message = string.Empty;

        [SerializeField] private string result = string.Empty;

        [SerializeField] private List<string> created = new();

        [SerializeField] private List<string> changed = new();

        [SerializeField] private List<string> problems = new();

        [SerializeField] private List<string> notes = new();

        [NonSerialized] private Object _resultObject;

        public ActionResult()
        {
        }

        public ActionResult(string verb)
        {
            this.verb = verb ?? string.Empty;
        }

        /// <summary>
        /// The action's name, e.g. "create-prefab".
        /// </summary>
        public string Verb
        {
            get => verb;
            set => verb = value ?? string.Empty;
        }

        /// <summary>
        /// True when nothing stopped the action (no problems).
        /// </summary>
        public bool Ok => problems.Count == 0;

        /// <summary>
        /// True when part of the work waits for the next script compile (see PendingSteps).
        /// </summary>
        public bool Pending
        {
            get => pending;
            set => pending = value;
        }

        /// <summary>
        /// One sentence for people: what happened, or what to do next.
        /// </summary>
        public string Message
        {
            get => message;
            set => message = value ?? string.Empty;
        }

        /// <summary>
        /// The main object the action made or used (a prefab, a component...), when there is one.
        /// </summary>
        public Object Result
        {
            get => _resultObject;
            set
            {
                _resultObject = value;

                result = Describe(value);
            }
        }

        /// <summary>
        /// Asset paths (and "scene#path" addresses) of what was created. Undo cannot remove created files.
        /// </summary>
        public IReadOnlyList<string> Created => created;

        /// <summary>
        /// What was changed, with the field: "Assets/Prefabs/Gameplay/ArenaGameMode.prefab: playerController".
        /// </summary>
        public IReadOnlyList<string> Changed => changed;

        /// <summary>
        /// What stopped the action, each with what to do about it.
        /// </summary>
        public IReadOnlyList<string> Problems => problems;

        /// <summary>
        /// Anything else worth knowing that is not a problem ("Reused Assets/Prefabs/Gameplay/ArenaPawn.prefab").
        /// </summary>
        public IReadOnlyList<string> Notes => notes;

        public static ActionResult Fail(string verb, string problem)
        {
            var failed = new ActionResult(verb);

            failed.AddProblem(problem);

            return failed;
        }

        public void AddCreated(string what)
        {
            if (!string.IsNullOrEmpty(what) && !created.Contains(what))
            {
                created.Add(what);
            }
        }

        public void AddChanged(string what)
        {
            if (!string.IsNullOrEmpty(what) && !changed.Contains(what))
            {
                changed.Add(what);
            }
        }

        public void AddProblem(string what)
        {
            if (!string.IsNullOrEmpty(what) && !problems.Contains(what))
            {
                problems.Add(what);
            }
        }

        public void AddNote(string what)
        {
            if (!string.IsNullOrEmpty(what) && !notes.Contains(what))
            {
                notes.Add(what);
            }
        }

        /// <summary>
        /// Adds everything another result collected (a step of this action) to this one. Its problems are left out when
        /// <paramref name="includeProblems"/> is false (the caller reports them its own way).
        /// </summary>
        public void Merge(ActionResult other, bool includeProblems = true)
        {
            if (other == null)
            {
                return;
            }

            foreach (var item in other.created)
            {
                AddCreated(item);
            }

            foreach (var item in other.changed)
            {
                AddChanged(item);
            }

            if (includeProblems)
            {
                foreach (var item in other.problems)
                {
                    AddProblem(item);
                }
            }

            foreach (var item in other.notes)
            {
                AddNote(item);
            }

            pending |= other.pending;

            if (_resultObject == null && other._resultObject != null)
            {
                Result = other._resultObject;
            }
        }

        /// <summary>
        /// One line of JSON: {"verb":..,"ok":..,"pending":..,"message":..,"result":..,"created":[..],"changed":[..],"problems":[..],"notes":[..]}.
        /// </summary>
        public string ToJson()
        {
            ok = problems.Count == 0;

            return JsonUtility.ToJson(this);
        }

        public static ActionResult FromJson(string json)
        {
            return string.IsNullOrEmpty(json) ? new ActionResult() : JsonUtility.FromJson<ActionResult>(json);
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }

            return Ok ? "Done." : string.Join("\n", problems);
        }

        /// <summary>
        /// How an object is named in results: its asset path, or "scene path#hierarchy path" for a scene object.
        /// </summary>
        public static string Describe(Object target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            var assetPath = AssetDatabase.GetAssetPath(target);

            if (!string.IsNullOrEmpty(assetPath))
            {
                var assetObject = target is Component assetComponent ? assetComponent.gameObject : target as GameObject;

                if (assetObject != null && assetObject.transform.parent != null)
                {
                    return assetPath + "#" + HierarchyPath(assetObject.transform);
                }

                return assetPath;
            }

            var gameObject = target is Component component ? component.gameObject : target as GameObject;

            if (gameObject == null)
            {
                return target.name;
            }

            var scenePath = gameObject.scene.IsValid() && !string.IsNullOrEmpty(gameObject.scene.path) ? gameObject.scene.path : "(unsaved scene)";

            return scenePath + "#" + HierarchyPath(gameObject.transform);
        }

        /// <summary>
        /// "Canvas/CarryBar" for a child, or the name of a root (the root of a prefab asset is left out).
        /// </summary>
        public static string HierarchyPath(Transform transform)
        {
            var path = transform.name;

            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.parent == null && EditorUtility.IsPersistent(parent))
                {
                    break;
                }

                path = parent.name + "/" + path;
            }

            return path;
        }
    }
}
