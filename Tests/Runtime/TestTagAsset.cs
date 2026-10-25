using UnityEngine;

namespace ModernWestern.UEArchitecture.Tests
{
    /// <summary>
    /// An asset with tag fields, for the tag tools' tests.
    /// </summary>
    public sealed class TestTagAsset : ScriptableObject
    {
        public GameplayTag gameplayTag;

        public GameplayTagContainer tags = new();
    }
}
