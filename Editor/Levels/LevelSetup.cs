using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What "Set Up This Level" and a new level get. Each part is added only when the level does not have it.
    /// </summary>
    public sealed class LevelSetupOptions
    {
        /// <summary>
        /// A GameMode prefab for the level's World Settings. Null: World Settings is added empty (the project default
        /// GameMode is used) and an existing override is kept.
        /// </summary>
        public GameObject GameMode;

        /// <summary>
        /// Player Starts the level needs. 0 or less: one per player at start of the level's GameMode (at least one).
        /// </summary>
        public int PlayerStarts;

        public bool WorldSettings = true;

        public bool Camera = true;

        public bool Light = true;

        public bool EventSystem = true;

        public bool AudioListener = true;

        /// <summary>
        /// A ground plane, only when the level has no collider at all (new levels).
        /// </summary>
        public bool Ground;
    }

    /// <summary>
    /// Makes a level playable: World Settings, one Player Start per player (on the ground, facing the view), a camera
    /// aimed at the first start, a light, an EventSystem with the input module that fits the project and an
    /// AudioListener (unless the GameMode's Pawn or Player Controller brings one). Find-or-add: what the level has is never touched, and every part says whether it was added or was
    /// already fine. Render-pipeline agnostic (plain Camera and Light).
    /// </summary>
    internal static class LevelSetup
    {
        private const float StartSpacing = 2f;

        /// <summary>
        /// Adds what <paramref name="options"/> asks for and the level lacks. <paramref name="added"/> and
        /// <paramref name="fine"/> name each part ("2 Player Starts", "camera"). <paramref name="undo"/> is false for a
        /// level the action made itself.
        /// </summary>
        public static void SetUp(ScaffoldOp op, Scene scene, LevelSetupOptions options, List<string> added, List<string> fine, bool undo)
        {
            options ??= new LevelSetupOptions();

            if (options.Ground)
            {
                if (HasAny_Internal(scene, "UnityEngine.Collider"))
                {
                    fine.Add("ground");
                }
                else if (AddGround_Internal(op, scene, undo))
                {
                    added.Add("ground");
                }
            }

            if (options.WorldSettings || options.GameMode != null)
            {
                var worldSettings = GameModeInfo.Find<WorldSettings>(scene);

                if (worldSettings == null)
                {
                    var created = op.CreateSceneObject(scene, "World Settings", undo, typeof(WorldSettings));

                    worldSettings = created != null ? created.GetComponent<WorldSettings>() : null;

                    if (worldSettings != null)
                    {
                        added.Add("World Settings");
                    }
                }
                else
                {
                    fine.Add("World Settings");
                }

                if (worldSettings != null && options.GameMode != null)
                {
                    var gameMode = options.GameMode.GetComponent<GameMode>();

                    if (gameMode == null || !EditorUtility.IsPersistent(gameMode))
                    {
                        op.Problem($"{options.GameMode.name} is not a GameMode prefab.");
                    }
                    else if (worldSettings.GameModeOverride != gameMode && op.Assign(worldSettings, "gameModeOverride", gameMode))
                    {
                        added.Add("GameMode " + options.GameMode.name);
                    }
                }
            }

            var starts = AddPlayerStarts(op, scene, options.PlayerStarts, undo);

            if (starts > 0)
            {
                added.Add(starts == 1 ? "1 Player Start" : $"{starts} Player Starts");
            }
            else
            {
                fine.Add("Player Starts");
            }

            var firstStart = Starts_Internal(scene).FirstOrDefault();

            if (options.Camera)
            {
                if (FindInScene<Camera>(scene) != null)
                {
                    fine.Add("camera");
                }
                else if (AddCamera_Internal(op, scene, firstStart != null ? firstStart.transform : null, undo))
                {
                    added.Add("camera");
                }
            }

            if (options.AudioListener)
            {
                if (FindInScene<AudioListener>(scene) != null)
                {
                    fine.Add("AudioListener");
                }
                else if (SpawnsListener_Internal(GameModeInfo.Effective(scene, out GameModeSource _)))
                {
                    // A second one would make Unity warn about two listeners as soon as the player spawns.
                    fine.Add("AudioListener (the player brings one)");
                }
                else if (AddAudioListener(op, scene, undo) != null)
                {
                    added.Add("AudioListener");
                }
            }

            if (options.Light)
            {
                if (FindInScene<Light>(scene) != null)
                {
                    fine.Add("light");
                }
                else if (AddLight_Internal(op, scene, undo))
                {
                    added.Add("light");
                }
            }

            if (options.EventSystem)
            {
                if (UiTypes.EventSystem == null)
                {
                    op.Result.AddNote("No EventSystem: " + UiBuilders.NeedsUnityUI);
                }
                else if (HasAny_Internal(scene, UiTypes.EventSystem))
                {
                    fine.Add("EventSystem");
                }
                else if (AddEventSystem(op, scene, undo) != null)
                {
                    added.Add("EventSystem");
                }
            }
        }

        /// <summary>
        /// "Added: 2 Player Starts, EventSystem; already fine: camera, light".
        /// </summary>
        public static string Report(IReadOnlyCollection<string> added, IReadOnlyCollection<string> fine)
        {
            var text = added.Count == 0 ? "Nothing to add" : "Added: " + string.Join(", ", added);

            return fine.Count == 0 ? text + "." : text + "; already fine: " + string.Join(", ", fine) + ".";
        }

        /// <summary>
        /// Adds Player Starts until the level has <paramref name="count"/> (0 or less: one per player at start of the
        /// level's GameMode, at least one). Each goes next to the last one, or at the Scene view's pivot facing the view
        /// (the origin headless), dropped to the ground. Returns how many were added.
        /// </summary>
        public static int AddPlayerStarts(ScaffoldOp op, Scene scene, int count, bool undo)
        {
            var needed = count > 0 ? count : PlayersAtStart(scene);

            var existing = Starts_Internal(scene);

            var missing = needed - existing.Count;

            if (missing <= 0)
            {
                return 0;
            }

            Vector3 origin;

            Quaternion facing;

            if (existing.Count > 0)
            {
                var last = existing[existing.Count - 1].transform;

                origin = last.position + last.right * StartSpacing;

                facing = last.rotation;
            }
            else
            {
                ViewPlacement(scene, out origin, out facing);
            }

            var right = facing * Vector3.right;

            var made = 0;

            for (var i = 0; i < missing; i++)
            {
                var name = existing.Count + made == 0 ? "Player Start" : $"Player Start {existing.Count + made + 1}";

                var created = op.CreateSceneObject(scene, name, undo, typeof(PlayerStart));

                if (created == null)
                {
                    break;
                }

                created.transform.SetPositionAndRotation(Ground(origin + right * (StartSpacing * i)), facing);

                made++;
            }

            return made;
        }

        /// <summary>
        /// Players created when the level begins: the playersAtStart of the level's GameMode, at least one.
        /// </summary>
        public static int PlayersAtStart(Scene scene)
        {
            var gameMode = GameModeInfo.Effective(scene, out GameModeSource _);

            return gameMode != null ? Mathf.Max(1, gameMode.PlayersAtStart) : 1;
        }

        /// <summary>
        /// Where a new object goes in a level: the Scene view's pivot, facing the way the view looks (flat), when the level
        /// is the one being edited; otherwise the origin facing forward.
        /// </summary>
        public static void ViewPlacement(Scene scene, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;

            rotation = Quaternion.identity;

            var view = SceneView.lastActiveSceneView;

            if (Application.isBatchMode || view == null || view.camera == null || scene != SceneManager.GetActiveScene())
            {
                return;
            }

            position = view.pivot;

            var forward = Vector3.ProjectOnPlane(view.camera.transform.forward, Vector3.up);

            if (forward.sqrMagnitude > 0.0001f)
            {
                rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// The point on the ground below <paramref name="position"/>: the first collider straight down, else y = 0; then the
        /// closest NavMesh point within 2 m when there is a NavMesh (Unity's AI module).
        /// </summary>
        public static Vector3 Ground(Vector3 position)
        {
            var grounded = position;

            if (TryGroundBelow(position, out var hit))
            {
                grounded = hit;
            }
            else
            {
                grounded.y = 0f;
            }
#if UEA_NAVIGATION
            if (UnityEngine.AI.NavMesh.SamplePosition(grounded, out var navHit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                grounded = navHit.position;
            }
#endif
            return grounded;
        }

        /// <summary>
        /// The first collider point straight below <paramref name="position"/> (starting just above it), or false.
        /// </summary>
        public static bool TryGroundBelow(Vector3 position, out Vector3 point)
        {
            point = position;
#if UEA_PHYSICS
            Physics.SyncTransforms();

            if (Physics.Raycast(position + Vector3.up * 0.25f, Vector3.down, out var hit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;

                return true;
            }
#endif
            return false;
        }

        /// <summary>
        /// The closest point on a NavMesh within <paramref name="radius"/>, or false (always false without Unity's AI module).
        /// </summary>
        public static bool TryNavMesh(Vector3 position, float radius, out Vector3 point)
        {
            point = position;
#if UEA_NAVIGATION
            if (UnityEngine.AI.NavMesh.SamplePosition(position, out var hit, radius, UnityEngine.AI.NavMesh.AllAreas))
            {
                point = hit.position;

                return true;
            }
#endif
            return false;
        }

        /// <summary>
        /// True when the project has a NavMesh loaded (baked or built), so "off the NavMesh" means something.
        /// </summary>
        public static bool HasNavMesh()
        {
#if UEA_NAVIGATION
            return UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length > 0;
#else
            return false;
#endif
        }

        /// <summary>
        /// An EventSystem object with the input module that fits the project (by type name: no hard reference to Unity UI
        /// or the Input System). Null when Unity UI is not installed. Never inside a prefab: HUD prefabs are spawned once
        /// per player, the level holds the one EventSystem.
        /// </summary>
        public static GameObject AddEventSystem(ScaffoldOp op, Scene scene, bool undo)
        {
            if (UiTypes.EventSystem == null)
            {
                op.Problem("Cannot add an EventSystem. " + UiBuilders.NeedsUnityUI);

                return null;
            }

            var created = op.CreateSceneObject(scene, "EventSystem", undo, UiTypes.EventSystem);

            if (created != null)
            {
                EditorCompat.AddInputModule(created);
            }

            return created;
        }

        /// <summary>
        /// An AudioListener on the level's first camera, or on a new object when it has none.
        /// </summary>
        public static AudioListener AddAudioListener(ScaffoldOp op, Scene scene, bool undo)
        {
            var camera = FindInScene<Camera>(scene);

            if (camera == null)
            {
                var created = op.CreateSceneObject(scene, "Audio Listener", undo, typeof(AudioListener));

                return created != null ? created.GetComponent<AudioListener>() : null;
            }

            if (!op.CheckEditable(camera))
            {
                return null;
            }

            var listener = undo ? Undo.AddComponent<AudioListener>(camera.gameObject) : camera.gameObject.AddComponent<AudioListener>();

            op.Result.AddCreated(ActionResult.Describe(camera.gameObject) + " (AudioListener)");

            op.Touch(camera);

            return listener;
        }

        /// <summary>
        /// True when the GameMode spawns an AudioListener with each player (on the Pawn or the Player Controller prefab).
        /// </summary>
        internal static bool SpawnsListener_Internal(GameMode gameMode)
        {
            if (gameMode == null)
            {
                return false;
            }

            var pawn = gameMode.DefaultPawnPrefab;

            var controller = gameMode.PlayerControllerPrefab;

            return pawn != null && pawn.GetComponentInChildren<AudioListener>(true) != null || controller != null && controller.GetComponentInChildren<AudioListener>(true) != null;
        }

        /// <summary>
        /// The first component of a type in a loaded level (inactive objects included), or null.
        /// </summary>
        public static T FindInScene<T>(Scene scene) where T : Component
        {
            return GameModeInfo.Find<T>(scene);
        }

        internal static List<PlayerStart> Starts_Internal(Scene scene)
        {
            var starts = new List<PlayerStart>();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return starts;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                starts.AddRange(root.GetComponentsInChildren<PlayerStart>(true));
            }

            return starts;
        }

        internal static bool HasAny_Internal(Scene scene, string typeName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    for (var type = component != null ? component.GetType() : null; type != null; type = type.BaseType)
                    {
                        if (type.FullName == typeName)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        internal static bool HasAny_Internal(Scene scene, Type type)
        {
            return type != null && scene.IsValid() && scene.isLoaded && scene.GetRootGameObjects().Any(root => root.GetComponentInChildren(type, true) != null);
        }

        private static bool AddCamera_Internal(ScaffoldOp op, Scene scene, Transform start, bool undo)
        {
            var created = op.CreateSceneObject(scene, "Main Camera", undo, typeof(Camera));

            if (created == null)
            {
                return false;
            }

            created.tag = "MainCamera";

            var target = start != null ? start.position + Vector3.up : Vector3.up;

            var back = start != null ? -start.forward : Vector3.back;

            created.transform.position = target + back * 6f + Vector3.up * 2.5f;

            created.transform.LookAt(target);

            return true;
        }

        private static bool AddLight_Internal(ScaffoldOp op, Scene scene, bool undo)
        {
            var created = op.CreateSceneObject(scene, "Directional Light", undo, typeof(Light));

            if (created == null)
            {
                return false;
            }

            var light = created.GetComponent<Light>();

            light.type = LightType.Directional;

            light.shadows = LightShadows.Soft;

            created.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            return true;
        }

        private static bool AddGround_Internal(ScaffoldOp op, Scene scene, bool undo)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);

            ground.name = "Ground";

            if (ground.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(ground, scene);
            }

            ground.transform.localScale = new Vector3(5f, 1f, 5f);

            if (undo)
            {
                Undo.RegisterCreatedObjectUndo(ground, op.Name);
            }

            op.Result.AddCreated(ActionResult.Describe(ground));

            op.Touch(ground);

            return true;
        }
    }
}
