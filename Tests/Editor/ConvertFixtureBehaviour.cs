using UnityEngine;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    // A plain MonoBehaviour for the Convert to framework class tests: a field named like GameMode's "hud", and an
    // OnDestroy that Pawn and the controllers declare as virtual. Its script is only read (dry runs), never written.
    public class ConvertFixtureBehaviour : MonoBehaviour
    {
        [SerializeField] private int hud;

        [SerializeField] private float speed = 2f;

        public int Hud => hud;

        public float Speed => speed;

        private void OnDestroy()
        {
        }
    }
}
