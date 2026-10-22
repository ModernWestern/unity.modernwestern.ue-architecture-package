#if UEA_NAVIGATION
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using UnityEngine.TestTools;
using System.Collections.Generic;
using UnityEngine.TestTools.Constraints;
using System.Text.RegularExpressions;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace ModernWestern.UEArchitecture.Tests
{
    // Every move must end with exactly one MoveCompleted, with the right outcome, whatever happens on the way.
    public class NavigationTests : NavigationTestBase
    {
        private static readonly (Vector3, Vector3) Island = (new Vector3(30f, -0.5f, 0f), new Vector3(10f, 1f, 10f));

        // Five walls across the ground, each with a gap at alternate ends: a long path with many corners.
        private static readonly (Vector3, Vector3)[] Zigzag =
        {
            (new Vector3(-5f, 1.5f, -12f), new Vector3(30f, 3f, 0.5f)),
            (new Vector3(5f, 1.5f, -6f), new Vector3(30f, 3f, 0.5f)),
            (new Vector3(-5f, 1.5f, 0f), new Vector3(30f, 3f, 0.5f)),
            (new Vector3(5f, 1.5f, 6f), new Vector3(30f, 3f, 0.5f)),
            (new Vector3(-5f, 1.5f, 12f), new Vector3(30f, 3f, 0.5f))
        };

        [UnityTest]
        public IEnumerator MoveTo_EndsWithExactlyOneSuccess_AtTheDestination()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var log = new MoveLog(pawn.PathFollowing);

            var destination = new Vector3(0f, 0f, 6f);

            var request = ai.MoveTo(destination);

            Assert.IsTrue(request.IsValid);

            Assert.IsTrue(request.IsActive);

            Assert.AreEqual(MoveStatus.Waiting, ai.MoveStatus, "The path is computed on a later frame.");

            yield return WaitFor(() => ai.MoveStatus == MoveStatus.Moving, 2f, "the move to start");

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            var result = log.Single(request);

            Assert.AreEqual(MoveOutcome.Success, result.Outcome);

            Assert.AreEqual(MoveReason.None, result.Reason);

            Assert.Less(Flat(result.Position, destination), 0.05f);

            Assert.Greater(result.Duration, 0.5f);

            Assert.AreSame(pawn, result.Pawn);

            Assert.AreEqual(1, ai.Results.Count, "The controller hears it once.");

            Assert.IsTrue(request.TryGetResult(out var stored) && stored.IsSuccess);

            Assert.AreEqual(MoveStatus.Idle, ai.MoveStatus);

            yield return Seconds(0.5f);

            Assert.AreEqual(1, log.Count, "Nothing else is raised afterwards.");
        }

        [UnityTest]
        public IEnumerator MoveTo_WithoutAutoBraking_Succeeds()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero, agent => agent.autoBraking = false);

            var ai = Possess(pawn);

            var request = ai.MoveTo(new Vector3(4f, 0f, 4f));

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            Assert.AreEqual(MoveOutcome.Success, ai.Results.Single().Outcome);
        }

        [UnityTest]
        public IEnumerator MoveTo_WithAStoppingDistance_SucceedsWhenItGetsThatClose()
        {
            OpenLevel();

            BuildNavMesh();

            // Without braking the agent coasts past its stopping distance; with it, Unity ignores the distance and walks to the end.
            foreach (var braking in new[] { false, true })
            {
                var pawn = Walker(new Vector3(braking ? 5f : -5f, 0f, -5f), agent =>
                {
                    agent.stoppingDistance = 1.5f;

                    agent.autoBraking = braking;
                });

                var ai = Possess(pawn);

                var destination = pawn.transform.position + new Vector3(0f, 0f, 8f);

                var request = ai.MoveTo(destination);

                yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

                var result = ai.Results.Single();

                Assert.AreEqual(MoveOutcome.Success, result.Outcome, "Braking: " + braking);

                Assert.LessOrEqual(Flat(result.Position, destination), 1.52f);
            }
        }

        [UnityTest]
        public IEnumerator AnAcceptanceRadius_EndsTheMoveThatFarAway_AndStopsTheAgent()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var destination = new Vector3(0f, 0f, 10f);

            var request = ai.MoveTo(destination, 3f);

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Success, result.Outcome);

            Assert.That(Flat(result.Position, destination), Is.InRange(2.5f, 3.02f));

            yield return Seconds(0.6f);

            Assert.Greater(Flat(pawn.transform.position, destination), 1.5f, "The agent stops: it does not walk the rest of the way.");
        }

        [UnityTest]
        public IEnumerator ALongPath_Succeeds_ThoughRemainingDistanceIsInfinityMostOfTheWay()
        {
            OpenLevel();

            BuildNavMesh(Zigzag);

            var pawn = Walker(new Vector3(0f, 0f, -18f), agent =>
            {
                agent.speed = 20f;

                agent.acceleration = 120f;

                agent.angularSpeed = 2000f;
            });

            var ai = Possess(pawn);

            var agent = pawn.GetComponent<NavMeshAgent>();

            var request = ai.MoveTo(new Vector3(0f, 0f, 18f));

            var sawInfinity = false;

            var deadline = Time.realtimeSinceStartup + 15f;

            while (request.IsActive)
            {
                sawInfinity |= agent.hasPath && float.IsInfinity(agent.remainingDistance);

                Assert.Less(Time.realtimeSinceStartup, deadline, "Timed out.");

                yield return null;
            }

            Assert.IsTrue(sawInfinity, "Unity reports an unknown remaining distance on long paths: a distance check alone never sees the end coming.");

            Assert.AreEqual(MoveOutcome.Success, ai.Results.Single().Outcome);

            Assert.Less(Flat(pawn.transform.position, new Vector3(0f, 0f, 18f)), 0.1f);
        }

        [UnityTest]
        public IEnumerator MoveTo_WhereItAlreadyIs_SucceedsOnALaterFrame_NeverInsideMoveTo()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(new Vector3(2f, 0f, 2f));

            var ai = Possess(pawn);

            yield return null;

            var request = ai.MoveTo(pawn.transform.position);

            Assert.AreEqual(0, ai.Results.Count, "Results never arrive inside MoveTo: subscribing right after it is safe.");

            var log = new MoveLog(pawn.PathFollowing);

            yield return WaitFor(() => !request.IsActive, 2f, "the move to end");

            var result = log.Single(request);

            Assert.AreEqual(MoveOutcome.Success, result.Outcome);

            Assert.AreEqual(MoveReason.AlreadyThere, result.Reason);
        }

        [UnityTest]
        public IEnumerator ADestinationSlightlyOffTheNavMesh_Succeeds_AtTheClosestWalkablePoint()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var request = ai.MoveTo(new Vector3(3f, 1.2f, 6f));

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Success, result.Outcome);

            Assert.Less(Flat(result.Position, new Vector3(3f, 0f, 6f)), 0.05f);
        }

        [UnityTest]
        public IEnumerator ADestinationFarFromAnyNavMesh_IsInvalid()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var request = ai.MoveTo(new Vector3(0f, 10f, 5f));

            Assert.AreEqual(0, ai.Results.Count, "Not inside MoveTo.");

            yield return WaitFor(() => !request.IsActive, 2f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Invalid, result.Outcome);

            Assert.AreEqual(MoveReason.DestinationNotOnNavMesh, result.Reason);
        }

        [UnityTest]
        public IEnumerator AnUnreachableDestination_IsInvalidRightAway_WithoutWalking()
        {
            OpenLevel();

            BuildNavMesh(Island);

            var pawn = Walker(new Vector3(10f, 0f, 0f));

            var ai = Possess(pawn);

            var request = ai.MoveTo(new Vector3(30f, 0f, 0f));

            yield return WaitFor(() => !request.IsActive, 2f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Invalid, result.Outcome);

            Assert.AreEqual(MoveReason.Unreachable, result.Reason);

            Assert.Less(Flat(pawn.transform.position, new Vector3(10f, 0f, 0f)), 0.3f, "It did not walk toward a place it cannot reach.");
        }

        [UnityTest]
        public IEnumerator AnUnreachableDestination_AllowingPartial_EndsPartial_AtTheClosestPoint()
        {
            OpenLevel();

            BuildNavMesh(Island);

            var pawn = Walker(new Vector3(10f, 0f, 0f));

            var ai = Possess(pawn);

            var agent = pawn.GetComponent<NavMeshAgent>();

            var request = ai.MoveTo(new Vector3(30f, 0f, 0f), allowPartial: true);

            var naiveSaidArrived = false;

            var deadline = Time.realtimeSinceStartup + 10f;

            while (request.IsActive)
            {
                // Unity moves agent.destination to the edge, so "close to agent.destination" says it arrived at the island.
                naiveSaidArrived |= agent.hasPath && Flat(agent.nextPosition, agent.destination) < 0.1f;

                Assert.Less(Time.realtimeSinceStartup, deadline, "Timed out.");

                yield return null;
            }

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Partial, result.Outcome, result.ToString());

            Assert.That(result.Position.x, Is.InRange(18.5f, 20f), "At the edge closest to the island.");

            Assert.IsTrue(naiveSaidArrived, "The naive check believes it reached a destination 10 m away.");
        }

        [UnityTest]
        public IEnumerator AnAgentThatCannotMove_EndsBlocked()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero, agent => agent.speed = 0f);

            var ai = Possess(pawn, 1f);

            var request = ai.MoveTo(new Vector3(0f, 0f, 6f));

            yield return WaitFor(() => !request.IsActive, 4f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Blocked, result.Outcome);

            Assert.GreaterOrEqual(result.Duration, 1f);
        }

        [UnityTest]
        public IEnumerator AnAgentStuckBehindAnObstacle_EndsBlocked()
        {
            OpenLevel();

            // A corridor 2 m wide (walls along x = -1.25 and x = 1.25), closed by an obstacle the NavMesh knows nothing about.
            BuildNavMesh((new Vector3(-1.25f, 1.5f, 5f), new Vector3(0.5f, 3f, 10f)), (new Vector3(1.25f, 1.5f, 5f), new Vector3(0.5f, 3f, 10f)));

            var obstacle = Thing("Obstacle", new Vector3(0f, 1f, 5f)).AddComponent<NavMeshObstacle>();

            obstacle.shape = NavMeshObstacleShape.Box;

            obstacle.size = new Vector3(3f, 2f, 1f);

            obstacle.carving = false;

            var pawn = Walker(new Vector3(0f, 0f, -2f));

            var ai = Possess(pawn, 1f);

            var request = ai.MoveTo(new Vector3(0f, 0f, 9f));

            yield return WaitFor(() => !request.IsActive, 8f, "the move to end");

            Assert.AreEqual(MoveOutcome.Blocked, ai.Results.Single().Outcome);

            Assert.Less(pawn.transform.position.z, 5f, "It never got past the obstacle.");
        }

        [UnityTest]
        public IEnumerator AnAgentThatKeepsMovingButGoesNowhere_EndsBlocked()
        {
            OpenLevel();

            BuildNavMesh();

            // Barely any push forward, a strong shove sideways every frame: it never stays put, so stillness alone never ends it.
            var pawn = Walker(Vector3.zero, agent => agent.acceleration = 0.5f);

            pawn.gameObject.AddComponent<TestDancer>();

            var ai = Possess(pawn, 1f);

            var request = ai.MoveTo(new Vector3(0f, 0f, 15f));

            yield return WaitFor(() => !request.IsActive, 8f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Blocked, result.Outcome);

            Assert.GreaterOrEqual(result.Duration, 3f, "Three Block Times inside a small area.");
        }

        [UnityTest]
        public IEnumerator AnotherAgentStandingOnTheDestination_StillEndsInSuccess()
        {
            OpenLevel();

            BuildNavMesh();

            var destination = new Vector3(0f, 0f, 5f);

            // Unity's default agent settings, as in a fresh project.
            Walker(destination, DefaultSpeed_Internal, "Standing");

            var pawn = Walker(Vector3.zero, DefaultSpeed_Internal);

            var ai = Possess(pawn);

            var agent = pawn.GetComponent<NavMeshAgent>();

            var request = ai.MoveTo(destination);

            var naiveSaidArrived = false;

            var deadline = Time.realtimeSinceStartup + 10f;

            while (request.IsActive)
            {
                naiveSaidArrived |= agent.hasPath && NaiveArrived(agent);

                Assert.Less(Time.realtimeSinceStartup, deadline, "Timed out: it never counted as arrived.");

                yield return null;
            }

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Success, result.Outcome, "It can get no closer and its body covers the destination.");

            Assert.Less(Flat(result.Position, destination), agent.radius + 0.05f);

            Assert.IsFalse(naiveSaidArrived, "Pushing against the other agent, remainingDistance never reaches the stopping distance.");
        }

        private static void DefaultSpeed_Internal(NavMeshAgent agent)
        {
            agent.speed = 3.5f;

            agent.acceleration = 8f;

            agent.angularSpeed = 120f;
        }

        [UnityTest]
        public IEnumerator ANewMove_EndsTheCurrentOneAsAborted_BeforeMoveToReturns()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var log = new MoveLog(pawn.PathFollowing);

            var first = ai.MoveTo(new Vector3(0f, 0f, 10f));

            yield return Seconds(0.3f);

            var second = ai.MoveTo(new Vector3(5f, 0f, 0f));

            var aborted = log.Single(first);

            Assert.AreEqual(MoveOutcome.Aborted, aborted.Outcome);

            Assert.AreEqual(MoveReason.NewRequest, aborted.Reason);

            Assert.IsFalse(first.IsActive);

            Assert.IsTrue(second.IsActive);

            yield return WaitFor(() => !second.IsActive, 10f, "the second move to end");

            Assert.AreEqual(MoveOutcome.Success, log.Single(second).Outcome);

            Assert.AreEqual(2, log.Count);
        }

        [UnityTest]
        public IEnumerator AHandlerThatAlwaysMovesOn_DoesNotStealTheMoveThatReplacedIt()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var ignored = new List<MoveRequest>();

            // A patrol written without looking at the outcome: whatever happened, go to the next point.
            ai.Completed = (self, result) => ignored.Add(self.MoveTo(new Vector3(-6f, 0f, 0f)));

            var patrol = ai.MoveTo(new Vector3(-6f, 0f, 0f));

            yield return Seconds(0.3f);

            var chase = ai.MoveTo(new Vector3(6f, 0f, 6f));

            Assert.AreEqual(MoveReason.NewRequest, ai.Results.Single().Reason);

            Assert.AreEqual(MoveRequest.None, ignored.Single(), "The patrol's reaction to being replaced is ignored.");

            Assert.IsTrue(chase.IsActive, "The move that replaced it is the one running.");

            Assert.IsFalse(patrol.IsActive);

            yield return WaitFor(() => !chase.IsActive, 10f, "the chase to end");

            Assert.AreEqual(MoveOutcome.Success, ai.Results[1].Outcome);

            Assert.IsTrue(ignored[1].IsValid, "Once the chase ended, the patrol goes on.");
        }

        [UnityTest]
        public IEnumerator TheNextMove_CanBeRequestedFromOnMoveCompleted()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var points = new[] { new Vector3(3f, 0f, 0f), new Vector3(3f, 0f, 3f), new Vector3(0f, 0f, 3f) };

            var next = 1;

            ai.Completed = (self, result) =>
            {
                if (result.IsSuccess && next < points.Length)
                {
                    self.MoveTo(points[next++]);
                }
            };

            ai.MoveTo(points[0]);

            yield return WaitFor(() => ai.Results.Count == 3, 15f, "three moves");

            Assert.IsTrue(ai.Results.All(result => result.IsSuccess));

            Assert.AreEqual(3, ai.Results.Select(result => result.Request.Id).Distinct().Count());

            Assert.Less(Flat(pawn.transform.position, points[2]), 0.05f);
        }

        [UnityTest]
        public IEnumerator StopMovement_EndsTheMoveAsAborted_AndStopsTheAgent()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn, 0.5f);

            var request = ai.MoveTo(new Vector3(0f, 0f, 15f));

            yield return Seconds(0.4f);

            ai.StopMovement();

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Aborted, result.Outcome);

            Assert.AreEqual(MoveReason.Stopped, result.Reason);

            Assert.IsFalse(request.IsActive);

            yield return Seconds(1f);

            var position = pawn.transform.position;

            yield return Seconds(0.3f);

            Assert.Less(Vector3.Distance(position, pawn.transform.position), 0.01f, "The agent stands still.");

            Assert.AreEqual(1, ai.Results.Count, "No Blocked later.");
        }

        [UnityTest]
        public IEnumerator APausedMove_IsNeverBlocked_AndArrivesOnceResumed()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn, 0.5f);

            var request = ai.MoveTo(new Vector3(0f, 0f, 8f));

            yield return Seconds(0.3f);

            ai.PauseMove();

            yield return Seconds(1.5f);

            Assert.AreEqual(MoveStatus.Paused, ai.MoveStatus);

            Assert.AreEqual(0, ai.Results.Count);

            ai.ResumeMove();

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            Assert.AreEqual(MoveOutcome.Success, ai.Results.Single().Outcome);
        }

        [UnityTest]
        public IEnumerator DisablingThePawn_EndsTheMoveOnce_AndDestroyingItLaterRaisesNothingElse()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var pathFollowing = Follower(pawn);

            var log = new MoveLog(pathFollowing);

            var request = pathFollowing.MoveTo(new Vector3(0f, 0f, 10f));

            yield return Seconds(0.3f);

            pawn.gameObject.SetActive(false);

            Assert.AreEqual(MoveOutcome.Aborted, log.Single(request).Outcome);

            Assert.AreEqual(MoveReason.Disabled, log.Single(request).Reason);

            Object.Destroy(pawn.gameObject);

            yield return Seconds(0.3f);

            Assert.AreEqual(1, log.Count);
        }

        [UnityTest]
        public IEnumerator DestroyingThePawn_EndsTheMoveOnce()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var log = new MoveLog(pawn.PathFollowing);

            var request = ai.MoveTo(new Vector3(0f, 0f, 10f));

            yield return Seconds(0.3f);

            Object.Destroy(pawn.gameObject);

            yield return Seconds(0.3f);

            Assert.AreEqual(MoveOutcome.Aborted, log.Single(request).Outcome);

            Assert.AreEqual(1, ai.Results.Count, "The controller heard it before going away with its Pawn.");
        }

        [UnityTest]
        public IEnumerator AWarpMidWay_DoesNotCountAsArriving_AndTheMoveStillSucceeds()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var agent = pawn.GetComponent<NavMeshAgent>();

            var destination = new Vector3(0f, 0f, 10f);

            var request = ai.MoveTo(destination);

            yield return Seconds(0.4f);

            agent.Warp(new Vector3(8f, 0f, -4f));

            Assert.IsTrue(NaiveArrived(agent), "Right after Warp the path is gone and remainingDistance is 0: the naive check says it arrived.");

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Success, result.Outcome);

            Assert.Less(Flat(result.Position, destination), 0.05f);
        }

        [UnityTest]
        public IEnumerator RebuildingTheNavMeshMidWay_DoesNotLoseTheMove()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var destination = new Vector3(0f, 0f, 12f);

            var request = ai.MoveTo(destination);

            yield return Seconds(0.5f);

            RebuildNavMesh();

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            Assert.AreEqual(MoveOutcome.Success, ai.Results.Single().Outcome);

            Assert.Less(Flat(pawn.transform.position, destination), 0.05f);
        }

        [UnityTest]
        public IEnumerator TheLevelEnding_EndsMovesAsAborted_BeforeTheControllersEndPlay()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            ai.MoveTo(new Vector3(0f, 0f, 10f));

            yield return Seconds(0.2f);

            World.EndPlay_Internal();

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Aborted, result.Outcome);

            Assert.AreEqual(MoveReason.LevelEnded, result.Reason);

            Assert.Less(IndexOf("AI.MoveCompleted Aborted (LevelEnded)"), IndexOf("AI.EndPlay"));
        }

        [UnityTest]
        public IEnumerator UnPossessing_EndsTheMove_AndTellsTheControllerThatLeft()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            ai.MoveTo(new Vector3(0f, 0f, 10f));

            yield return Seconds(0.2f);

            ai.UnPossess();

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Aborted, result.Outcome);

            Assert.AreEqual(MoveReason.ControllerChanged, result.Reason);
        }

        [UnityTest]
        public IEnumerator RestartPlayer_StopsTheMove_AndWarpsTheAgentToTheStart()
        {
            BuildNavMesh();

            var mode = Template<TestGameMode>("Mode");

            var hero = Template<TestPawn>("Hero");

            hero.gameObject.AddComponent<NavMeshAgent>();

            Set(mode, "defaultPawn", hero);

            var worldSettings = Create<WorldSettings>("World Settings");

            Set(worldSettings, "gameModeOverride", mode);

            var start = Create<PlayerStart>("Start");

            start.transform.position = new Vector3(5f, 0f, 5f);

            OpenLevel(worldSettings, start);

            yield return null;

            var player = World.GetPlayer();

            var pawn = player.Pawn;

            var request = player.MoveTo(new Vector3(-10f, 0f, -10f));

            yield return Seconds(0.6f);

            Assert.Greater(Flat(pawn.transform.position, start.transform.position), 1f, "It walked away first.");

            World.GameMode.RestartPlayer(player);

            Assert.IsTrue(request.TryGetResult(out var result), "The move ended right away.");

            Assert.AreEqual(MoveReason.Stopped, result.Reason);

            yield return Seconds(0.5f);

            Assert.Less(Flat(pawn.transform.position, start.transform.position), 0.1f, "It stays at the start: the agent neither pulled it back nor walked on.");
        }

        [UnityTest]
        public IEnumerator MoveToActor_FollowsAMovingActor_UntilItTouchesIt()
        {
            OpenLevel();

            BuildNavMesh();

            var target = Thing("Target", new Vector3(0f, 0f, 6f));

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var request = ai.MoveToActor(target.transform);

            var time = 0f;

            while (request.IsActive)
            {
                // Runs away along x for a second and a half, slower than the walker.
                if (time < 1.5f)
                {
                    target.transform.position += new Vector3(3f * Time.deltaTime, 0f, 0f);
                }

                time += Time.deltaTime;

                Assert.Less(time, 10f, "Timed out.");

                yield return null;
            }

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Success, result.Outcome);

            Assert.Greater(target.transform.position.x, 3f, "The actor moved.");

            Assert.LessOrEqual(Flat(result.Position, target.transform.position), 0.52f, "Touching it: its radius plus the acceptance.");
        }

        [UnityTest]
        public IEnumerator MoveToActor_EndsAsAborted_WhenTheActorIsDestroyed()
        {
            OpenLevel();

            BuildNavMesh();

            var target = Thing("Target", new Vector3(0f, 0f, 12f));

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var request = ai.MoveToActor(target.transform);

            yield return Seconds(0.3f);

            Object.Destroy(target);

            yield return WaitFor(() => !request.IsActive, 2f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Aborted, result.Outcome);

            Assert.AreEqual(MoveReason.TargetLost, result.Reason);
        }

        [UnityTest]
        public IEnumerator MoveToActor_ToAnObstacle_EndsTouchingIt()
        {
            OpenLevel();

            // A 2 m crate baked into the NavMesh: its footprint is a hole, so every path to it is partial.
            BuildNavMesh((new Vector3(0f, 1f, 8f), new Vector3(2f, 2f, 2f)));

            var crate = Thing("Crate", new Vector3(0f, 0f, 8f)).AddComponent<NavMeshObstacle>();

            crate.shape = NavMeshObstacleShape.Box;

            crate.size = new Vector3(2f, 2f, 2f);

            crate.center = new Vector3(0f, 1f, 0f);

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var request = ai.MoveToActor(crate);

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Success, result.Outcome, result.ToString());

            Assert.Less(Flat(result.Position, crate.transform.position), 2f, "Next to the crate.");
        }

        [UnityTest]
        public IEnumerator AnOffMeshLink_IsCrossed_AndArrivalIsNeverDeclaredMidLink()
        {
            OpenLevel();

            BuildNavMesh(Island);

            AddLink(new Vector3(19.3f, 0f, 0f), new Vector3(25.7f, 0f, 0f));

            var pawn = Walker(new Vector3(14f, 0f, 0f));

            var ai = Possess(pawn);

            var agent = pawn.GetComponent<NavMeshAgent>();

            var onLinkWhenItEnded = true;

            var crossed = false;

            ai.Completed = (self, result) => onLinkWhenItEnded = agent.isOnOffMeshLink;

            var request = ai.MoveTo(new Vector3(26.2f, 0f, 0f), 1f);

            var deadline = Time.realtimeSinceStartup + 10f;

            while (request.IsActive)
            {
                crossed |= agent.isOnOffMeshLink;

                Assert.Less(Time.realtimeSinceStartup, deadline, "Timed out.");

                yield return null;
            }

            Assert.IsTrue(crossed, "The path went through the link.");

            Assert.AreEqual(MoveOutcome.Success, ai.Results.Single().Outcome);

            Assert.IsFalse(onLinkWhenItEnded, "The destination is within 1 m of the link's end, but arrival waits until the link is crossed.");

            Assert.Greater(pawn.transform.position.x, 25f);
        }

        [UnityTest]
        public IEnumerator AnOffMeshLinkThatIsNeverFinished_EndsBlocked()
        {
            OpenLevel();

            BuildNavMesh(Island);

            AddLink(new Vector3(19.3f, 0f, 0f), new Vector3(25.7f, 0f, 0f));

            var pawn = Walker(new Vector3(16f, 0f, 0f), agent => agent.autoTraverseOffMeshLink = false);

            var ai = Possess(pawn, 1f);

            var request = ai.MoveTo(new Vector3(30f, 0f, 0f));

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            Assert.AreEqual(MoveOutcome.Blocked, ai.Results.Single().Outcome);
        }

        [UnityTest]
        public IEnumerator AnAgentAwayFromAnyNavMesh_IsInvalid_AfterBlockTime()
        {
            OpenLevel();

            BuildNavMesh();

            LogAssert.Expect(LogType.Warning, new Regex("Failed to create agent"));

            var pawn = Walker(new Vector3(0f, 50f, 0f));

            var ai = Possess(pawn, 0.5f);

            var request = ai.MoveTo(new Vector3(0f, 0f, 5f));

            Assert.AreEqual(MoveStatus.Waiting, ai.MoveStatus);

            yield return WaitFor(() => !request.IsActive, 3f, "the move to end");

            var result = ai.Results.Single();

            Assert.AreEqual(MoveOutcome.Invalid, result.Outcome);

            Assert.AreEqual(MoveReason.AgentNotOnNavMesh, result.Reason);
        }

        [UnityTest]
        public IEnumerator AMoveRequestedBeforeTheNavMeshExists_StartsOnceItIsBuilt()
        {
            OpenLevel();

            LogAssert.Expect(LogType.Warning, new Regex("Failed to create agent"));

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var request = ai.MoveTo(new Vector3(0f, 0f, 5f));

            yield return Seconds(0.3f);

            Assert.AreEqual(MoveStatus.Waiting, ai.MoveStatus);

            BuildNavMesh();

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            Assert.AreEqual(MoveOutcome.Success, ai.Results.Single().Outcome);
        }

        [UnityTest]
        public IEnumerator Notifications_GoToWhoAsked_ThenTheController_ThenListeners_ThenWaits()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            pawn.PathFollowing.MoveCompleted += result => TestGate.Log.Add("Event " + result);

            var request = ai.MoveTo(new Vector3(0f, 0f, 3f), this, static (self, result) => TestGate.Log.Add("Callback " + result));

            request.GetAwaiter().OnCompleted(() => TestGate.Log.Add("Wait"));

            yield return WaitFor(() => !request.IsActive, 10f, "the move to end");

            var callback = IndexOf("Callback Success");

            var controller = IndexOf("AI.MoveCompleted Success");

            var listeners = IndexOf("Event Success");

            var wait = IndexOf("Wait");

            Assert.Less(callback, controller);

            Assert.Less(controller, listeners);

            Assert.Less(listeners, wait);

            // A callback whose owner was destroyed is skipped; the others still hear it.
            var owner = Thing("Owner", Vector3.zero);

            var second = ai.MoveTo(new Vector3(3f, 0f, 3f), owner, static (gone, result) => TestGate.Log.Add("Dead owner"));

            Object.DestroyImmediate(owner);

            yield return WaitFor(() => !second.IsActive, 10f, "the second move to end");

            CollectionAssert.DoesNotContain(TestGate.Log, "Dead owner");

            Assert.AreEqual(2, ai.Results.Count);
        }

        [UnityTest]
        public IEnumerator AMove_CanBeWaitedForInACoroutine_OrAwaited()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Walker(Vector3.zero);

            var ai = Possess(pawn);

            var wait = ai.MoveTo(new Vector3(0f, 0f, 3f)).Wait();

            yield return wait;

            Assert.IsTrue(wait.IsDone);

            Assert.AreEqual(MoveOutcome.Success, wait.Result.Outcome);

            MoveResult? awaited = null;

            Await_Internal(ai.MoveTo(new Vector3(3f, 0f, 3f)), result => awaited = result);

            yield return WaitFor(() => awaited.HasValue, 10f, "the awaited move");

            Assert.AreEqual(MoveOutcome.Success, awaited.Value.Outcome);

            var late = MoveRequest.None.Wait();

            Assert.IsTrue(late.IsDone, "Waiting on no move ends at once.");

            Assert.AreEqual(MoveReason.NoAgent, late.Result.Reason);
        }

        private static async void Await_Internal(MoveRequest request, System.Action<MoveResult> done)
        {
            done(await request);
        }

        [UnityTest]
        public IEnumerator MoveTo_OnAPawnWithoutANavMeshAgent_RequestsNothing_AndSaysWhy()
        {
            OpenLevel();

            BuildNavMesh();

            var pawn = Create<TestPawn>("No Agent");

            var ai = Create<TestNavAI>("AI");

            ai.Possess(pawn);

            LogAssert.Expect(LogType.Warning, new Regex("needs a NavMeshAgent"));

            var request = ai.MoveTo(Vector3.one);

            Assert.IsFalse(request.IsValid);

            Assert.AreEqual(MoveRequest.None, request);

            yield return null;

            Assert.AreEqual(0, ai.Results.Count);
        }

        [UnityTest]
        public IEnumerator FiftyAgents_GoingAnywhere_EveryMoveEndsExactlyOnce()
        {
            OpenLevel();

            BuildNavMesh(Island, (new Vector3(-8f, 1f, -8f), new Vector3(4f, 2f, 4f)), (new Vector3(8f, 1f, 8f), new Vector3(3f, 2f, 6f)), (new Vector3(0f, 1f, 0f), new Vector3(6f, 2f, 1f)));

            var random = new System.Random(7);

            var issued = new HashSet<int>();

            var ended = new Dictionary<int, int>();

            var outcomes = new Dictionary<MoveOutcome, int>();

            var followers = new List<PathFollowing>();

            var running = true;

            Vector3 RandomDestination()
            {
                var roll = random.NextDouble();

                var x = (float)(random.NextDouble() * 36.0 - 18.0);

                var z = (float)(random.NextDouble() * 36.0 - 18.0);

                if (roll < 0.1)
                {
                    return new Vector3(30f + x / 5f, 0f, z / 5f);
                }

                if (roll < 0.2)
                {
                    return new Vector3(x, 1.2f, z);
                }

                return roll < 0.25 ? new Vector3(x, 10f, z) : new Vector3(x, 0f, z);
            }

            void Issue(PathFollowing pathFollowing)
            {
                var request = pathFollowing.MoveTo(RandomDestination(), (float)random.NextDouble() * 0.5f, random.Next(2) == 0);

                if (request.IsValid)
                {
                    issued.Add(request.Id);
                }
            }

            for (var i = 0; i < 50; i++)
            {
                var start = new Vector3((float)(random.NextDouble() * 34.0 - 17.0), 0f, (float)(random.NextDouble() * 34.0 - 17.0));

                var pathFollowing = Follower(Walker(start, name: "Walker " + i), 1.5f);

                pathFollowing.MoveCompleted += result =>
                {
                    ended[result.Request.Id] = ended.TryGetValue(result.Request.Id, out var count) ? count + 1 : 1;

                    outcomes[result.Outcome] = outcomes.TryGetValue(result.Outcome, out var times) ? times + 1 : 1;

                    if (running)
                    {
                        Issue(result.PathFollowing);
                    }
                };

                followers.Add(pathFollowing);
            }

            foreach (var pathFollowing in followers)
            {
                Issue(pathFollowing);
            }

            Time.timeScale = 2f;

            yield return Seconds(8f);

            running = false;

            foreach (var pathFollowing in followers)
            {
                pathFollowing.Stop();
            }

            Time.timeScale = 1f;

            yield return null;

            yield return null;

            Debug.Log($"[UEArchitecture tests] {issued.Count} moves: " + string.Join(", ", outcomes.Select(pair => pair.Key + " " + pair.Value)));

            foreach (var id in issued)
            {
                Assert.AreEqual(1, ended.TryGetValue(id, out var count) ? count : 0, $"Move {id} must end exactly once.");
            }

            Assert.AreEqual(issued.Count, ended.Count, "No event for a move that was never requested.");

            Assert.IsTrue(followers.All(pathFollowing => !pathFollowing.IsMoving));

            Assert.Greater(issued.Count, 100);

            Assert.Greater(outcomes.TryGetValue(MoveOutcome.Success, out var successes) ? successes : 0, 30);
        }

        [UnityTest]
        public IEnumerator FiftyAgents_InterruptedAtRandom_EveryMoveEndsExactlyOnce()
        {
            OpenLevel();

            BuildNavMesh(Island, (new Vector3(-8f, 1f, -8f), new Vector3(4f, 2f, 4f)), (new Vector3(0f, 1f, 0f), new Vector3(6f, 2f, 1f)));

            AddLink(new Vector3(19.3f, 0f, 3f), new Vector3(25.7f, 0f, 3f));

            var random = new System.Random(11);

            var issued = new HashSet<int>();

            var ended = new Dictionary<int, int>();

            var outcomes = new Dictionary<MoveOutcome, int>();

            var followers = new List<PathFollowing>();

            var running = true;

            Vector3 Ground()
            {
                return new Vector3((float)(random.NextDouble() * 34.0 - 17.0), 0f, (float)(random.NextDouble() * 34.0 - 17.0));
            }

            void Issue(PathFollowing pathFollowing)
            {
                var destination = random.NextDouble() < 0.15 ? new Vector3(28f, 0f, (float)(random.NextDouble() * 6.0 - 3.0)) : Ground();

                var request = pathFollowing.MoveTo(destination, (float)random.NextDouble() * 0.5f, random.Next(2) == 0);

                if (request.IsValid)
                {
                    issued.Add(request.Id);
                }
            }

            for (var i = 0; i < 50; i++)
            {
                var pathFollowing = Follower(Walker(Ground(), name: "Walker " + i), 1f);

                pathFollowing.MoveCompleted += result =>
                {
                    ended[result.Request.Id] = ended.TryGetValue(result.Request.Id, out var count) ? count + 1 : 1;

                    outcomes[result.Outcome] = outcomes.TryGetValue(result.Outcome, out var times) ? times + 1 : 1;

                    if (running)
                    {
                        Issue(result.PathFollowing);
                    }
                };

                followers.Add(pathFollowing);
            }

            foreach (var pathFollowing in followers)
            {
                Issue(pathFollowing);
            }

            var end = Time.realtimeSinceStartup + 8f;

            while (Time.realtimeSinceStartup < end)
            {
                foreach (var pathFollowing in followers)
                {
                    var walker = pathFollowing.gameObject;

                    if (!walker.activeSelf)
                    {
                        if (random.NextDouble() < 0.1)
                        {
                            walker.SetActive(true);
                        }

                        continue;
                    }

                    var roll = random.NextDouble();

                    if (roll < 0.004)
                    {
                        Issue(pathFollowing);
                    }
                    else if (roll < 0.006)
                    {
                        pathFollowing.Stop();
                    }
                    else if (roll < 0.008)
                    {
                        pathFollowing.Agent.Warp(Ground());
                    }
                    else if (roll < 0.010)
                    {
                        pathFollowing.Pause();
                    }
                    else if (roll < 0.020)
                    {
                        pathFollowing.Resume();
                    }
                    else if (roll < 0.021)
                    {
                        walker.SetActive(false);
                    }
                }

                yield return null;
            }

            running = false;

            foreach (var pathFollowing in followers)
            {
                pathFollowing.gameObject.SetActive(true);

                pathFollowing.Stop();
            }

            yield return null;

            yield return null;

            Debug.Log($"[UEArchitecture tests] {issued.Count} interrupted moves: " + string.Join(", ", outcomes.Select(pair => pair.Key + " " + pair.Value)));

            foreach (var id in issued)
            {
                Assert.AreEqual(1, ended.TryGetValue(id, out var count) ? count : 0, $"Move {id} must end exactly once.");
            }

            Assert.AreEqual(issued.Count, ended.Count, "No event for a move that was never requested.");

            Assert.IsTrue(followers.All(pathFollowing => !pathFollowing.IsMoving));

            Assert.AreEqual(0, NavigationSystem.Moving.Count, "Nothing is left ticking.");

            Assert.Greater(issued.Count, 300);
        }

        private sealed class MoveCounter
        {
            public int Value;
        }

        [UnityTest]
        public IEnumerator TickingMoves_DoesNotAllocate()
        {
            OpenLevel();

            BuildNavMesh(Zigzag);

            var followers = new List<PathFollowing>();

            for (var i = 0; i < 20; i++)
            {
                var pathFollowing = Follower(Walker(new Vector3(-15f + i * 1.5f, 0f, -18f)));

                pathFollowing.MoveTo(new Vector3(15f - i * 1.5f, 0f, 18f), allowPartial: true);

                followers.Add(pathFollowing);
            }

            yield return Seconds(0.5f);

            var frame = 1 << 24;

            void Tick()
            {
                frame++;

                foreach (var pathFollowing in followers)
                {
                    pathFollowing.Tick_Internal(0.001f, frame);
                }
            }

            Tick();

            Assert.That(Tick, Is.Not.AllocatingGCMemory());

            Assert.IsTrue(followers.All(pathFollowing => pathFollowing.Status == MoveStatus.Moving), "The measured ticks followed real paths.");
        }

        [UnityTest]
        public IEnumerator MoveToWithAStaticCallback_DoesNotAllocate()
        {
            OpenLevel();

            BuildNavMesh();

            var pathFollowing = Follower(Walker(Vector3.zero));

            var counter = new MoveCounter();

            yield return null;

            void Move()
            {
                pathFollowing.MoveTo(new Vector3(4f, 0f, 4f), counter, static (self, result) => self.Value++);

                pathFollowing.MoveTo(new Vector3(-4f, 0f, 4f), counter, static (self, result) => self.Value++);
            }

            Move();

            Assert.That(Move, Is.Not.AllocatingGCMemory());

            Assert.Greater(counter.Value, 2, "Each move ended the previous one (NewRequest) and called back.");
        }
    }
}
#endif
