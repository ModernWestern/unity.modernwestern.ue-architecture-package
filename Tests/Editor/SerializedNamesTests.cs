using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The editor tools write these serialized fields by name. A rename without [FormerlySerializedAs] breaks them
    /// silently, so every name is checked here.
    /// </summary>
    public class SerializedNamesTests
    {
        [TestCase(typeof(GameMode), "playerController")]
        [TestCase(typeof(GameMode), "defaultPawn")]
        [TestCase(typeof(GameMode), "hud")]
        [TestCase(typeof(GameMode), "playersAtStart")]
        [TestCase(typeof(WorldSettings), "gameModeOverride")]
        [TestCase(typeof(UEArchitectureSettings), "gameInstance")]
        [TestCase(typeof(UEArchitectureSettings), "defaultGameMode")]
        [TestCase(typeof(UEArchitectureSettings), "loadingScreen")]
        [TestCase(typeof(Pawn), "autoPossessPlayer")]
        [TestCase(typeof(Pawn), "autoPossessAI")]
        [TestCase(typeof(Pawn), "aiController")]
        [TestCase(typeof(LoadingScreen), "canvasGroup")]
        [TestCase(typeof(PlayerStart), "startTag")]
        public void EveryFieldTheToolsWrite_Exists(Type type, string propertyPath)
        {
            Object target = null;

            GameObject holder = null;

            try
            {
                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    target = ScriptableObject.CreateInstance(type);
                }
                else
                {
                    holder = new GameObject("SerializedNames") { hideFlags = HideFlags.HideAndDontSave };

                    target = holder.AddComponent(type);
                }

                Assert.IsNotNull(new SerializedObject(target).FindProperty(propertyPath), $"{type.Name}.{propertyPath}");

                Assert.IsNotNull(Scaffold.FieldType(type, propertyPath), $"{type.Name}.{propertyPath} field type");
            }
            finally
            {
                if (holder != null)
                {
                    Object.DestroyImmediate(holder);
                }
                else if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }
        }
    }
}
