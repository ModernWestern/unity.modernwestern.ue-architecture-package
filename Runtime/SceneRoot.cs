using UnityEngine;

namespace ModernWestern.UEArchitecture
{
    public abstract class SceneRoot : MonoBehaviour
    {
        protected abstract void Construct();

        protected abstract void Init();
        
        protected abstract void Deconstruct();

        private void Awake()
        {
            if (!GameInstanceBase.Exists)
            {
                GameInstanceBase.RootScene();
            }

            Construct();
        }

        private void Start()
        {
            Init();
        }

        private void OnDestroy()
        {
            Deconstruct();
        }
    }
}