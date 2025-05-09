﻿using UnityEditor;

namespace ModernWestern.UEArchitecture.Editor.Util
{
    public static class GameStateCreator
    {
        private const string Name = "GameState";
        
        [MenuItem("Assets/Create/ModernWestern/UE Architecture/Create/Scriptable Object Script/GameState Script", false, 1)]
        public static void CreateGameEventScript()
        {
            ScriptCreator.CreateScriptAsset(Name);
        }
    }
}
