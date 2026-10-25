using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// Convert to framework class (plan, preview, all-or-nothing apply, run twice), Find Usages on test levels and prefabs,
    /// Change Class (kept and lost fields, Undo, only where the component was added), Change Game State and Player State
    /// Classes, and their command-line verbs run twice. Scripts are only written in a folder Unity does not import, so
    /// nothing compiles during the run.
    /// </summary>
    public class ConvertToolsTests
    {
        private const string Folder = "Assets/__UEATempConvert";

        // Unity does not import folders whose name ends in "~": scripts written there never compile during the test run.
        private const string HiddenFolder = "Assets/__UEATempConvert~";

        private const string Sitizens = "using UnityEngine;\n\nnamespace Sitizens\n{\n    public class GameplayScene : MonoBehaviour\n    {\n" +
                                        "        [SerializeField] private int round;\n\n        private void Start()\n        {\n            GetComponent<GameplayScene>();\n        }\n    }\n}\n";

        private EditorTestKit _kit;

        [SetUp]
        public void SetUp()
        {
            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            _kit.Dispose();

            foreach (var step in PendingSteps.All().Where(step => step.scriptPaths.Any(path => path.StartsWith(HiddenFolder, StringComparison.Ordinal))).ToArray())
            {
                PendingSteps.Cancel(step.id);
            }

            if (Directory.Exists(HiddenFolder))
            {
                Directory.Delete(HiddenFolder, true);
            }
        }

        [Test]
        public void Plan_AGameMode_WithNewStatesAndARename()
        {
            var plan = ClassConversion.Plan(Request_Internal(Sitizens, "GameplayGameMode"), Context_Internal());

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            Assert.IsTrue(plan.Renames);

            Assert.AreEqual(HiddenFolder + "/GameplayGameMode.cs", plan.NewScriptPath);

            StringAssert.Contains("public class GameplayGameMode : GameMode<GameplayGameState, GameplayPlayerState>", plan.NewSource);

            StringAssert.Contains("GetComponent<GameplayGameMode>()", plan.NewSource, "The class is renamed in its own code.");

            StringAssert.Contains("using ModernWestern.UEArchitecture;", plan.NewSource);

            CollectionAssert.AreEqual(new[] { HiddenFolder + "/GameplayGameState.cs", HiddenFolder + "/GameplayPlayerState.cs" }, plan.NewFiles.Select(file => file.Key).ToArray());

            StringAssert.Contains("namespace Sitizens", plan.NewFiles[0].Value, "The states go in the class's namespace.");

            StringAssert.Contains("GameplayGameState : GameState", plan.NewFiles[0].Value);

            var preview = string.Join("\n", plan.Describe());

            StringAssert.Contains("- public class GameplayScene : MonoBehaviour", preview);

            StringAssert.Contains("+ public class GameplayGameMode : GameMode<GameplayGameState, GameplayPlayerState>", preview);

            StringAssert.Contains("keeps its GUID", preview);
        }

        [Test]
        public void Plan_UsesExistingStates_AndRefusesWhatWouldBreak()
        {
            var request = Request_Internal(Sitizens, string.Empty);

            request.GameState = typeof(TestGameState).FullName;

            request.PlayerState = "TestPlayerState";

            var plan = ClassConversion.Plan(request, Context_Internal(name => name == typeof(TestGameState).FullName ? typeof(TestGameState) :
                                                                              name == "TestPlayerState" ? typeof(TestPlayerState) : null));

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            CollectionAssert.IsEmpty(plan.NewFiles, "Existing classes are named, not written.");

            StringAssert.Contains($"GameMode<global::{typeof(TestGameState).FullName}, global::{typeof(TestPlayerState).FullName}>", plan.NewSource);

            var users = ClassConversion.Plan(Request_Internal(Sitizens, "GameplayGameMode"), Context_Internal(scriptsUsing: (_, _) => new[] { "Assets/Game/Spawner.cs" }));

            Assert.IsFalse(users.Ok, "Other scripts name the class: no rename.");

            StringAssert.Contains("Spawner.cs", users.Problems[0]);

            var collision = Request_Internal(Sitizens, string.Empty);

            collision.CurrentType = typeof(ConvertFixtureBehaviour);

            var clash = ClassConversion.Plan(collision, Context_Internal());

            Assert.IsTrue(clash.Problems.Any(problem => problem.Contains("\"hud\"")), "A field named like GameMode's hud: Unity cannot keep both.");

            collision.FrameworkBase = typeof(Pawn);

            var pawn = ClassConversion.Plan(collision, Context_Internal());

            Assert.IsTrue(pawn.Ok, string.Join("\n", pawn.Problems));

            Assert.IsTrue(pawn.BaseCalls.Any(call => call.StartsWith("OnDestroy", StringComparison.Ordinal) && call.Contains("base.OnDestroy()")), string.Join("\n", pawn.BaseCalls));

            Assert.IsTrue(pawn.Notes.Any(note => note.Contains("Auto Possess AI")), "Says which new fields start with their defaults.");

            Assert.IsFalse(ClassConversion.Plan(Request_Internal(Sitizens, "Pawn"), Context_Internal()).Ok, "Not the name of a framework class.");
        }

        [Test]
        public void Apply_IsAllOrNothing_AndRunningItAgainChangesNothing()
        {
            Directory.CreateDirectory(HiddenFolder);

            var scriptPath = HiddenFolder + "/GameplayScene.cs";

            File.WriteAllText(scriptPath, Sitizens);

            File.WriteAllText(scriptPath + ".meta", "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");

            // A state file that appears after the preview: nothing at all is written.
            var blocked = ClassConversion.Plan(Request_Internal(Sitizens, "GameplayGameMode"), ConvertContext.Current);

            Assert.IsTrue(blocked.Ok, string.Join("\n", blocked.Problems));

            File.WriteAllText(HiddenFolder + "/GameplayPlayerState.cs", "// in the way\n");

            var failed = UEArchitectureActions.Convert_Internal(blocked, null, false);

            Assert.IsFalse(failed.Ok);

            StringAssert.Contains("Nothing was changed", failed.Problems[0]);

            Assert.AreEqual(Sitizens, File.ReadAllText(scriptPath), "The script is as it was.");

            Assert.IsFalse(File.Exists(HiddenFolder + "/GameplayGameState.cs"), "The file written before the failure is removed.");

            Assert.IsFalse(File.Exists(HiddenFolder + "/GameplayGameMode.cs"));

            File.Delete(HiddenFolder + "/GameplayPlayerState.cs");

            var dry = UEArchitectureActions.Convert_Internal(ClassConversion.Plan(Request_Internal(Sitizens, "GameplayGameMode"), ConvertContext.Current), null, true);

            Assert.IsTrue(dry.Ok, string.Join("\n", dry.Problems));

            StringAssert.Contains("Preview", dry.Message);

            CollectionAssert.IsEmpty(dry.Created);

            Assert.AreEqual(Sitizens, File.ReadAllText(scriptPath), "A dry run writes nothing.");

            var applied = UEArchitectureActions.Convert_Internal(ClassConversion.Plan(Request_Internal(Sitizens, "GameplayGameMode"), ConvertContext.Current), null, false);

            Assert.IsTrue(applied.Ok, string.Join("\n", applied.Problems));

            CollectionAssert.AreEquivalent(new[] { HiddenFolder + "/GameplayGameState.cs", HiddenFolder + "/GameplayPlayerState.cs" }, applied.Created);

            Assert.IsFalse(File.Exists(scriptPath), "Renamed.");

            var newPath = HiddenFolder + "/GameplayGameMode.cs";

            StringAssert.Contains("public class GameplayGameMode : GameMode<GameplayGameState, GameplayPlayerState>", File.ReadAllText(newPath));

            StringAssert.Contains("0123456789abcdef0123456789abcdef", File.ReadAllText(newPath + ".meta"), "The .meta moved with it: same GUID.");

            var again = UEArchitectureActions.Convert_Internal(ClassConversion.Plan(Request_Internal(File.ReadAllText(newPath), "GameplayGameMode", "GameplayGameMode", newPath),
                                                                                   ConvertContext.Current), null, false);

            Assert.IsTrue(again.Ok, string.Join("\n", again.Problems));

            CollectionAssert.IsEmpty(again.Created, "The second run writes nothing.");

            CollectionAssert.IsEmpty(again.Changed);

            StringAssert.Contains("already derives from GameMode", again.Message);
        }

        [Test]
        public void Apply_KeepsTheByteOrderMarkAndTheAccents()
        {
            Directory.CreateDirectory(HiddenFolder);

            var scriptPath = HiddenFolder + "/GameplayScene.cs";

            var withAccents = Sitizens.Replace("using UnityEngine;\n", "using UnityEngine;\n\n// Résumé: niño, 25 °C\n");

            var original = ScriptText.Encode(withAccents, true);

            Assert.AreEqual(0xEF, original[0]);

            File.WriteAllBytes(scriptPath, original);

            Assert.IsTrue(ScriptText.TryRead(scriptPath, scriptPath, out var text, out var problem), problem);

            Assert.IsTrue(text.Bom);

            Assert.AreEqual(withAccents, text.Text, "The byte order mark is not part of the text.");

            var request = Request_Internal(text.Text, string.Empty);

            request.SourceBytes = text.Bytes;

            request.Bom = text.Bom;

            request.States = false;

            var applied = UEArchitectureActions.Convert_Internal(ClassConversion.Plan(request, ConvertContext.Current), null, false);

            Assert.IsTrue(applied.Ok, string.Join("\n", applied.Problems));

            var written = File.ReadAllBytes(scriptPath);

            Assert.IsTrue(written.Length > 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF, "The byte order mark is kept.");

            Assert.IsTrue(ScriptText.TryDecode(written, out var after, out _), "Still UTF-8.");

            StringAssert.Contains("// Résumé: niño, 25 °C", after, "The accents are kept.");

            StringAssert.Contains("public class GameplayScene : GameMode", after);
        }

        [Test]
        public void AFileThatIsNotUtf8_IsRefused_AndAnyByteChangeStopsTheApply()
        {
            Directory.CreateDirectory(HiddenFolder);

            var scriptPath = HiddenFolder + "/GameplayScene.cs";

            // "// Café" in Windows-1252: 0xE9 is not UTF-8.
            var latin = new List<byte>(System.Text.Encoding.ASCII.GetBytes("// Caf")) { 0xE9, (byte)'\n' };

            latin.AddRange(System.Text.Encoding.ASCII.GetBytes(Sitizens));

            File.WriteAllBytes(scriptPath, latin.ToArray());

            Assert.IsFalse(ScriptText.TryRead(scriptPath, scriptPath, out _, out var problem));

            StringAssert.Contains("is not saved as UTF-8; re-save it as UTF-8 first", problem);

            // A preview of the UTF-8 file, then the same text saved with a byte order mark: the bytes changed, nothing is written.
            File.WriteAllText(scriptPath, Sitizens);

            var plan = ClassConversion.Plan(Request_Internal(Sitizens, string.Empty), ConvertContext.Current);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            var resaved = ScriptText.Encode(Sitizens, true);

            File.WriteAllBytes(scriptPath, resaved);

            var applied = UEArchitectureActions.Convert_Internal(plan, null, false);

            Assert.IsFalse(applied.Ok);

            StringAssert.Contains("changed since the preview", applied.Problems[0]);

            CollectionAssert.AreEqual(resaved, File.ReadAllBytes(scriptPath), "Nothing was written.");

            Assert.IsFalse(File.Exists(HiddenFolder + "/GameplayGameState.cs"));
        }

        [Test]
        public void TheWindowAppliesThePreviewedPlan_NotANewOne()
        {
            Directory.CreateDirectory(HiddenFolder);

            var scriptPath = HiddenFolder + "/GameplayScene.cs";

            File.WriteAllText(scriptPath, Sitizens);

            var plan = ClassConversion.Plan(Request_Internal(Sitizens, string.Empty), ConvertContext.Current);

            Assert.IsTrue(plan.Ok, string.Join("\n", plan.Problems));

            // Edited in the code editor while the preview was open.
            var edited = Sitizens.Replace("private int round;", "private int round;\n\n        [SerializeField] private int lives;");

            File.WriteAllText(scriptPath, edited);

            var applied = ConvertClassWindow.ApplyPreviewed_Internal(plan, null, (_, _) => Array.Empty<string>());

            Assert.IsFalse(applied.Ok, "The changed-since-preview check fires.");

            StringAssert.Contains("changed since the preview", applied.Problems[0]);

            Assert.AreEqual(edited, File.ReadAllText(scriptPath), "The edit is kept.");

            // A rename the other scripts started to need after the preview: refused when applying.
            File.WriteAllText(scriptPath, Sitizens);

            var rename = ClassConversion.Plan(Request_Internal(Sitizens, "GameplayGameMode"), Context_Internal());

            Assert.IsTrue(rename.Ok, string.Join("\n", rename.Problems));

            var refused = ConvertClassWindow.ApplyPreviewed_Internal(rename, null, (_, _) => new[] { "Assets/Game/Spawner.cs" });

            Assert.IsFalse(refused.Ok);

            StringAssert.Contains("Spawner.cs", refused.Problems[0]);

            Assert.AreEqual(Sitizens, File.ReadAllText(scriptPath));

            var lookups = 0;

            var cache = new ScriptsNamingCache((_, _) =>
            {
                lookups++;

                return Array.Empty<string>();
            });

            for (var keystroke = 0; keystroke < 5; keystroke++)
            {
                cache.Get("GameplayScene", scriptPath);
            }

            Assert.AreEqual(1, lookups, "The project's scripts are searched once per class name, not on every keystroke.");
        }

        [Test]
        public void RenamedObjectNames_ANameNicifiedAsItself_KeepsItsOwnPair()
        {
            var arena = UEArchitectureActions.RenamedObjectNames_Internal("Arena", "ArenaGameMode");

            Assert.AreEqual(1, arena.Count);

            Assert.AreEqual("ArenaGameMode", arena["Arena"], "Not overwritten by \"Arena Game Mode\".");

            var scene = UEArchitectureActions.RenamedObjectNames_Internal("GameplayScene", "GameplayGameMode");

            Assert.AreEqual("GameplayGameMode", scene["GameplayScene"]);

            Assert.AreEqual("Gameplay Game Mode", scene["Gameplay Scene"]);
        }

        [Test]
        public void ConvertClass_DryRunTwice_FromTheCommandLine()
        {
            var script = ScriptPath_Internal(typeof(ConvertFixtureBehaviour));

            var before = File.ReadAllText(ClassConversion.PhysicalPath(script));

            var first = Twice_Internal("convert-class", "script=" + script, "to=Pawn");

            StringAssert.Contains("Preview", first.Message);

            Assert.IsTrue(first.Notes.Any(note => note.Contains("ConvertFixtureBehaviour : Pawn")), string.Join("\n", first.Notes));

            Assert.IsTrue(first.Notes.Any(note => note.Contains("base.OnDestroy()")), "Says which methods need a base call.");

            Assert.AreEqual(before, File.ReadAllText(ClassConversion.PhysicalPath(script)), "A dry run writes nothing.");

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("convert-class", "script=" + script, "to=GameMode")).Ok, "Its hud field clashes with GameMode's.");

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("convert-class", "script=" + script, "to=Banana")).Ok);

            var already = UEArchitectureCli.Execute(Parse_Internal("convert-class", "class=" + typeof(TestPawn).FullName, "to=Pawn"));

            Assert.IsTrue(already.Ok, string.Join("\n", already.Problems));

            StringAssert.Contains("already is a Pawn", already.Message);
        }

        [Test]
        public void FindUsages_SeesLevelsAndPrefabs_DirectlyAndThroughPrefabs()
        {
            var hero = _kit.Prefab("Hero", typeof(TestPawn));

            GameObject squad;

            using (var op = Scaffold.Begin("Test squad"))
            {
                squad = op.FindOrCreatePrefab(Folder + "/Squad.prefab", "Squad", Type.EmptyTypes,
                                              root => ((GameObject)PrefabUtility.InstantiatePrefab(hero, root.scene)).transform.SetParent(root.transform, false));
            }

            var direct = _kit.CreateLevel("Direct");

            SceneManager.MoveGameObjectToScene(new GameObject("Placed").AddComponent<TestPawn>().gameObject, direct);

            EditorSceneManager.SaveScene(direct);

            var through = _kit.CreateLevel("Through");

            PrefabUtility.InstantiatePrefab(squad, through);

            EditorSceneManager.SaveScene(through);

            _kit.CreateLevel("Unrelated");

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath_Internal(typeof(TestPawn)));

            var usages = FrameworkUsages.Find(script, new[] { Folder });

            CollectionAssert.AreEquivalent(new[] { direct.path, through.path }, usages.Scenes);

            CollectionAssert.AreEquivalent(new[] { AssetDatabase.GetAssetPath(hero), AssetDatabase.GetAssetPath(squad) }, usages.Prefabs);

            if (usages.Through.TryGetValue(AssetDatabase.GetAssetPath(squad), out var via))
            {
                Assert.AreEqual(AssetDatabase.GetAssetPath(hero), via, "The squad has it through the Hero prefab.");
            }

            Assert.IsTrue(usages.Through.ContainsKey(through.path) || AssetDatabase.GetDependencies(through.path, false).Contains(AssetDatabase.GetAssetPath(script)),
                          "The level has it through a prefab.");

            Assert.IsFalse(usages.Through.ContainsKey(direct.path));

            StringAssert.StartsWith("Used in 2 scenes, 2 prefabs", usages.Summary());

            var cancelled = FrameworkUsages.Find("Assets/A.cs", new[] { "Assets/S.unity" }, new[] { "Assets/P.prefab" }, _ => new[] { "Assets/A.cs" }, (done, _, _) => done >= 1);

            Assert.IsTrue(cancelled.Cancelled, "The progress bar's Cancel stops the search.");

            var first = Twice_Internal("find-usages", "script=" + AssetDatabase.GetAssetPath(script), "folder=" + Folder);

            Assert.AreEqual(4, first.Notes.Count, string.Join("\n", first.Notes));
        }

        [Test]
        public void ChangeClass_KeepsSharedFields_ListsLostOnes_AndGoesBack()
        {
            var level = _kit.CreateLevel("ChangeClass");

            var pawn = new GameObject("Guard").AddComponent<TestPawn>();

            SceneManager.MoveGameObjectToScene(pawn.gameObject, level);

            pawn.Possessions = 3;

            pawn.AutoPossessAI = AutoPossessAI.PlacedInWorld;

            Undo.ClearAll();

            var preview = UEArchitectureActions.ChangeComponentClass(pawn, typeof(Pawn));

            Assert.IsTrue(preview.Ok, string.Join("\n", preview.Problems));

            Assert.IsTrue(preview.Notes.Any(note => note.Contains("Possessions")), "Possessions is only TestPawn's: it is lost.");

            Assert.IsInstanceOf<TestPawn>(pawn.gameObject.GetComponent<Pawn>(), "A dry run changes nothing.");

            var applied = UEArchitectureActions.ChangeComponentClass(pawn, typeof(Pawn), false);

            Assert.IsTrue(applied.Ok, string.Join("\n", applied.Problems));

            var changed = GameObject.Find("Guard").GetComponent<Pawn>();

            Assert.AreEqual(typeof(Pawn), changed.GetType());

            Assert.AreEqual(AutoPossessAI.PlacedInWorld, changed.AutoPossessAI, "A field both classes have keeps its value.");

            StringAssert.Contains("Change Class again restores the class; the lost fields start empty", applied.Message, "It does not promise the lost fields back.");

            var again = UEArchitectureActions.ChangeComponentClass(changed, typeof(Pawn), false);

            Assert.IsTrue(again.Ok);

            CollectionAssert.IsEmpty(again.Changed, "Already a Pawn.");

            var back = UEArchitectureActions.ChangeComponentClass(changed, typeof(TestPawn), false);

            Assert.IsTrue(back.Ok, string.Join("\n", back.Problems));

            Assert.AreEqual(typeof(TestPawn), GameObject.Find("Guard").GetComponent<Pawn>().GetType(), "Change Class again restores the class.");

            Assert.AreEqual(0, GameObject.Find("Guard").GetComponent<TestPawn>().Possessions, "The lost field starts empty.");

            var prefab = _kit.Prefab("Turret", typeof(TestPawn));

            var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, level);

            var inherited = UEArchitectureActions.ChangeComponentClass(copy.GetComponent<Pawn>(), typeof(Pawn), false);

            Assert.IsFalse(inherited.Ok, "A copy's component comes from its prefab.");

            StringAssert.Contains("Turret.prefab", inherited.Problems[0]);

            var onAsset = UEArchitectureActions.ChangeComponentClass(prefab.GetComponent<Pawn>(), typeof(Pawn), false);

            Assert.IsTrue(onAsset.Ok, string.Join("\n", onAsset.Problems));

            Assert.AreEqual(typeof(Pawn), AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)).GetComponent<Pawn>().GetType(), "A prefab asset where it was added.");

            CollectionAssert.Contains(ConvertMenus.ChangeClassCandidates_Internal(typeof(TestPawn)), typeof(Pawn));

            CollectionAssert.DoesNotContain(ConvertMenus.ChangeClassCandidates_Internal(typeof(Pawn)), typeof(TestPawn), "Test classes are left out.");
        }

        [Test]
        public void ChangeClass_Twice_FromTheCommandLine()
        {
            var level = _kit.CreateLevel("ChangeClassCli");

            SceneManager.MoveGameObjectToScene(new GameObject("Start").AddComponent<PlayerStart>().gameObject, level);

            EditorSceneManager.SaveScene(level);

            var dry = UEArchitectureCli.Execute(Parse_Internal("change-class", "target=" + level.path + "#Start", "class=" + typeof(TestPawn).FullName));

            Assert.IsTrue(dry.Ok, string.Join("\n", dry.Problems));

            var path = AssetDatabase.GetAssetPath(_kit.Prefab("Runner", typeof(TestPawn)));

            Twice_Internal("change-class", "target=" + path, "class=Pawn", "apply=true");

            Assert.AreEqual(typeof(Pawn), AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Pawn>().GetType());
        }

        [Test]
        public void ChangeGameModeStates_PreviewsTheLine_AndTwiceFromTheCommandLine()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath_Internal(typeof(TestGameMode)));

            var before = File.ReadAllText(ClassConversion.PhysicalPath(AssetDatabase.GetAssetPath(script)));

            var same = UEArchitectureActions.ChangeGameModeStates(script, typeof(TestGameState), typeof(TestPlayerState));

            Assert.IsTrue(same.Ok, string.Join("\n", same.Problems));

            StringAssert.Contains("already uses", same.Message);

            var preview = UEArchitectureActions.ChangeGameModeStates(script, typeof(GameState), typeof(PlayerState));

            Assert.IsTrue(preview.Ok, string.Join("\n", preview.Problems));

            Assert.IsTrue(preview.Notes.Any(note => note.Contains("GameMode<GameState, PlayerState>")), string.Join("\n", preview.Notes));

            Assert.AreEqual(before, File.ReadAllText(ClassConversion.PhysicalPath(AssetDatabase.GetAssetPath(script))), "A preview writes nothing.");

            Assert.IsFalse(UEArchitectureActions.ChangeGameModeStates(script, typeof(TestPlayerState), typeof(TestPlayerState)).Ok, "A PlayerState is not a GameState.");

            Twice_Internal("change-gamemode-states", "class=" + typeof(TestGameMode).FullName, "game-state=TestGameState", "player-state=TestPlayerState", "apply=true");
        }

        [Test]
        public void Help_ListsTheConvertVerbs()
        {
            var help = UEArchitectureCli.Execute(Parse_Internal("help")).Message;

            foreach (var verb in new[] { "convert-class", "find-usages", "change-class", "change-gamemode-states", "set-auto-possess-player" })
            {
                StringAssert.Contains(verb, help);
            }
        }

        private static ConvertRequest Request_Internal(string source, string newName, string className = "GameplayScene", string path = HiddenFolder + "/GameplayScene.cs")
        {
            return new ConvertRequest
            {
                ScriptPath = path,
                Source = source,
                ClassName = className,
                Namespace = "Sitizens",
                CurrentBase = "MonoBehaviour",
                FrameworkBase = typeof(GameMode),
                NewClassName = newName
            };
        }

        private static ConvertContext Context_Internal(Func<string, Type> findType = null, Func<string, string, IReadOnlyList<string>> scriptsUsing = null)
        {
            return new ConvertContext(findType ?? (_ => null), _ => false, scriptsUsing ?? ((_, _) => Array.Empty<string>()));
        }

        private static string ScriptPath_Internal(Type type)
        {
            return AssetDatabase.FindAssets(type.Name + " t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith("/" + type.Name + ".cs", StringComparison.Ordinal));
        }

        private static ActionResult Twice_Internal(string verb, params string[] pairs)
        {
            var first = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(first.Ok, $"{verb}: {string.Join("\n", first.Problems)}");

            Assert.AreEqual(verb, first.Verb);

            Assert.AreEqual(0, UEArchitectureCli.ExitCodeOf_Internal(first));

            var second = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(second.Ok, $"{verb} again: {string.Join("\n", second.Problems)}");

            CollectionAssert.IsEmpty(second.Created, $"{verb}: the second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed, $"{verb}: the second run changes nothing.");

            return first;
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }
}
