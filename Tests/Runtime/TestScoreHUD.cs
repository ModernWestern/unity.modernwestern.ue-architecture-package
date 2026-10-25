using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    // A HUD of your own with references to its children, for the editor's "Find children by name".
    public sealed class TestScoreHUD : HUD
    {
        [SerializeField] private Transform score;

        [SerializeField] private GameObject panel;

        public Transform Score => score;

        public GameObject Panel => panel;
    }
}
