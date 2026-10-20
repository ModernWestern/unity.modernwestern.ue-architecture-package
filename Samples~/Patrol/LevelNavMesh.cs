using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.Samples.Patrol
{
    // A level subsystem that builds the level's NavMesh as the level begins, before the GameMode spawns anyone, and then
    // spawns a guard on every PatrolRoute. Subsystems exist in every level of a project, so this one only exists in a
    // Patrol level. (In your own project, bake the NavMesh, or use NavMeshSurface from the AI Navigation package.)
    public sealed class LevelNavMesh : WorldSubsystem
    {
        private NavMeshData _data;

        private NavMeshDataInstance _instance;

        protected override bool ShouldCreate()
        {
            return PatrolLevel.Current != null;
        }

        protected override void Initialize()
        {
            var level = PatrolLevel.Current;

            var sources = new List<NavMeshBuildSource>();

            var bounds = new Bounds(level.transform.position, Vector3.zero);

            // The level is made of scaled cubes: each one is a box source.
            foreach (var box in level.Geometry.GetComponentsInChildren<MeshRenderer>())
            {
                var shape = box.transform;

                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = shape.lossyScale,
                    transform = Matrix4x4.TRS(shape.position, shape.rotation, Vector3.one),
                    area = 0
                });

                bounds.Encapsulate(box.bounds);
            }

            bounds.Expand(2f);

            _data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);

            _instance = NavMesh.AddNavMeshData(_data);
        }

        protected override void OnWorldBeginPlay()
        {
            foreach (var route in PatrolLevel.Current.GetComponentsInChildren<PatrolRoute>())
            {
                route.SpawnGuard();
            }
        }

        protected override void Deinitialize()
        {
            _instance.Remove();

            Object.Destroy(_data);
        }
    }
}
