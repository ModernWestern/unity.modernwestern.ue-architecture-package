using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The gameplay tag tools: the pure tree, edits and constants, the picker, and adding, renaming and deleting tags
    /// over real assets (prefabs, a variant, a ScriptableObject, a level).
    /// </summary>
    public class GameplayTagToolsTests
    {
        private const string Folder = "Assets/__UEATempTags";

        // Unity does not import folders whose name ends in "~": the constants written there never compile during the run.
        private const string HiddenFolder = "Assets/__UEATempTagConstants~";

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

            if (Directory.Exists(HiddenFolder))
            {
                Directory.Delete(HiddenFolder, true);
            }
        }

        [Test]
        public void TheTree_AddsImpliedParents_InDepthFirstOrder()
        {
            var nodes = GameplayTagTree.Build(new[] { "Status.Burning", "Team", "Status.Frozen", "StatusEffect", "bad..tag", "Status.Burning", "A.B.C" });

            CollectionAssert.AreEqual(new[] { "A", "A.B", "A.B.C", "Status", "Status.Burning", "Status.Frozen", "StatusEffect", "Team" }, nodes.Select(node => node.Name));

            var status = nodes.Single(node => node.Name == "Status");

            Assert.IsFalse(status.Declared, "Status is implied by its children.");

            Assert.IsTrue(status.HasChildren);

            Assert.IsFalse(nodes.Single(node => node.Name == "StatusEffect").HasChildren, "StatusEffect is not a child of Status.");

            Assert.AreEqual(2, nodes.Single(node => node.Name == "A.B.C").Depth);

            CollectionAssert.AreEqual(new[] { "bad..tag" }, GameplayTagTree.Invalid(new[] { "Status", "bad..tag", "bad..tag" }));
        }

        [Test]
        public void TheTreeSearch_KeepsTheParentsOfAMatch()
        {
            var nodes = GameplayTagTree.Build(new[] { "Status.Burning", "Status.Frozen", "Team.Red" });

            CollectionAssert.AreEqual(new[] { "Status", "Status.Burning" }, GameplayTagTree.Filter(nodes, "burn").Select(node => node.Name));

            Assert.AreEqual(nodes.Count, GameplayTagTree.Filter(nodes, " ").Count);
        }

        [Test]
        public void Edits_AddRenameAndRemove()
        {
            var tags = GameplayTagEdits.Add(new[] { "Status.Burning" }, "Status", out var changed, out var problem);

            Assert.IsFalse(changed, "Status is already there (implied).");

            Assert.IsNull(problem);

            tags = GameplayTagEdits.Add(tags, "Team.Red", out changed, out _);

            Assert.IsTrue(changed);

            CollectionAssert.AreEqual(new[] { "Status.Burning", "Team.Red" }, tags);

            GameplayTagEdits.Add(tags, "Bad Tag", out changed, out problem);

            Assert.IsFalse(changed);

            StringAssert.Contains("not a valid tag", problem);

            var renamed = GameplayTagEdits.Rename(new[] { "Status.Burning", "Status.Frozen", "Team" }, "Status", "Condition", out var count, out problem);

            Assert.IsNull(problem);

            Assert.AreEqual(2, count, "Renaming an implied parent renames its children.");

            CollectionAssert.AreEqual(new[] { "Condition.Burning", "Condition.Frozen", "Team" }, renamed);

            StringAssert.Contains("already a project tag", GameplayTagEdits.RenameProblem(new[] { "Status", "Team" }, "Status", "Team"));

            StringAssert.Contains("under itself", GameplayTagEdits.RenameProblem(new[] { "Status" }, "Status", "Status.Old"));

            Assert.AreEqual("Condition.Burning.Hot", GameplayTagEdits.RenameValue("Status.Burning.Hot", "Status", "Condition"));

            Assert.IsNull(GameplayTagEdits.RenameValue("StatusEffect", "Status", "Condition"), "Only whole names are renamed.");

            var removed = GameplayTagEdits.Remove(new[] { "Status.Burning", "Status.Burning.Hot", "Team" }, "Status.Burning", out count);

            Assert.AreEqual(2, count, "The tag and the tags below it go.");

            CollectionAssert.AreEqual(new[] { "Team", "Status" }, removed, "The parent stays.");

            CollectionAssert.AreEqual(new[] { "Team" }, GameplayTagEdits.Remove(new[] { "Team" }, "Nope", out count));

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Constants_NestParentsAndEscapeNames()
        {
            var source = GameplayTagConstants.Generate(new[] { "Status.Burning", "Status.class", "Ability.1Up", "Tags.Tags", "Tag.Value", "Status.Status" }, "Game.Data");

            StringAssert.Contains("namespace Game.Data", source);

            StringAssert.Contains("public static class Tags", source);

            StringAssert.Contains("public static class Status", source);

            StringAssert.Contains("Tag = global::ModernWestern.UEArchitecture.GameplayTag.Request(\"Status\");", source, "A parent's own tag.");

            StringAssert.Contains(" Burning = global::ModernWestern.UEArchitecture.GameplayTag.Request(\"Status.Burning\");", source);

            StringAssert.Contains(" @class = ", source, "Keywords get '@'.");

            StringAssert.Contains(" _1Up = ", source, "A leading digit gets '_'.");

            StringAssert.Contains("public static class Tags_", source, "Not the name of the enclosing class.");

            StringAssert.Contains("public static class Tag_", source, "Not the name of the Tag field.");

            StringAssert.Contains(" Status_ = ", source, "A member is never called like its class.");

            Assert.AreEqual(source.Count(c => c == '{'), source.Count(c => c == '}'), "Balanced braces.");

            StringAssert.DoesNotContain("namespace", GameplayTagConstants.Generate(new[] { "A" }, string.Empty), "No namespace when the assembly has none.");
        }

        [Test]
        public void ConstantsPreflight_NeedsTheRuntimeReference()
        {
            var lacking = new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { "Unity.TextMeshPro" }, true);

            StringAssert.Contains("does not reference UEArchitecture.Runtime", GameplayTagConstants.AssemblyProblem(lacking, "Assets/Game/GameplayTags.g.cs"));

            Assert.IsNull(GameplayTagConstants.AssemblyProblem(new ScriptAssembly("Game", "Assets/Game/Game.asmdef", new[] { "UEArchitecture.Runtime" }, true), "x.cs"));

            Assert.IsNull(GameplayTagConstants.AssemblyProblem(new ScriptAssembly("Assembly-CSharp", string.Empty, null, false), "x.cs"), "The predefined assembly sees every auto-referenced asmdef.");
        }

        [Test]
        public void Mentions_CountWholeNamesOnly()
        {
            Assert.AreEqual(2, GameplayTagUsages.CountMentions("name: Status.Burning\n- Status\n- StatusEffect\n- Old.Status", "Status"));

            Assert.AreEqual(0, GameplayTagUsages.CountTagValues("  m_Name: Status\n  value: Status\n", "Status"), "A GameObject's name, or a value with no tag property path, is not a tag.");

            Assert.AreEqual(1, GameplayTagUsages.CountLiterals("GameplayTag.Request(\"Status.Burning\"); var x = \"Statuses\";", "Status"));
        }

        [Test]
        public void ThePicker_ListsTagsAsATree_WithNoneAndNewTag()
        {
            var dropdown = new GameplayTagDropdown(null, null, () => { }, true, new[] { "Status", "Status.Burning", "Team" });

            var root = dropdown.BuildRoot_Internal();

            var names = EditorTestKit.Children(root).Select(item => item.name).ToArray();

            Assert.AreEqual(GameplayTagDropdown.NoneLabel, names.First());

            Assert.AreEqual(GameplayTagDropdown.NewTagLabel, names.Last());

            CollectionAssert.Contains(names, "Team");

            var status = EditorTestKit.Children(root).Single(item => item.name == "Status");

            CollectionAssert.AreEqual(new[] { "Status", "Status.Burning" }, EditorTestKit.Children(status).Select(item => item.name).ToArray(), "A parent can be picked itself.");
        }

        [Test]
        public void AddTag_AddsOnce_AndRefusesBadNames()
        {
            var first = UEArchitectureActions.AddTag("UEATest.Status.Burning");

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            CollectionAssert.Contains(FrameworkIndex.Settings.GameplayTags, "UEATest.Status.Burning");

            Assert.IsTrue(ProjectTags.IsKnown("UEATest.Status"), "Its parent is offered too.");

            var again = UEArchitectureActions.AddTag("UEATest.Status");

            Assert.IsTrue(again.Ok);

            CollectionAssert.IsEmpty(again.Changed, "A parent that is already there is not added again.");

            Assert.IsFalse(UEArchitectureActions.AddTag("UEATest..Bad").Ok);
        }

        [Test]
        public void RenameTag_PreviewsThenUpdatesAssets_AndListsLevels()
        {
            UEArchitectureActions.AddTag("UEATest.Status.Burning");

            UEArchitectureActions.AddTag("UEATest.Team");

            var basePrefab = _kit.Prefab("Holder", typeof(TestTagHolder));

            SetHolder_Internal(basePrefab.GetComponent<TestTagHolder>(), "UEATest.Status.Burning", "UEATest.Status.Burning", "UEATest.Team");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

            var variant = PrefabUtility.SaveAsPrefabAsset(instance, Folder + "/Holder Variant.prefab");

            Object.DestroyImmediate(instance);

            var asset = ScriptableObject.CreateInstance<TestTagAsset>();

            AssetDatabase.CreateAsset(asset, Folder + "/Tags.asset");

            var serializedAsset = new SerializedObject(asset);

            serializedAsset.FindProperty("gameplayTag.name").stringValue = "UEATest.Status";

            var list = serializedAsset.FindProperty("tags.tags");

            list.arraySize = 2;

            list.GetArrayElementAtIndex(0).stringValue = "UEATest.Status.Burning";

            list.GetArrayElementAtIndex(1).stringValue = "UEATest.Condition.Burning";

            serializedAsset.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();

            var scene = _kit.CreateLevel("TagLevel");

            var placed = new GameObject("Holder");

            SceneManagerMove_Internal(placed, scene);

            SetHolder_Internal(placed.AddComponent<TestTagHolder>(), "UEATest.Status.Burning", null, null);

            EditorSceneManager.SaveScene(scene);

            var scenePath = scene.path;

            EditorTestKit.Close(scene);

            var sceneText = File.ReadAllText(scenePath);

            var preview = UEArchitectureActions.RenameTag("UEATest.Status", "UEATest.Condition");

            Assert.IsTrue(preview.Ok, string.Join("\n", preview.Problems));

            Assert.IsTrue(preview.Pending, "Assets would change: a preview first.");

            Assert.AreEqual(UEArchitectureCli.PendingExitCode, UEArchitectureCli.ExitCodeOf_Internal(preview));

            StringAssert.Contains("Update 4 references in 2 assets", preview.Message, "The base prefab (2) and the asset (2); the variant only inherits.");

            StringAssert.Contains("1 level listed, not changed", preview.Message);

            CollectionAssert.Contains(FrameworkIndex.Settings.GameplayTags, "UEATest.Status.Burning", "The preview changes nothing.");

            var applied = UEArchitectureActions.RenameTag("UEATest.Status", "UEATest.Condition", true);

            Assert.IsTrue(applied.Ok, string.Join("\n", applied.Problems));

            Assert.IsFalse(applied.Pending);

            CollectionAssert.Contains(FrameworkIndex.Settings.GameplayTags, "UEATest.Condition.Burning");

            CollectionAssert.DoesNotContain(FrameworkIndex.Settings.GameplayTags, "UEATest.Status.Burning");

            var holder = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(basePrefab)).GetComponent<TestTagHolder>();

            Assert.AreEqual("UEATest.Condition.Burning", holder.gameplayTag.Name);

            Assert.AreEqual("UEATest.Team", new SerializedObject(holder).FindProperty("tags.tags").GetArrayElementAtIndex(1).stringValue, "Other tags stay.");

            Assert.AreEqual("UEATest.Condition.Burning", AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(variant)).GetComponent<TestTagHolder>().gameplayTag.Name,
                            "The variant gets it from its base.");

            var renamedAsset = new SerializedObject(AssetDatabase.LoadAssetAtPath<TestTagAsset>(Folder + "/Tags.asset"));

            Assert.AreEqual("UEATest.Condition", renamedAsset.FindProperty("gameplayTag.name").stringValue);

            Assert.AreEqual(1, renamedAsset.FindProperty("tags.tags").arraySize, "The container does not hold the same tag twice.");

            Assert.AreEqual(sceneText, File.ReadAllText(scenePath), "Levels are listed, not changed.");

            Assert.IsTrue(applied.Notes.Any(note => note.Contains(scenePath)), "The level is listed.");

            var again = UEArchitectureActions.RenameTag("UEATest.Status", "UEATest.Condition", true);

            Assert.IsTrue(again.Ok, string.Join("\n", again.Problems));

            CollectionAssert.IsEmpty(again.Changed, "Running it again changes nothing.");
        }

        [Test]
        public void RemoveTag_AsksWhileUsed_AndKeepsTheParent()
        {
            UEArchitectureActions.AddTag("UEATest.Status.Burning");

            UEArchitectureActions.AddTag("UEATest.Status.Burning.Hot");

            var prefab = _kit.Prefab("Holder", typeof(TestTagHolder));

            SetHolder_Internal(prefab.GetComponent<TestTagHolder>(), "UEATest.Status.Burning.Hot", null, null);

            var preview = UEArchitectureActions.RemoveTag("UEATest.Status.Burning");

            Assert.IsTrue(preview.Pending, "Used: a preview first.");

            StringAssert.Contains("1 reference in 1 asset", preview.Message);

            CollectionAssert.Contains(FrameworkIndex.Settings.GameplayTags, "UEATest.Status.Burning");

            var removed = UEArchitectureActions.RemoveTag("UEATest.Status.Burning", true);

            Assert.IsTrue(removed.Ok, string.Join("\n", removed.Problems));

            Assert.IsFalse(ProjectTags.IsKnown("UEATest.Status.Burning"));

            Assert.IsFalse(ProjectTags.IsKnown("UEATest.Status.Burning.Hot"), "The tags below it go too.");

            Assert.IsTrue(ProjectTags.IsKnown("UEATest.Status"), "Its parent stays.");

            Assert.AreEqual("UEATest.Status.Burning.Hot", AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)).GetComponent<TestTagHolder>().gameplayTag.Name,
                            "Fields keep their value.");

            var again = UEArchitectureActions.RemoveTag("UEATest.Status.Burning", true);

            Assert.IsTrue(again.Ok);

            CollectionAssert.IsEmpty(again.Changed);
        }

        [Test]
        public void GenerateTagConstants_WritesOnce_InTheAssemblyNamespace()
        {
            UEArchitectureActions.AddTag("UEATest.Status.Burning");

            var path = HiddenFolder + "/GameplayTags.g.cs";

            var first = UEArchitectureActions.GenerateTagConstants(path, "UEATestTags");

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            CollectionAssert.Contains(first.Created, path);

            var source = File.ReadAllText(path);

            StringAssert.Contains("namespace UEATestTags", source);

            StringAssert.Contains("Request(\"UEATest.Status.Burning\")", source);

            var second = UEArchitectureActions.GenerateTagConstants(path, "UEATestTags");

            Assert.IsTrue(second.Ok);

            CollectionAssert.IsEmpty(second.Created);

            CollectionAssert.IsEmpty(second.Changed, "An up-to-date file is left alone.");

            UEArchitectureActions.AddTag("UEATest.Team");

            var third = UEArchitectureActions.GenerateTagConstants(path, "UEATestTags");

            CollectionAssert.Contains(third.Changed, path, "A new tag rewrites the file.");

            Assert.IsFalse(UEArchitectureActions.GenerateTagConstants("Packages/x.cs").Ok, "Only under Assets/.");
        }

        [Test]
        public void NewTag_FromThePickerOrTheChecklist_WaitsForItsOwnField()
        {
            var asset = ScriptableObject.CreateInstance<TestTagAsset>();

            var other = ScriptableObject.CreateInstance<TestTagAsset>();

            try
            {
                GameplayTagPopups.RequestNewTag(asset, "tags.tags", string.Empty, null);

                Assert.IsTrue(GameplayTagPopups.HasRequest_Internal(asset, "tags.tags"), "The checklist asks its field to open \"New tag…\".");

                Assert.IsFalse(GameplayTagPopups.HasRequest_Internal(asset, "gameplayTag.name"), "Another field of the same object does not open it.");

                Assert.IsFalse(GameplayTagPopups.HasRequest_Internal(other, "tags.tags"), "Nor the same field of another object.");

                Assert.IsFalse(GameplayTagPopups.ShowRequested(new Rect(0f, 0f, 100f, 18f), asset, "tags.tags"), "It opens on a repaint of the field only.");

                Assert.IsTrue(GameplayTagPopups.HasRequest_Internal(asset, "tags.tags"), "Until then the request waits.");
            }
            finally
            {
                GameplayTagPopups.ClearRequest_Internal();

                Object.DestroyImmediate(asset);

                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Usages_IncludePresetsAndOtherNativeAssets_AndRenameThem()
        {
            UEArchitectureActions.AddTag("UEATest.Status.Burning");

            Directory.CreateDirectory(Folder);

            AssetDatabase.Refresh();

            var holderObject = new GameObject("Preset source");

            var presetPath = Folder + "/Holder.preset";

            try
            {
                var holder = holderObject.AddComponent<TestTagHolder>();

                SetHolder_Internal(holder, "UEATest.Status.Burning", "UEATest.Status.Burning", null);

                AssetDatabase.CreateAsset(new UnityEditor.Presets.Preset(holder), presetPath);
            }
            finally
            {
                Object.DestroyImmediate(holderObject);
            }

            var usages = GameplayTagUsages.Find("UEATest.Status");

            Assert.IsTrue(usages.Assets.Any(asset => asset.Key == presetPath && asset.Value == 2), "The preset's field and container entry: " + usages.Summary());

            var renamed = UEArchitectureActions.RenameTag("UEATest.Status", "UEATest.Condition", true);

            Assert.IsTrue(renamed.Ok, string.Join("\n", renamed.Problems));

            var values = AssetDatabase.LoadAssetAtPath<UnityEditor.Presets.Preset>(presetPath).PropertyModifications;

            Assert.AreEqual("UEATest.Condition.Burning", values.Single(value => value.propertyPath == "gameplayTag.name").value);

            Assert.AreEqual("UEATest.Condition.Burning", values.Single(value => value.propertyPath == "tags.tags.Array.data[0]").value);
        }

        [Test]
        public void Usages_FindNamesWithLettersOutsideAscii()
        {
            const string tag = "UEATest.Quemándose";

            Assert.AreEqual(1, GameplayTagUsages.CountMentions(GameplayTagUsages.DecodeYaml("  gameplayTag:\n    name: \"UEATest.Quem\\u00E1ndose\"\n"), tag), "Unity's YAML escape.");

            Assert.AreEqual(1, GameplayTagUsages.CountTagValues(GameplayTagUsages.DecodeYaml("    name: \"UEATest.Que\\\n      m\\u00E1ndose\"\n"), tag), "A string that goes on over two lines.");

            Assert.IsTrue(UEArchitectureActions.AddTag(tag).Ok, "The runtime accepts letters outside ASCII.");

            Directory.CreateDirectory(Folder);

            AssetDatabase.Refresh();

            var asset = ScriptableObject.CreateInstance<TestTagAsset>();

            AssetDatabase.CreateAsset(asset, Folder + "/Burning.asset");

            var serialized = new SerializedObject(asset);

            serialized.FindProperty("gameplayTag.name").stringValue = tag;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, GameplayTagUsages.Find(tag).AssetReferences, File.ReadAllText(Folder + "/Burning.asset"));

            var preview = UEArchitectureActions.RenameTag(tag, "UEATest.Ardiendo");

            Assert.IsTrue(preview.Pending, "Used by an asset: a preview first.");

            Assert.IsTrue(UEArchitectureActions.RenameTag(tag, "UEATest.Ardiendo", true).Ok);

            Assert.AreEqual("UEATest.Ardiendo", new SerializedObject(AssetDatabase.LoadAssetAtPath<TestTagAsset>(Folder + "/Burning.asset")).FindProperty("gameplayTag.name").stringValue);
        }

        [Test]
        public void RenameTag_UsedOnlyByALevel_IsAPreviewToo()
        {
            UEArchitectureActions.AddTag("UEATest.Team.Red");

            var scene = _kit.CreateLevel("OnlyLevel");

            var placed = new GameObject("Holder");

            SceneManagerMove_Internal(placed, scene);

            SetHolder_Internal(placed.AddComponent<TestTagHolder>(), "UEATest.Team.Red", null, null);

            EditorSceneManager.SaveScene(scene);

            EditorTestKit.Close(scene);

            var preview = UEArchitectureActions.RenameTag("UEATest.Team", "UEATest.Side");

            Assert.IsTrue(preview.Pending, "A level uses it: a preview, exit code 2.");

            Assert.AreEqual(UEArchitectureCli.PendingExitCode, UEArchitectureCli.ExitCodeOf_Internal(preview));

            CollectionAssert.Contains(FrameworkIndex.Settings.GameplayTags, "UEATest.Team.Red", "The preview renames nothing.");

            CollectionAssert.IsEmpty(preview.Changed);
        }

        [Test]
        public void RemoveTag_OfAnImpliedParent_CountsTheTagsBelowIt()
        {
            UEArchitectureActions.AddTag("UEATest.Status.Burning");

            UEArchitectureActions.AddTag("UEATest.Status.Frozen");

            var prefab = _kit.Prefab("Holder", typeof(TestTagHolder));

            SetHolder_Internal(prefab.GetComponent<TestTagHolder>(), "UEATest.Status.Burning", null, null);

            var preview = UEArchitectureActions.RemoveTag("UEATest.Status");

            Assert.IsTrue(preview.Pending);

            StringAssert.Contains("and the 2 tag(s) below it", preview.Message, "UEATest.Status is only implied: both listed tags are below it.");

            var removed = UEArchitectureActions.RemoveTag("UEATest.Status", true);

            Assert.IsTrue(removed.Ok, string.Join("\n", removed.Problems));

            StringAssert.Contains("and 2 tag(s) below it", removed.Message);

            Assert.IsFalse(ProjectTags.IsKnown("UEATest.Status.Frozen"));
        }

        [Test]
        public void Usages_InLevelsAreTagValues_AndTheGeneratedConstantsAreLeftOut()
        {
            const string root = "Temp/__UEATempTagScan";

            const string header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";

            Directory.CreateDirectory(root);

            try
            {
                File.WriteAllText(root + "/Named.unity", header + "--- !u!1 &1\nGameObject:\n  m_Name: UEATest.Team\n");

                var tagged = header + "--- !u!114 &2\nMonoBehaviour:\n  m_Name: \n  gameplayTag:\n    name: UEATest.Team\n  tags:\n    tags:\n    - UEATest.Team.Red\n    - Other\n" +
                             "  more:\n  - name: UEATest.Team.Blue\n" +
                             "--- !u!1001 &3\nPrefabInstance:\n  m_Modification:\n    m_Modifications:\n" +
                             "    - target: {fileID: 1, guid: 0123456789abcdef0123456789abcdef, type: 3}\n      propertyPath: tags.tags.Array.data[0]\n      value: UEATest.Team\n      objectReference: {fileID: 0}\n" +
                             "    - target: {fileID: 1, guid: 0123456789abcdef0123456789abcdef, type: 3}\n      propertyPath: m_Name\n      value: UEATest.Team\n      objectReference: {fileID: 0}\n";

                File.WriteAllText(root + "/Tagged.unity", tagged);

                File.WriteAllText(root + "/GameplayTags.g.cs", GameplayTagConstants.Generate(new[] { "UEATest.Team.Red" }, "UEATestTags"));

                File.WriteAllText(root + "/Uses.cs", "class Uses { object tag = ModernWestern.UEArchitecture.GameplayTag.Request(\"UEATest.Team.Red\"); }\n");

                Assert.AreEqual(4, GameplayTagUsages.CountTagValues(tagged, "UEATest.Team"), "The field, the container entry, the array entry and the prefab override; not the GameObject's name.");

                var usages = GameplayTagUsages.Find("UEATest.Team", root);

                CollectionAssert.AreEqual(new[] { root + "/Tagged.unity" }, usages.Scenes.Select(scene => scene.Key), "A GameObject called like the tag is not a use.");

                CollectionAssert.AreEqual(new[] { root + "/Uses.cs" }, usages.Scripts.Select(script => script.Key), "The generated constants are made again, not edited.");

                Assert.IsTrue(GameplayTagConstants.IsGenerated(File.ReadAllText(root + "/GameplayTags.g.cs")));

                Assert.IsFalse(GameplayTagConstants.IsGenerated("public static class Tags { }"));

                var cancelled = GameplayTagUsages.Find("UEATest.Team", root, (done, path) => true);

                Assert.IsTrue(cancelled.Cancelled, "The progress callback can stop the search.");

                Assert.IsTrue(cancelled.IsEmpty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Constants_KeepPlainNames_AndReportClashes()
        {
            const string tagType = "global::ModernWestern.UEArchitecture.GameplayTag";

            StringAssert.Contains($" Tag_ = {tagType}.Request(\"X.Tag_\");", GameplayTagConstants.Generate(new[] { "X.Tag_", "X.Other" }, string.Empty));

            var collisions = new System.Collections.Generic.List<string>();

            var source = GameplayTagConstants.Generate(new[] { "X.Tag_", "X.Tag", "X.Other", "A.1Up", "A._1Up" }, string.Empty, GameplayTagConstants.DefaultClassName, collisions);

            StringAssert.Contains($" Tag_ = {tagType}.Request(\"X.Tag_\");", source, "Adding X.Tag does not move Tags.X.Tag_ to it.");

            StringAssert.Contains($" _1Up = {tagType}.Request(\"A._1Up\");", source, "Nor does 1Up take Tags.A._1Up.");

            Assert.AreEqual(2, collisions.Count, string.Join("\n", collisions));

            StringAssert.Contains("\"X.Tag\" and \"X.Tag_\"", collisions[1] + collisions[0]);

            UEArchitectureActions.AddTag("UEATest.Tag_");

            UEArchitectureActions.AddTag("UEATest.Tag");

            var path = HiddenFolder + "/GameplayTags.g.cs";

            var refused = UEArchitectureActions.GenerateTagConstants(path, "UEATestTags");

            Assert.IsFalse(refused.Ok, "Clashing names are refused.");

            StringAssert.Contains("would both be", refused.Problems[0]);

            Assert.IsFalse(File.Exists(path), "Nothing was written.");
        }

        [Test]
        public void GenerateTagConstants_LeavesAFileItDidNotWrite()
        {
            UEArchitectureActions.AddTag("UEATest.Status.Burning");

            Directory.CreateDirectory(HiddenFolder);

            var path = HiddenFolder + "/Tags.cs";

            const string handWritten = "public static class Tags { public const string Mine = \"mine\"; }\n";

            File.WriteAllText(path, handWritten);

            var result = UEArchitectureActions.GenerateTagConstants(path, "UEATestTags");

            Assert.IsFalse(result.Ok, "A file without the generator's header is not overwritten.");

            StringAssert.Contains("was not made by UE Architecture", result.Problems[0]);

            Assert.AreEqual(handWritten, File.ReadAllText(path));
        }

        private static void SetHolder_Internal(TestTagHolder holder, string tag, string first, string second)
        {
            var serialized = new SerializedObject(holder);

            serialized.FindProperty("gameplayTag.name").stringValue = tag;

            var list = serialized.FindProperty("tags.tags");

            list.arraySize = 0;

            foreach (var value in new[] { first, second }.Where(value => value != null))
            {
                list.arraySize++;

                list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (EditorUtility.IsPersistent(holder))
            {
                AssetDatabase.SaveAssetIfDirty(holder.gameObject);
            }
        }

        private static void SceneManagerMove_Internal(GameObject gameObject, UnityEngine.SceneManagement.Scene scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, scene);
        }
    }
}
