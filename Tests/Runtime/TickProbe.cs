using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    // A MonoBehaviour that marks where Update and LateUpdate happen in the frame.
    public sealed class TickProbe : MonoBehaviour
    {
        private void Update() => TickGate.Add("MB.Update");

        private void LateUpdate() => TickGate.Add("MB.LateUpdate");
    }
}
