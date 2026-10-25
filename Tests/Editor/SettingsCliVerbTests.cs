using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The command-line verbs of the tag tools and the settings lists, each run twice: the second run must report nothing
    /// created or changed. And in Play mode every one of those tools refuses.
    /// </summary>
    public class SettingsCliVerbTests
    {
        private const string Folder = "Assets/__UEATempSettingsVerbs";

        private const string HiddenFolder = "Assets/__UEATempSettingsVerbs~";

        private EditorTestKit _kit;

        private EditorBuildSettingsScene[] _buildScenes;

        [SetUp]
        public void SetUp()
        {
            _buildScenes = EditorBuildSettings.scenes;

            _kit = new EditorTestKit(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            EditorBuildSettings.scenes = _buildScenes;

            _kit.Dispose();

            if (Directory.Exists(HiddenFolder))
            {
                Directory.Delete(HiddenFolder, true);
            }
        }

        [Test]
        public void Help_ListsTheSettingsVerbs()
        {
            foreach (var verb in new[] { "add-tag", "rename-tag", "remove-tag", "find-tag-usages", "generate-tag-constants", "list-levels", "list-subsystems", "add-to-build", "set-first-level" })
            {
                StringAssert.Contains(verb, UEArchitectureCli.HelpText);
            }
        }

        [Test]
        public void AddTag_Twice()
        {
            var first = Twice_Internal("add-tag", "tag=UEATest.Cli.Burning");

            Assert.IsTrue(first.Changed.Any(changed => changed.EndsWith(": gameplayTags")), "The tag list changed the first time.");

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("add-tag")).Ok, "tag= is needed.");
        }

        [Test]
        public void RenameTag_Twice_AfterAPreview()
        {
            UEArchitectureActions.AddTag("UEATest.Cli.Burning");

            var prefab = _kit.Prefab("Holder", typeof(TestTagHolder));

            var serialized = new SerializedObject(prefab.GetComponent<TestTagHolder>());

            serialized.FindProperty("gameplayTag.name").stringValue = "UEATest.Cli.Burning";

            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssetIfDirty(prefab);

            var preview = UEArchitectureCli.Execute(Parse_Internal("rename-tag", "from=UEATest.Cli", "to=UEATest.Line"));

            Assert.IsTrue(preview.Pending, "Without apply=true a rename that touches assets is a preview.");

            Assert.AreEqual(UEArchitectureCli.PendingExitCode, UEArchitectureCli.ExitCodeOf_Internal(preview));

            CollectionAssert.IsEmpty(preview.Changed);

            var first = Twice_Internal("rename-tag", "from=UEATest.Cli", "to=UEATest.Line", "apply=true");

            CollectionAssert.Contains(first.Changed, AssetDatabase.GetAssetPath(prefab) + ": 1 tag field");
        }

        [Test]
        public void RemoveTag_Twice()
        {
            UEArchitectureActions.AddTag("UEATest.Cli.Burning");

            Twice_Internal("remove-tag", "tag=UEATest.Cli.Burning", "apply=true");

            Assert.IsFalse(ProjectTags.IsKnown("UEATest.Cli.Burning"));
        }

        [Test]
        public void FindTagUsages_Twice()
        {
            UEArchitectureActions.AddTag("UEATest.Cli.Burning");

            StringAssert.Contains("Not used", Twice_Internal("find-tag-usages", "tag=UEATest.Cli").Message);
        }

        [Test]
        public void GenerateTagConstants_Twice()
        {
            UEArchitectureActions.AddTag("UEATest.Cli.Burning");

            var path = HiddenFolder + "/GameplayTags.g.cs";

            var first = Twice_Internal("generate-tag-constants", "path=" + path, "namespace=UEATestCli");

            CollectionAssert.Contains(first.Created, path);
        }

        [Test]
        public void ListVerbs_Twice()
        {
            var level = _kit.CreateLevel("Listed");

            FrameworkIndex.Invalidate();

            Assert.IsTrue(Twice_Internal("list-levels").Notes.Any(note => note.StartsWith(level.path, System.StringComparison.Ordinal)));

            Twice_Internal("list-subsystems");
        }

        [Test]
        public void BuildVerbs_Twice()
        {
            var level = _kit.CreateLevel("Built").path;

            var other = _kit.CreateLevel("Other").path;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(other, true) };

            Assert.AreEqual(1, Twice_Internal("add-to-build", "scene=" + level).Changed.Count);

            Assert.AreEqual(1, Twice_Internal("set-first-level", "scene=" + level).Changed.Count);

            Assert.AreEqual(level, EditorBuildSettings.scenes[0].path);
        }

        private static ActionResult Twice_Internal(string verb, params string[] pairs)
        {
            var first = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(first.Ok, $"{verb}: {string.Join("\n", first.Problems)}");

            Assert.AreEqual(verb, first.Verb);

            var second = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(second.Ok, $"{verb} again: {string.Join("\n", second.Problems)}");

            Assert.IsFalse(second.Pending, $"{verb} again: nothing waits.");

            CollectionAssert.IsEmpty(second.Created, $"{verb}: the second run creates nothing.");

            CollectionAssert.IsEmpty(second.Changed, $"{verb}: the second run changes nothing.");

            return first;
        }

        private static CliArguments Parse_Internal(string verb, params string[] pairs)
        {
            return CliArguments.Parse(new[] { "Unity.exe", "-uea", verb }.Concat(pairs).ToArray());
        }
    }

    /// <summary>
    /// In Play mode the tag tools and the Build Settings fixes refuse and change nothing.
    /// </summary>
    public class SettingsPlayModeGuardTests
    {
        [UnityTest]
        public IEnumerator TagAndBuildTools_RefuseInPlayMode()
        {
            yield return new EnterPlayMode();

            const string level = "Assets/__UEATempPlayGuard/Level.unity";

            foreach (var result in new[]
                     {
                         UEArchitectureActions.AddTag("UEATest.Play"), UEArchitectureActions.RenameTag("UEATest.Play", "UEATest.Stop", true),
                         UEArchitectureActions.RemoveTag("UEATest.Play", true), UEArchitectureActions.GenerateTagConstants("Assets/__UEATempPlayGuard~/GameplayTags.g.cs"),
                         UEArchitectureActions.AddSceneToBuild(level), UEArchitectureActions.SetFirstLevel(level)
                     })
            {
                Assert.IsFalse(result.Ok, result.Verb + " refuses in Play mode.");

                StringAssert.Contains("Leave Play mode first", result.Problems[0]);
            }

            yield return new ExitPlayMode();

            Assert.IsFalse(ProjectTags.IsKnown("UEATest.Play"), "Nothing was added.");

            Assert.IsFalse(Directory.Exists("Assets/__UEATempPlayGuard~"), "Nothing was written.");
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }
    }
}
