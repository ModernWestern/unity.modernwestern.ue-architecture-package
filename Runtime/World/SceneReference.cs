using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// A scene picked in the Inspector (drag the scene asset), instead of typing its name. It keeps working if the scene is
    /// renamed or moved, and the Inspector warns (with a fix button) when the scene is not in Build Settings.
    /// <code>[SerializeField] private SceneReference nextLevel;   ...   Levels.Open(nextLevel);</code>
    /// </summary>
    [Serializable]
    public sealed class SceneReference : ISerializationCallbackReceiver
    {
        [SerializeField] private Object asset;

        [SerializeField] private string path = string.Empty;

        [NonSerialized] private string _name;

        [NonSerialized] private string _namePath;

        public SceneReference()
        {
        }

        public SceneReference(string scenePath)
        {
            path = scenePath ?? string.Empty;
        }

        /// <summary>
        /// Path of the scene, e.g. "Assets/Scenes/Level 1.unity". What Levels.Open and SceneManager use.
        /// </summary>
        public string Path
        {
            get
            {
#if UNITY_EDITOR
                // In the Editor, follow renames and moves even if the Inspector was never opened.
                if (asset != null)
                {
                    var current = UnityEditor.AssetDatabase.GetAssetPath(asset);

                    if (!string.IsNullOrEmpty(current))
                    {
                        path = current;
                    }
                }
#endif
                return path;
            }
        }

        /// <summary>
        /// Name of the scene, e.g. "Level 1" (cached, no allocation after the first call).
        /// </summary>
        public string Name
        {
            get
            {
                var current = Path;

                if (!ReferenceEquals(current, _namePath))
                {
                    _namePath = current;

                    _name = System.IO.Path.GetFileNameWithoutExtension(current);
                }

                return _name;
            }
        }

        /// <summary>
        /// True when a scene is assigned.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Path);

        public static implicit operator string(SceneReference scene)
        {
            return scene?.Path;
        }

        public override string ToString()
        {
            return IsValid ? Name : "(no scene)";
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
#if UNITY_EDITOR
            // Serialization can run on a loading thread, where the AssetDatabase cannot be used.
            if (asset != null && UnityEditorInternal.InternalEditorUtility.CurrentThreadIsMainThread())
            {
                var current = UnityEditor.AssetDatabase.GetAssetPath(asset);

                if (!string.IsNullOrEmpty(current))
                {
                    path = current;
                }
            }
#endif
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
        }
    }
}
