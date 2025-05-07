using UnityEditor;

namespace ModernWestern.UEArchitecture.Editor.Util
{
    public static class SceneRootCreator
    {
        private const string Name = "SceneRoot";

        [MenuItem("Assets/Create/ModernWestern/UE Architecture/Create/SceneRoot Script", false, 1)]
        public static void CreateGameEventScript()
        {
            ScriptCreator.CreateScriptAsset(Name, null, false);
        }
    }
}
