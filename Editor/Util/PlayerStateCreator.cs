using UnityEditor;

namespace UEArchitecture.Editor.Util
{
    public static class PlayerStateCreator
    {
        private const string Name = "PlayerState";

        [MenuItem("Assets/Create/UE Architecture/Create/Scriptable Object Script/PlayerState Script", false, 1)]
        public static void CreateGameEventScript()
        {
            ScriptCreator.CreateScriptAsset(Name);
        }
    }
}
