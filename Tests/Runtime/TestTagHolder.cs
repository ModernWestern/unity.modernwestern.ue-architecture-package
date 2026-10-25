using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    /// <summary>
    /// A component with tag fields, for the tag tools' tests (rename and usages over prefabs and levels).
    /// </summary>
    public sealed class TestTagHolder : MonoBehaviour
    {
        public GameplayTag gameplayTag;

        public GameplayTagContainer tags = new();

        public GameplayTag[] more = System.Array.Empty<GameplayTag>();
    }
}
