using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class CliTests
    {
        private const string Folder = "Assets/__UEATempCli";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);

            FrameworkIndex.Invalidate();
        }

        [Test]
        public void Parse_ReadsTheVerbAndItsPairs()
        {
            var arguments = CliArguments.Parse(new[]
            {
                "Unity.exe", "-batchmode", "-executeMethod", "ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run",
                "-uea", "Create-Prefab", "class=ArenaPawn", "Path=\"Assets/My Prefabs/ArenaPawn.prefab\"", "value=a=b", "-quit", "-logFile", "log.txt"
            });

            CollectionAssert.IsEmpty(arguments.Problems);

            Assert.AreEqual("create-prefab", arguments.Verb);

            Assert.AreEqual("ArenaPawn", arguments.Get("class"));

            Assert.AreEqual("Assets/My Prefabs/ArenaPawn.prefab", arguments.Get("path"), "Keys ignore case and quotes are removed.");

            Assert.AreEqual("a=b", arguments.Get("value"), "Only the first '=' splits.");

            Assert.IsNull(arguments.Get("log.txt"), "Parsing stops at the next -flag.");

            Assert.IsTrue(arguments.HasQuit);
        }

        [Test]
        public void Parse_ReportsWhatIsWrong()
        {
            Assert.IsNotEmpty(CliArguments.Parse(new[] { "Unity.exe", "-batchmode" }).Problems, "No -uea.");

            Assert.IsNotEmpty(CliArguments.Parse(new[] { "Unity.exe", "-uea", "-quit" }).Problems, "No verb.");

            var bad = CliArguments.Parse(new[] { "Unity.exe", "-uea", "assign", "target", "property=hud", "property=hud" });

            Assert.AreEqual(2, bad.Problems.Count, "A token without '=' and a repeated key.");

            Assert.IsFalse(bad.HasQuit);

            Assert.IsTrue(CliArguments.Parse(new[] { "-uea", "new-script", "prefab=Yes" }).Flag("prefab"));

            Assert.IsFalse(CliArguments.Parse(new[] { "-uea", "new-script", "prefab=no" }).Flag("prefab"));
        }

        [TestCase("Assets/Scenes/Boss.unity#World Settings", true, "Assets/Scenes/Boss.unity", "World Settings")]
        [TestCase("Assets/Scenes/Boss.unity#Canvas/Carry Bar/", true, "Assets/Scenes/Boss.unity", "Canvas/Carry Bar")]
        [TestCase("Assets/Prefabs/UI/HUD.prefab", true, "Assets/Prefabs/UI/HUD.prefab", "")]
        [TestCase("Assets\\Prefabs\\UI\\HUD.prefab#Bar", true, "Assets/Prefabs/UI/HUD.prefab", "Bar")]
        [TestCase("Assets/Resources/UEArchitectureSettings.asset", true, "Assets/Resources/UEArchitectureSettings.asset", "")]
        [TestCase("Assets/Scenes/Boss.unity", false, null, null)]
        [TestCase("Scenes/Boss.unity#World Settings", false, null, null)]
        [TestCase("Assets/Resources/UEArchitectureSettings.asset#Child", false, null, null)]
        [TestCase("Assets/Scenes#World Settings", false, null, null)]
        [TestCase("Assets/Scenes/Boss.unity#Canvas//Bar", false, null, null)]
        public void ObjectAddresses_AreParsed(string text, bool valid, string assetPath, string hierarchyPath)
        {
            Assert.AreEqual(valid, ObjectAddress.TryParse(text, out var address, out var problem), problem);

            if (!valid)
            {
                Assert.IsNotEmpty(problem);

                return;
            }

            Assert.AreEqual(assetPath, address.AssetPath);

            Assert.AreEqual(hierarchyPath, address.HierarchyPath);
        }

        [Test]
        public void Execute_ReportsUnknownVerbsAndMissingKeys()
        {
            var unknown = UEArchitectureCli.Execute(CliArguments.Parse(new[] { "-uea", "dance" }));

            Assert.IsFalse(unknown.Ok);

            Assert.AreEqual("dance", unknown.Verb);

            var missing = UEArchitectureCli.Execute(CliArguments.Parse(new[] { "-uea", "create-prefab" }));

            StringAssert.Contains("class=", missing.Problems.Single());

            var noClass = UEArchitectureCli.Execute(CliArguments.Parse(new[] { "-uea", "create-prefab", "class=NoSuchClassAnywhere" }));

            StringAssert.Contains("NoSuchClassAnywhere", noClass.Problems.Single());

            var help = UEArchitectureCli.Execute(CliArguments.Parse(new[] { "-uea", "help" }));

            Assert.IsTrue(help.Ok);

            StringAssert.Contains("complete-pending", help.Message);
        }

        [Test]
        public void Execute_CreatesPrefabsAndFillsTheirFields()
        {
            var controllerPath = Folder + "/TestController.prefab";

            var gameModePath = Folder + "/TestGameMode.prefab";

            var created = UEArchitectureCli.Execute(Parse_Internal("create-prefab", "class=" + typeof(TestController).FullName, "path=" + controllerPath));

            Assert.IsTrue(created.Ok, string.Join("\n", created.Problems));

            CollectionAssert.Contains(created.Created, controllerPath);

            var again = UEArchitectureCli.Execute(Parse_Internal("create-prefab", "class=" + typeof(TestController).FullName, "path=" + controllerPath));

            Assert.IsTrue(again.Ok);

            CollectionAssert.IsEmpty(again.Created, "The second run creates nothing.");

            UEArchitectureCli.Execute(Parse_Internal("create-prefab", "class=TestGameMode", "path=" + gameModePath));

            var assigned = UEArchitectureCli.Execute(Parse_Internal("assign", "target=" + gameModePath, "property=playerController", "value=" + controllerPath));

            Assert.IsTrue(assigned.Ok, string.Join("\n", assigned.Problems));

            var gameMode = AssetDatabase.LoadAssetAtPath<GameObject>(gameModePath).GetComponent<GameMode>();

            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(controllerPath).GetComponent<PlayerController>(), gameMode.PlayerControllerPrefab);

            var number = UEArchitectureCli.Execute(Parse_Internal("assign", "target=" + gameModePath, "property=playersAtStart", "value=2"));

            Assert.IsTrue(number.Ok, string.Join("\n", number.Problems));

            Assert.AreEqual(2, gameMode.PlayersAtStart);

            var cleared = UEArchitectureCli.Execute(Parse_Internal("assign", "target=" + gameModePath, "property=playerController", "value=none"));

            Assert.IsTrue(cleared.Ok);

            Assert.IsNull(gameMode.PlayerControllerPrefab);

            var wrongField = UEArchitectureCli.Execute(Parse_Internal("assign", "target=" + gameModePath, "property=nope", "value=1"));

            Assert.IsFalse(wrongField.Ok);

            var json = ActionResult.FromJson(assigned.ToJson());

            Assert.AreEqual("assign", json.Verb);

            Assert.IsTrue(json.Ok);

            CollectionAssert.AreEqual(assigned.Changed, json.Changed);
        }

        [Test]
        public void FindComponentType_TakesFullOrPlainNames()
        {
            Assert.AreEqual(typeof(TestPawn), UEArchitectureCli.FindComponentType("TestPawn", out _));

            Assert.AreEqual(typeof(Pawn), UEArchitectureCli.FindComponentType(typeof(Pawn).FullName, out _));

            Assert.IsNull(UEArchitectureCli.FindComponentType("TestGameState", out var problem), "Not a component.");

            Assert.IsNotEmpty(problem);
        }

        [Test]
        public void CommandLines_ForCopyCliCommand_ParseBack()
        {
            var command = UEArchitectureCli.CommandLine("fill-scene-references", new KeyValuePair<string, string>("target", "Assets/Scenes/Boss Level.unity#World Settings"),
                                                        new KeyValuePair<string, string>("component", "GameMode"), new KeyValuePair<string, string>("empty", string.Empty));

            StringAssert.Contains(" -executeMethod ModernWestern.UEArchitecture.EditorTools.UEArchitectureCli.Run -uea fill-scene-references ", command);

            StringAssert.StartsWith("-batchmode -projectPath ", command,
                                    "Only the arguments, like ObserverVM's: Unity.app is a folder on macOS, and PowerShell needs '&' before a quoted Unity.exe.");

            StringAssert.DoesNotContain(EditorApplication.applicationPath, command);

            StringAssert.Contains("\"target=Assets/Scenes/Boss Level.unity#World Settings\"", command, "Values with spaces are quoted.");

            StringAssert.Contains("\"remove=a;b\"", UEArchitectureCli.CommandLine("remove-files", new KeyValuePair<string, string>("remove", "a;b")), "';' would end the command in a shell.");

            StringAssert.EndsWith(" -quit", command);

            var parsed = CliArguments.Parse(SplitLikeAShell(command));

            CollectionAssert.IsEmpty(parsed.Problems);

            Assert.AreEqual("fill-scene-references", parsed.Verb);

            Assert.AreEqual("Assets/Scenes/Boss Level.unity#World Settings", parsed.Get("target"));

            Assert.AreEqual("GameMode", parsed.Get("component"));

            Assert.IsNull(parsed.Get("empty"), "Empty values are left out.");

            Assert.IsTrue(parsed.HasQuit);

            Assert.IsEmpty(UEArchitectureCli.Address(null));

            var holder = new GameObject("Unsaved");

            try
            {
                if (string.IsNullOrEmpty(holder.scene.path))
                {
                    Assert.IsEmpty(UEArchitectureCli.Address(holder), "An object of a level that was never saved has no address.");
                }
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void RemoveFiles_MovesGeneratedFilesToTheTrashAndKeepsTheRest()
        {
            GameObject prefab;

            ActionResult made;

            using (var op = Scaffold.Begin("Make a file to keep"))
            {
                Assert.IsNotNull(op.FindOrCreatePrefab(Folder + "/Keep.prefab", "Keep", new[] { typeof(TestPawn) }));
            }

            using (var op = Scaffold.Begin("Make files"))
            {
                prefab = op.FindOrCreatePrefab(Folder + "/Made/TestPawn.prefab", "TestPawn", new[] { typeof(TestPawn) });

                made = op.Result;
            }

            Assert.IsNotNull(prefab);

            CollectionAssert.AreEquivalent(new[] { Folder + "/Made", Folder + "/Made/TestPawn.prefab" }, made.Created);

            var paths = string.Join(";", made.Created.Append(Folder));

            var result = UEArchitectureCli.Execute(Parse_Internal("remove-files", $"paths={paths};Assets/Scenes/Boss.unity#World Settings;Packages/com.example/X.prefab"));

            CollectionAssert.Contains(result.Changed, Folder + "/Made/TestPawn.prefab (moved to the trash)");

            CollectionAssert.Contains(result.Changed, Folder + "/Made (moved to the trash)", "The folder it made, now empty.");

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Made/TestPawn.prefab"));

            Assert.IsFalse(AssetDatabase.IsValidFolder(Folder + "/Made"));

            Assert.IsTrue(AssetDatabase.IsValidFolder(Folder), "A folder that holds other things is kept.");

            Assert.AreEqual(1, result.Problems.Count, "Only Assets/ is touched: " + string.Join("\n", result.Problems));

            var nothing = UEArchitectureActions.RemoveCreatedFiles(new[] { Folder + "/Made/TestPawn.prefab" });

            CollectionAssert.IsEmpty(nothing.Changed, "Already gone.");
        }

        [Test]
        public void Help_ListsTheExitCodesAndEveryNewVerb()
        {
            var help = UEArchitectureCli.Execute(Parse_Internal("help"));

            StringAssert.Contains("remove-files", help.Message);

            StringAssert.Contains("2 still pending", help.Message);

            StringAssert.Contains("timeout", help.Message);
        }

        /// <summary>
        /// Splits a command line the way a shell does: spaces separate arguments, quotes keep them whole and are removed.
        /// </summary>
        internal static string[] SplitLikeAShell(string command)
        {
            var arguments = new List<string>();

            var current = new System.Text.StringBuilder();

            var quoted = false;

            var started = false;

            foreach (var character in command)
            {
                if (character == '"')
                {
                    quoted = !quoted;

                    started = true;

                    continue;
                }

                if (character == ' ' && !quoted)
                {
                    if (started)
                    {
                        arguments.Add(current.ToString());

                        current.Clear();

                        started = false;
                    }

                    continue;
                }

                current.Append(character);

                started = true;
            }

            if (started)
            {
                arguments.Add(current.ToString());
            }

            return arguments.ToArray();
        }

        private static CliArguments Parse_Internal(params string[] verbAndPairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea" }.Concat(verbAndPairs).ToArray());
        }
    }
}
