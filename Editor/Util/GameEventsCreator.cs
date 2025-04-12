using UnityEditor;

namespace UEArchitecture.Editor.Util
{
    public static class GameEventsCreator
    {
        private const string Name = "GameEvents";
        
        [MenuItem("Assets/Create/UE Architecture/Create/Scriptable Object Script/GameEvent Script", false, 1)]
        public static void CreateGameEventScript()
        {
            ScriptCreator.CreateScriptAsset(Name);
        }
    }
}
