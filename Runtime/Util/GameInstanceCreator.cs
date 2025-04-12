using UnityEditor;
using UnityEngine;

namespace UEArchitecture.Editor.Util
{
    public static class GameInstanceCreator
    {
        private const string Name = "GameInstance";

        [MenuItem("Assets/Create/UE Architecture/Create/GameInstance Script", false, 1)]
        public static void CreateGameEventScript()
        {
            ScriptCreator.CreateScriptAsset(Name, Application.productName);
        }
    }
}
