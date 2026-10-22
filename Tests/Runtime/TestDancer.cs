#if UEA_NAVIGATION
using UnityEngine;
using UnityEngine.AI;

namespace ModernWestern.UEArchitecture.Tests
{
    // Shoves its agent from side to side every frame, like two agents dancing around each other: always moving, never getting anywhere.
    public sealed class TestDancer : MonoBehaviour
    {
        private NavMeshAgent _agent;

        private void Awake() => _agent = GetComponent<NavMeshAgent>();

        private void Update() => _agent.velocity = new Vector3(Mathf.Sin(Time.time * 8f) * 2.5f, 0f, 0f);
    }
}
#endif
