#if UEA_NAVIGATION
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.Tests
{
    /// <summary>
    /// NavMeshes built at runtime from boxes (nothing to bake), walkers with their AI, and waits in real time.
    /// The ground is 40 x 40 m around the origin, its walkable top at y = 0 (the NavMesh lies a few centimetres above).
    /// </summary>
    public abstract class NavigationTestBase : FrameworkTestBase
    {
        private readonly List<NavMeshDataInstance> _navMeshes = new();

        private readonly List<NavMeshLinkInstance> _links = new();

        private readonly List<GameObject> _objects = new();

        private (Vector3 center, Vector3 size)[] _lastBoxes;

        private int _frameRate;

        // Frames like a game's, not the uncapped ones of a batch run: the agents' simulation behaves as it would in play.
        [SetUp]
        public void SetFrameRate()
        {
            _frameRate = Application.targetFrameRate;

            Application.targetFrameRate = 60;
        }

        [TearDown]
        public void RemoveNavigation()
        {
            Time.timeScale = 1f;

            Application.targetFrameRate = _frameRate;

            foreach (var created in _objects)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _objects.Clear();

            RemoveNavMeshes();
        }

        protected void BuildNavMesh(params (Vector3 center, Vector3 size)[] boxes)
        {
            _lastBoxes = boxes;

            var sources = new List<NavMeshBuildSource> { Box_Internal(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f)) };

            sources.AddRange(boxes.Select(box => Box_Internal(box.center, box.size)));

            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(new Vector3(10f, 0f, 0f), new Vector3(100f, 30f, 60f)), Vector3.zero, Quaternion.identity);

            _navMeshes.Add(NavMesh.AddNavMeshData(data));
        }

        /// <summary>
        /// Removes the NavMesh and adds the same one again, as a runtime rebuild would.
        /// </summary>
        protected void RebuildNavMesh()
        {
            RemoveNavMeshes();

            BuildNavMesh(_lastBoxes ?? Array.Empty<(Vector3, Vector3)>());
        }

        protected void RemoveNavMeshes()
        {
            foreach (var link in _links)
            {
                link.Remove();
            }

            foreach (var navMesh in _navMeshes)
            {
                navMesh.Remove();
            }

            _links.Clear();

            _navMeshes.Clear();
        }

        protected void AddLink(Vector3 start, Vector3 end)
        {
            _links.Add(NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = start,
                endPosition = end,
                width = 1f,
                costModifier = -1f,
                bidirectional = true,
                area = 0,
                agentTypeID = 0
            }));
        }

        /// <summary>
        /// A Pawn with a NavMeshAgent, a bit faster than Unity's default so the tests take less time.
        /// </summary>
        protected TestPawn Walker(Vector3 position, Action<NavMeshAgent> setup = null, string name = "Walker")
        {
            var walker = new GameObject(name);

            walker.SetActive(false);

            walker.transform.position = position;

            var pawn = walker.AddComponent<TestPawn>();

            var agent = walker.AddComponent<NavMeshAgent>();

            agent.speed = 6f;

            agent.acceleration = 40f;

            agent.angularSpeed = 720f;

            setup?.Invoke(agent);

            walker.SetActive(true);

            _objects.Add(walker);

            return pawn;
        }

        protected PathFollowing Follower(Pawn pawn, float blockTime = 3f)
        {
            var pathFollowing = pawn.PathFollowingToMove_Internal();

            pathFollowing.BlockTime = blockTime;

            return pathFollowing;
        }

        protected TestNavAI Possess(Pawn pawn, float blockTime = 3f)
        {
            var ai = new GameObject("AI " + pawn.name).AddComponent<TestNavAI>();

            _objects.Add(ai.gameObject);

            ai.Possess(pawn);

            Follower(pawn, blockTime);

            return ai;
        }

        protected GameObject Thing(string name, Vector3 position)
        {
            var thing = new GameObject(name);

            thing.transform.position = position;

            _objects.Add(thing);

            return thing;
        }

        protected static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            var end = Time.realtimeSinceStartup + seconds;

            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    Assert.Fail("Timed out waiting for " + what + ".");
                }

                yield return null;
            }
        }

        protected static IEnumerator Seconds(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        protected static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;

            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// The usual hand-written arrival check, to show where it goes wrong.
        /// </summary>
        protected static bool NaiveArrived(NavMeshAgent agent)
        {
            return agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
        }

        private static NavMeshBuildSource Box_Internal(Vector3 center, Vector3 size)
        {
            return new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = size,
                transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                area = 0
            };
        }

        /// <summary>
        /// Every MoveCompleted of one PathFollowing.
        /// </summary>
        protected sealed class MoveLog
        {
            public readonly List<MoveResult> Results = new();

            public MoveLog(PathFollowing pathFollowing)
            {
                pathFollowing.MoveCompleted += Results.Add;
            }

            public int Count => Results.Count;

            public MoveResult Single(MoveRequest request)
            {
                var matching = Results.Where(result => result.Request.Id == request.Id).ToArray();

                Assert.AreEqual(1, matching.Length, $"Move {request.Id} must end exactly once. Ended: {string.Join(", ", Results.Select(result => result.Request.Id + " " + result))}");

                return matching[0];
            }
        }
    }
}
#endif
