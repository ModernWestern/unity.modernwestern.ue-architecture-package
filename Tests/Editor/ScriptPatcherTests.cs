using NUnit.Framework;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// ScriptPatcher on fixtures: one declaration changes, in any namespace, with attributes, interfaces and generic
    /// arguments; comments and strings never change; partial, nested, repeated, generic and commented declarations are
    /// refused with the reason.
    /// </summary>
    public class ScriptPatcherTests
    {
        [Test]
        public void Reparent_AGlobalClass_ChangesItsBaseClassOnly()
        {
            const string source = "using UnityEngine;\n\npublic class GameplayScene : MonoBehaviour\n{\n    private void Start()\n    {\n    }\n}\n";

            var result = ScriptPatcher.Reparent(source, "GameplayScene", "GameMode<GameplayGameState, GameplayPlayerState>", "MonoBehaviour");

            Assert.IsTrue(result.Ok, result.Problem);

            Assert.IsTrue(result.Changed);

            Assert.AreEqual(source.Replace(": MonoBehaviour", ": GameMode<GameplayGameState, GameplayPlayerState>"), result.Source);

            Assert.AreEqual("public class GameplayScene : MonoBehaviour", result.Before);

            Assert.AreEqual("public class GameplayScene : GameMode<GameplayGameState, GameplayPlayerState>", result.After);

            Assert.AreEqual(3, result.Line);
        }

        [Test]
        public void Reparent_InANamespace_WithAttributesAndAnInterface()
        {
            const string source = "using System;\nusing UnityEngine;\n\nnamespace Sitizens.Gameplay\n{\n    [DisallowMultipleComponent]\n    [RequireComponent(typeof(Rigidbody))]\n" +
                                  "    public sealed class PlayerCharacter : UnityEngine.MonoBehaviour, ISerializationCallbackReceiver\n    {\n" +
                                  "        public void OnBeforeSerialize() { }\n\n        public void OnAfterDeserialize() { }\n    }\n}\n";

            var result = ScriptPatcher.Reparent(source, "PlayerCharacter", "Pawn", "MonoBehaviour");

            Assert.IsTrue(result.Ok, result.Problem);

            StringAssert.Contains("    public sealed class PlayerCharacter : Pawn, ISerializationCallbackReceiver\n", result.Source);

            StringAssert.Contains("[RequireComponent(typeof(Rigidbody))]", result.Source, "Attributes stay.");

            Assert.AreEqual(source.Length - "UnityEngine.MonoBehaviour".Length + "Pawn".Length, result.Source.Length, "Nothing else changed.");
        }

        [Test]
        public void Reparent_AnExistingBaseThatIsNotTheCurrentOne_IsRefused()
        {
            const string source = "public class GameplayScene : SitizensBehaviour\n{\n}\n";

            var result = ScriptPatcher.Reparent(source, "GameplayScene", "GameMode", "MonoBehaviour");

            Assert.IsFalse(result.Ok);

            StringAssert.Contains("SitizensBehaviour", result.Problem);

            Assert.AreEqual(source, result.Source, "Nothing changed.");
        }

        [Test]
        public void Reparent_AClassThatAlreadyHasTheBase_IsLeftAsItIs()
        {
            const string source = "public class ArenaMode : ModernWestern.UEArchitecture.GameMode<ArenaState, ArenaPlayerState>\n{\n}\n";

            var result = ScriptPatcher.Reparent(source, "ArenaMode", "GameMode", "MonoBehaviour");

            Assert.IsTrue(result.Ok, result.Problem);

            Assert.IsFalse(result.Changed);

            Assert.AreEqual(source, result.Source);
        }

        [Test]
        public void Reparent_LeavesCommentsAndStringsAlone()
        {
            const string source = "// class Fake : MonoBehaviour\n/* public class Hero : MonoBehaviour { } */\npublic class Hero : MonoBehaviour\n{\n" +
                                  "    private const string Text = \"class Hero : MonoBehaviour {\";\n\n    private const string Verbatim = @\"class Hero : MonoBehaviour \"\"{\"\"\";\n\n" +
                                  "    private string Interpolated => $\"{Text} class Hero : MonoBehaviour {{ {\"x\"}\";\n\n    private const char Brace = '{';\n}\n";

            var result = ScriptPatcher.Reparent(source, "Hero", "Pawn", "MonoBehaviour");

            Assert.IsTrue(result.Ok, result.Problem);

            Assert.AreEqual(source.Replace("public class Hero : MonoBehaviour\n", "public class Hero : Pawn\n"), result.Source);
        }

        [Test]
        public void Reparent_AClassWithoutABase_GetsOne_BeforeItsInterfaces()
        {
            Assert.AreEqual("public class Hero : Pawn\n{\n}\n", ScriptPatcher.Reparent("public class Hero\n{\n}\n", "Hero", "Pawn", null).Source);

            Assert.AreEqual("public class Hero : Pawn, IDisposable\n{\n}\n", ScriptPatcher.Reparent("public class Hero : IDisposable\n{\n}\n", "Hero", "Pawn", null).Source);
        }

        [TestCase("public partial class Hero : MonoBehaviour\n{\n}\n", "partial")]
        [TestCase("public class Outer\n{\n    public class Hero : MonoBehaviour\n    {\n    }\n}\n", "inside another class")]
        [TestCase("#if A\npublic class Hero : MonoBehaviour\n{\n}\n#else\npublic class Hero : MonoBehaviour\n{\n}\n#endif\n", "2 times")]
        [TestCase("public class Hero<T> : MonoBehaviour\n{\n}\n", "generic")]
        [TestCase("public class Hero /* the player */ : MonoBehaviour\n{\n}\n", "comments")]
        [TestCase("public struct Hero\n{\n}\n", "struct")]
        [TestCase("public class Villain : MonoBehaviour\n{\n}\n", "no class called Hero")]
        public void Reparent_UnusualCode_IsRefusedWithTheReason(string source, string reason)
        {
            var result = ScriptPatcher.Reparent(source, "Hero", "Pawn", "MonoBehaviour");

            Assert.IsFalse(result.Ok);

            StringAssert.Contains(reason, result.Problem);

            Assert.AreEqual(source, result.Source);
        }

        [Test]
        public void RetargetGameModeStates_ReplacesTheGenericArguments()
        {
            const string source = "namespace Game\n{\n    public class ArenaMode : GameMode<ArenaState, Game.Players.ArenaPlayerState>\n    {\n    }\n}\n";

            var result = ScriptPatcher.RetargetGameModeStates(source, "ArenaMode", "BossState", "global::Other.BossPlayerState");

            Assert.IsTrue(result.Ok, result.Problem);

            Assert.AreEqual(source.Replace("GameMode<ArenaState, Game.Players.ArenaPlayerState>", "GameMode<BossState, global::Other.BossPlayerState>"), result.Source);

            var same = ScriptPatcher.RetargetGameModeStates(result.Source, "ArenaMode", "BossState", "global::Other.BossPlayerState");

            Assert.IsTrue(same.Ok);

            Assert.IsFalse(same.Changed, "Already so.");
        }

        [Test]
        public void RetargetGameModeStates_APlainGameModeGetsItsStates_AnotherGameModeIsRefused()
        {
            var plain = ScriptPatcher.RetargetGameModeStates("public class ArenaMode : ModernWestern.UEArchitecture.GameMode\n{\n}\n", "ArenaMode", "A", "B");

            Assert.AreEqual("public class ArenaMode : ModernWestern.UEArchitecture.GameMode<A, B>\n{\n}\n", plain.Source);

            var derived = ScriptPatcher.RetargetGameModeStates("public class BossMode : ArenaMode\n{\n}\n", "BossMode", "A", "B");

            Assert.IsFalse(derived.Ok);

            StringAssert.Contains("ArenaMode", derived.Problem);
        }

        [Test]
        public void RenameClass_RenamesTheCodeOnly()
        {
            const string source = "public class GameplayScene : MonoBehaviour\n{\n    // GameplayScene is the old name\n    private string Label => \"GameplayScene\";\n\n" +
                                  "    private void Awake() => GetComponent<GameplayScene>();\n\n    private string Name => nameof(GameplayScene);\n\n    public int GameplaySceneCount;\n}\n";

            var result = ScriptPatcher.RenameClass(source, "GameplayScene", "GameplayGameMode");

            Assert.IsTrue(result.Ok, result.Problem);

            StringAssert.Contains("public class GameplayGameMode : MonoBehaviour", result.Source);

            StringAssert.Contains("GetComponent<GameplayGameMode>()", result.Source);

            StringAssert.Contains("nameof(GameplayGameMode)", result.Source);

            StringAssert.Contains("// GameplayScene is the old name", result.Source, "Comments stay.");

            StringAssert.Contains("\"GameplayScene\"", result.Source, "Strings stay.");

            StringAssert.Contains("GameplaySceneCount", result.Source, "Longer names stay.");

            Assert.IsFalse(ScriptPatcher.RenameClass(source.Replace("GameplaySceneCount", "GameplayGameMode"), "GameplayScene", "GameplayGameMode").Ok, "The new name is taken.");
        }

        [Test]
        public void EnsureUsing_AddsItOnce_AfterTheOtherUsings()
        {
            const string source = "using System;\r\nusing UnityEngine;\r\n\r\npublic class Hero : Pawn\r\n{\r\n}\r\n";

            var added = ScriptPatcher.EnsureUsing(source, "ModernWestern.UEArchitecture");

            Assert.AreEqual("using System;\r\nusing UnityEngine;\r\nusing ModernWestern.UEArchitecture;\r\n\r\npublic class Hero : Pawn\r\n{\r\n}\r\n", added.Source, "Line endings kept.");

            Assert.IsFalse(ScriptPatcher.EnsureUsing(added.Source, "ModernWestern.UEArchitecture").Changed, "Already there.");

            Assert.AreEqual("using ModernWestern.UEArchitecture;\n\npublic class Hero : Pawn\n{\n}\n", ScriptPatcher.EnsureUsing("public class Hero : Pawn\n{\n}\n", "ModernWestern.UEArchitecture").Source);

            Assert.IsFalse(ScriptPatcher.EnsureUsing("namespace ModernWestern.UEArchitecture.Game\n{\n}\n", "ModernWestern.UEArchitecture").Changed, "Inside the namespace.");
        }

        [Test]
        public void EnsureUsing_NeverGoesInsideIf_NorCountsAUsingThere()
        {
            const string Name = "ModernWestern.UEArchitecture";

            const string editorOnly = "using UnityEngine;\n#if UNITY_EDITOR\nusing UnityEditor;\n#endif\n\npublic class Hero : Pawn\n{\n}\n";

            var added = ScriptPatcher.EnsureUsing(editorOnly, Name);

            Assert.IsTrue(added.Ok, added.Problem);

            Assert.AreEqual("using UnityEngine;\nusing ModernWestern.UEArchitecture;\n#if UNITY_EDITOR\nusing UnityEditor;\n#endif\n\npublic class Hero : Pawn\n{\n}\n", added.Source,
                            "After the last using every build compiles, not before the #endif (player builds would miss it).");

            Assert.AreEqual("#if UNITY_EDITOR\nusing UnityEditor;\n#endif\nusing ModernWestern.UEArchitecture;\npublic class Hero : Pawn\n{\n}\n",
                            ScriptPatcher.EnsureUsing("#if UNITY_EDITOR\nusing UnityEditor;\n#endif\npublic class Hero : Pawn\n{\n}\n", Name).Source, "After the #if block.");

            var onlyInEditor = ScriptPatcher.EnsureUsing("#if UNITY_EDITOR\nusing ModernWestern.UEArchitecture;\n#endif\npublic class Hero : Pawn\n{\n}\n", Name);

            Assert.IsTrue(onlyInEditor.Changed, "A using inside #if is not there for every build.");

            Assert.AreEqual("#define TRACE_HERO\nusing ModernWestern.UEArchitecture;\npublic class Hero : Pawn\n{\n}\n",
                            ScriptPatcher.EnsureUsing("#define TRACE_HERO\npublic class Hero : Pawn\n{\n}\n", Name).Source, "#define must stay first.");

            var unbalanced = ScriptPatcher.EnsureUsing("using System;\n#endif\npublic class Hero : Pawn\n{\n}\n", Name);

            Assert.IsFalse(unbalanced.Ok, "Refused rather than guessed.");

            StringAssert.Contains("#endif", unbalanced.Problem);
        }

        [TestCase("public class GameplayScene : MonoBehaviour\n{\n    public static int Count;\n\n    private void Start() => Debug.Log(Sitizens.GameplayScene.Count);\n}\n", "after a dot")]
        [TestCase("public class GameplayScene : MonoBehaviour\n{\n    private string Label => $\"{nameof(GameplayScene)} ready\";\n}\n", "interpolated string")]
        [TestCase("public enum Role { GameplayScene, Other }\n\npublic class GameplayScene : MonoBehaviour\n{\n}\n", "enum value")]
        [TestCase("public class Settings\n{\n    public int GameplayScene;\n}\n\npublic class GameplayScene : MonoBehaviour\n{\n}\n", "member")]
        [TestCase("public class GameplayScene : MonoBehaviour\n{\n    private void Start()\n    {\n        var GameplayScene = 1;\n    }\n}\n", "variable")]
        public void RenameClass_ANameThatMayNotBeTheClass_IsRefused(string source, string reason)
        {
            var result = ScriptPatcher.RenameClass(source, "GameplayScene", "GameplayGameMode");

            Assert.IsFalse(result.Ok, "Refused: " + reason);

            StringAssert.Contains(reason, result.Problem);

            Assert.AreEqual(source, result.Source, "Nothing changed.");
        }

        [Test]
        public void RenameClass_TheUsualUsesOfTheClass_AreRenamed()
        {
            const string source = "public class GameplayScene : MonoBehaviour\n{\n    public static GameplayScene Instance { get; private set; }\n\n    public GameplayScene()\n    {\n    }\n\n" +
                                  "    private void Awake()\n    {\n        Instance = this as GameplayScene;\n\n        var all = new System.Collections.Generic.List<GameplayScene>();\n\n" +
                                  "        if (all.Count > 0 && Instance is GameplayScene scene)\n        {\n            Debug.Log(typeof(GameplayScene));\n        }\n    }\n}\n";

            var result = ScriptPatcher.RenameClass(source, "GameplayScene", "GameplayGameMode");

            Assert.IsTrue(result.Ok, result.Problem);

            Assert.AreEqual(source.Replace("GameplayScene", "GameplayGameMode"), result.Source);
        }

        [TestCase("var scene = MyGame.GameplayScene.Instance;", true)]
        [TestCase("using Scene = MyGame.GameplayScene;", true)]
        [TestCase("Debug.Log($\"{nameof(GameplayScene)} ready\");", true)]
        [TestCase("Debug.Log($@\"{(ok ? \"x\" : nameof(GameplayScene))}\");", true)]
        [TestCase("// GameplayScene\nvar text = \"GameplayScene\";", false)]
        [TestCase("Debug.Log($\"GameplayScene {count} {{GameplayScene}}\");", false)]
        [TestCase("var count = GameplaySceneCount;", false)]
        public void Names_CountsQualifiedNamesAndInterpolatedHoles(string source, bool names)
        {
            Assert.AreEqual(names, ClassConversion.Names(source, "GameplayScene"));
        }

        [Test]
        public void Mask_KeepsPositionsAndLineBreaks()
        {
            const string source = "a // b\n\"c\\\"d\" '\\'' /* e\nf */ g\n  #if X\nh";

            var masked = ScriptPatcher.Mask(source);

            Assert.AreEqual(source.Length, masked.Length);

            Assert.AreEqual(source.Split('\n').Length, masked.Split('\n').Length);

            Assert.AreEqual("a", masked.Substring(0, 1));

            StringAssert.DoesNotContain("b", masked);

            StringAssert.DoesNotContain("d", masked);

            StringAssert.DoesNotContain("e", masked);

            StringAssert.DoesNotContain("X", masked);

            StringAssert.Contains("g", masked);

            Assert.AreEqual('h', masked[masked.Length - 1]);
        }
    }
}
