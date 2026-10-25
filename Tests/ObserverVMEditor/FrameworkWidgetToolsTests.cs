using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ModernWestern.UI.ObserverVM;
using ModernWestern.UI.ObserverVM.EditorTools;
using ModernWestern.UEArchitecture.Tests;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    /// <summary>
    /// The ObserverVM integration on built hierarchies: "Bound by" for widgets in a HUD, with a Framework Binder, held by
    /// a parent widget, or bound by nothing; Bind Framework Widgets Below on a level and on a prefab; the HUD's widget
    /// table and Add Widget for; the validation rules, the level problems and every command-line verb, run twice.
    /// </summary>
    public class FrameworkWidgetToolsTests
    {
        private const string Folder = "Assets/__UEATempOvmTools";

        private const string HiddenFolder = "Assets/__UEATempOvmScripts~";

        private Scene _level;

        private Scene _previousActive;

        private string _levelPath;

        private bool _preferTmp;

        [SetUp]
        public void SetUp()
        {
            // Generate UI makes uGUI texts here: TextMeshPro needs Essential Resources the test project does not have.
            _preferTmp = ObserverVMSettings.instance.PreferTmp;

            ObserverVMSettings.instance.PreferTmp = false;

            AssetDatabase.DeleteAsset(Folder);

            Directory.CreateDirectory(Folder);

            AssetDatabase.Refresh();

            FrameworkIndex.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var step in PendingSteps.All().Where(step => step.kind == FrameworkWidgetActions.HudWidgetKind).ToArray())
            {
                PendingSteps.Cancel(step.id);
            }

            // Undo records of the level's objects go before the level does.
            Undo.ClearAll();

            if (_level.IsValid() && _level.isLoaded && SceneManager.sceneCount > 1)
            {
                if (SceneManager.GetActiveScene() == _level && _previousActive.IsValid() && _previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(_previousActive);
                }

                EditorSceneManager.CloseScene(_level, true);
            }

            _level = default;

            if (Directory.Exists(HiddenFolder))
            {
                Directory.Delete(HiddenFolder, true);
            }

            AssetDatabase.DeleteAsset(Folder);

            Undo.ClearAll();

            FrameworkIndex.Invalidate();

            ProblemProviders.Invalidate();

            ObserverVMSettings.instance.PreferTmp = _preferTmp;
        }

        [Test]
        public void BoundBy_OnBuiltHierarchies()
        {
            var level = Level_Internal("Bound");

            var gameModePrefab = Prefab_Internal("BoundMode", typeof(TestGameMode));

            UEArchitectureActions.UseGameModeInLevel(gameModePrefab, level);

            var canvas = Root_Internal(level, "Canvas");

            var loose = Child_Internal<ScoreWidget>(canvas, "Loose Score");

            var bound = Child_Internal<ScoreWidget>(canvas, "Bound Score");

            bound.gameObject.AddComponent<FrameworkBinder>();

            var parent = Child_Internal<HoldingWidget>(canvas, "Parent");

            var held = Child_Internal<ScoreWidget>(parent.gameObject, "Child Score");

            var serialized = new SerializedObject(parent);

            serialized.FindProperty("child").objectReferenceValue = held;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            var hudObject = Root_Internal(level, "Level HUD");

            hudObject.AddComponent<TestHUD>();

            var inHud = Child_Internal<MatchWidget>(hudObject, "Match");

            Refresh_Internal();

            var looseBinding = BindingPreview.Of(loose);

            Assert.AreEqual(BindingSource.Nothing, looseBinding.Source);

            Assert.IsTrue(looseBinding.NeedsBinder);

            Assert.AreEqual(BoundByTone.Error, FrameworkWidgetHooks.BoundBy_Internal(loose).Value.Tone);

            StringAssert.StartsWith("Nothing will bind this widget", looseBinding.Text);

            var boundBinding = BindingPreview.Of(bound);

            Assert.AreEqual(BindingSource.FrameworkBinder, boundBinding.Source);

            Assert.AreEqual(FrameworkSlot.PlayerState, boundBinding.Prediction.Slot);

            StringAssert.Contains(": will probably bind to the Player State (TestPlayerState)", boundBinding.Text);

            Assert.AreEqual(BoundByTone.Bound, FrameworkWidgetHooks.BoundBy_Internal(bound).Value.Tone);

            Assert.IsNull(FrameworkWidgetHooks.BoundBy_Internal(held), "A widget its parent holds gets ObserverVM's own answer.");

            StringAssert.Contains("HoldingWidget.child", WidgetInspectorExtensions.BoundByOf(held).Text);

            // Only a HUD the GameMode spawns for a player starts and binds its widgets: one placed in the level never does.
            var hudBinding = BindingPreview.Of(inHud);

            Assert.AreEqual(BindingSource.HUD, hudBinding.Source);

            Assert.IsTrue(hudBinding.HudNotSpawned);

            Assert.IsFalse(hudBinding.NeedsBinder);

            Assert.AreEqual(BoundByTone.Error, FrameworkWidgetHooks.BoundBy_Internal(inHud).Value.Tone);

            StringAssert.StartsWith("Level HUD is placed in the level, not spawned by a GameMode: nothing binds it", hudBinding.Text);

            Assert.IsTrue(FrameworkWidgetHooks.Rule_Internal(inHud, _levelPath).Any(issue => issue.Problem.Contains("no GameMode spawns it")));

            Assert.IsFalse(FrameworkWidgetActions.BinderCandidates(hudObject).Single().Selected, "Not ticked: the fix is to use the HUD as a prefab.");

            // Asked on every repaint: the same answer until something changes.
            Assert.AreSame(looseBinding, BindingPreview.Of(loose), "Cached.");

            Refresh_Internal();

            Assert.AreNotSame(looseBinding, BindingPreview.Of(loose), "Read again once something changed.");

            var prefabWidget = Prefab_Internal("LooseWidget", typeof(RectTransform), typeof(ScoreWidget)).GetComponent<ScoreWidget>();

            Assert.AreEqual(BindingSource.DependsOnPlacement, BindingPreview.Of(prefabWidget).Source, "A prefab outside a HUD depends on where it is placed.");

            var predicted = FrameworkWidgetActions.PredictBinding(bound);

            Assert.IsTrue(predicted.Ok);

            Assert.AreEqual(boundBinding.Text, predicted.Message);
        }

        [Test]
        public void AHudPrefab_IsCheckedAgainstTheGameModesThatUseIt()
        {
            var hudPrefab = Prefab_Internal("ArenaHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            var pawnPrefab = Prefab_Internal("ArenaPawn", typeof(TestPawn));

            var gameModePrefab = Prefab_Internal("ArenaMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameModePrefab.GetComponent<GameMode>(), "hud", hudPrefab);

            AddChild_Internal(hudPrefab, "Score", typeof(ScoreWidget));

            AddChild_Internal(hudPrefab, "Health", typeof(PawnWidget));

            AddChild_Internal(hudPrefab, "Saves", typeof(FirstSubsystemWidget));

            Refresh_Internal();

            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponent<HUD>();

            CollectionAssert.AreEqual(new[] { gameModePrefab.GetComponent<GameMode>() }, BindingPreview.GameModesUsing(hud));

            var rows = FrameworkWidgetActions.HudWidgets(hud);

            Assert.AreEqual(FrameworkSlot.PlayerState, rows.Single(row => row.Widget.name == "Score").Binding.Prediction.Slot);

            Assert.AreEqual(FrameworkSlot.GameInstanceSubsystem, rows.Single(row => row.Widget.name == "Saves").Binding.Prediction.Slot);

            var health = rows.Single(row => row.Widget.name == "Health");

            Assert.IsFalse(health.Matches, "ArenaMode has no Default Pawn yet.");

            Assert.IsTrue(health.Binding.DependsOnPawn);

            UEArchitectureActions.Assign(gameModePrefab.GetComponent<GameMode>(), "defaultPawn", pawnPrefab);

            Refresh_Internal();

            Assert.AreEqual(FrameworkSlot.Pawn, FrameworkWidgetActions.HudWidgets(hud).Single(row => row.Widget.name == "Health").Binding.Prediction.Slot);

            var described = Twice_Internal("hud-widgets", "target=" + AssetDatabase.GetAssetPath(hudPrefab));

            Assert.IsTrue(described.Notes.Any(note => note.StartsWith("Score: Player State (TestPlayerState)")), string.Join("\n", described.Notes));

            StringAssert.Contains("ArenaMode", described.Message);

            // Only this test's classes: the project may hold samples whose subsystems have Observables.
            CollectionAssert.IsEmpty(FrameworkWidgetActions.WidgetTypesForHud(hud).Where(type => type.Assembly == typeof(TestGameState).Assembly || type.Assembly == GetType().Assembly), "The test classes have no Observables to show.");

            Assert.IsTrue(FrameworkWidgetActions.HasBindableMembers(typeof(BindableState)));

            Assert.IsFalse(FrameworkWidgetActions.HasBindableMembers(typeof(TestGameState)));
        }

        [Test]
        public void AddFrameworkBinders_Twice_OnALevelAndOnAPrefab()
        {
            var level = Level_Internal("Binders");

            var canvas = Root_Internal(level, "Canvas");

            var first = Child_Internal<ScoreWidget>(canvas, "Coins");

            var second = Child_Internal<FirstSubsystemWidget>(canvas, "Best");

            var parent = Child_Internal<HoldingWidget>(canvas, "Parent");

            var held = Child_Internal<ScoreWidget>(parent.gameObject, "Held");

            var serialized = new SerializedObject(parent);

            serialized.FindProperty("child").objectReferenceValue = held;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            Refresh_Internal();

            var candidates = FrameworkWidgetActions.BinderCandidates(canvas);

            Assert.IsFalse(candidates.Single(candidate => candidate.Widget == held).Selected, "A widget its parent holds is unticked.");

            Assert.IsTrue(candidates.Single(candidate => candidate.Widget == first).Selected);

            var dry = FrameworkWidgetActions.AddFrameworkBinders(canvas, true);

            Assert.IsTrue(dry.Ok);

            CollectionAssert.IsEmpty(dry.Changed, "A dry run changes nothing.");

            Assert.IsNull(first.GetComponent<FrameworkBinder>());

            var applied = FrameworkWidgetActions.AddFrameworkBinders(canvas);

            Assert.IsTrue(applied.Ok, string.Join("\n", applied.Problems));

            Assert.IsNotNull(first.GetComponent<FrameworkBinder>());

            Assert.IsNotNull(second.GetComponent<FrameworkBinder>());

            Assert.IsNotNull(parent.GetComponent<FrameworkBinder>(), "The parent shows a GameState: it needs one too.");

            Assert.IsNull(held.GetComponent<FrameworkBinder>(), "Never on a widget its parent binds.");

            Undo.PerformUndo();

            Assert.IsNull(first.GetComponent<FrameworkBinder>(), "One Undo step takes all the binders back.");

            Assert.IsNull(second.GetComponent<FrameworkBinder>());

            Assert.AreEqual(3, FrameworkWidgetActions.AddFrameworkBinders(canvas).Changed.Count);

            var again = FrameworkWidgetActions.AddFrameworkBinders(canvas);

            CollectionAssert.IsEmpty(again.Changed, "The second run changes nothing.");

            var prefab = Prefab_Internal("Overlay", typeof(RectTransform), typeof(Canvas));

            AddChild_Internal(prefab, "Coins", typeof(ScoreWidget));

            var onPrefab = FrameworkWidgetActions.AddFrameworkBinders(prefab);

            Assert.IsTrue(onPrefab.Ok, string.Join("\n", onPrefab.Problems));

            Assert.AreEqual(1, onPrefab.Changed.Count);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)).transform.Find("Coins").GetComponent<FrameworkBinder>(), "Saved in the prefab.");

            CollectionAssert.IsEmpty(FrameworkWidgetActions.AddFrameworkBinders(prefab).Changed);
        }

        [Test]
        public void TheVerbs_Twice_FromTheCommandLine()
        {
            var level = Level_Internal("Cli");

            var canvas = Root_Internal(level, "Canvas");

            Child_Internal<ScoreWidget>(canvas, "Coins");

            Child_Internal<MatchWidget>(canvas, "Round");

            EditorSceneManager.SaveScene(level);

            Refresh_Internal();

            var predicted = UEArchitectureCli.Execute(Parse_Internal("predict-binding", $"target={_levelPath}#Canvas/Coins"));

            Assert.IsTrue(predicted.Ok, string.Join("\n", predicted.Problems));

            StringAssert.StartsWith("Nothing will bind this widget", predicted.Message);

            var dry = UEArchitectureCli.Execute(Parse_Internal("add-framework-binders", $"target={_levelPath}#Canvas", "dry-run=true"));

            Assert.IsTrue(dry.Ok);

            Assert.AreEqual(2, dry.Notes.Count(note => note.StartsWith("Would add")));

            var single = Twice_Internal("add-framework-binder", $"target={_levelPath}#Canvas/Round", "player=2");

            Assert.AreEqual(1, single.Changed.Count);

            StringAssert.Contains("Player 2", single.Changed[0]);

            var all = Twice_Internal("add-framework-binders", $"target={_levelPath}#Canvas");

            Assert.AreEqual(1, all.Changed.Count, "Round had its binder already.");

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("predict-binding")).Ok, "target= is needed.");

            StringAssert.Contains("predict-binding", UEArchitectureCli.HelpText);
        }

        [Test]
        public void AddHudWidget_Twice_WithAnExistingClassAndWithANewOne()
        {
            var hudPrefab = Prefab_Internal("ShopHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            var gameModePrefab = Prefab_Internal("ShopMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameModePrefab.GetComponent<GameMode>(), "hud", hudPrefab);

            Refresh_Internal();

            var hud = hudPrefab.GetComponent<HUD>();

            var first = FrameworkWidgetActions.AddHudWidget(hud, typeof(TestPlayerState));

            Assert.IsTrue(first.Ok, string.Join("\n", first.Problems));

            var added = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponentsInChildren<ScoreWidget>(true);

            Assert.AreEqual(1, added.Length, "ScoreWidget, the one widget class for TestPlayerState, is in the HUD.");

            var second = FrameworkWidgetActions.AddHudWidget(hud, typeof(TestPlayerState));

            Assert.IsTrue(second.Ok, string.Join("\n", second.Problems));

            CollectionAssert.IsEmpty(second.Created, "The second run reuses the widget.");

            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponentsInChildren<ScoreWidget>(true).Length);

            var cli = Twice_Internal("add-hud-widget", "target=" + AssetDatabase.GetAssetPath(hudPrefab), "view-model=" + typeof(TestGameState).FullName, "widget=" + typeof(MatchWidget).FullName);

            Assert.IsTrue(cli.Created.Any(), string.Join(", ", cli.Created));

            // A class with no widget yet: the thin widget is written (in a folder Unity does not compile) and waits.
            var written = FrameworkWidgetActions.AddHudWidget(hud, typeof(PlayerState), null, HiddenFolder);

            Assert.IsTrue(written.Ok, string.Join("\n", written.Problems));

            Assert.IsTrue(written.Pending, "The widget is added after the compile.");

            var script = written.Created.Single(path => path.EndsWith(".cs"));

            StringAssert.Contains("Widget<PlayerState>", File.ReadAllText(script).Replace("ModernWestern.UEArchitecture.", string.Empty));

            var step = PendingSteps.All().Single(item => item.kind == FrameworkWidgetActions.HudWidgetKind && item.scriptPaths.Contains(script));

            var writtenAgain = FrameworkWidgetActions.AddHudWidget(hud, typeof(PlayerState), null, HiddenFolder);

            Assert.IsTrue(writtenAgain.Ok, string.Join("\n", writtenAgain.Problems));

            CollectionAssert.IsEmpty(writtenAgain.Created, "Written once.");

            Assert.AreEqual(1, PendingSteps.All().Count(item => item.kind == FrameworkWidgetActions.HudWidgetKind), "Queued once.");

            // After the compile: ScoreWidget stands in for the new class (no test starts a compile).
            using (var op = Scaffold.Begin("Finish the HUD widget"))
            {
                FrameworkWidgetActions.FinishHudWidget_Internal(new PendingStepContext(step, new[] { typeof(FirstSubsystemWidget) }, op));

                Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));
            }

            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponentsInChildren<FirstSubsystemWidget>(true).Length,
                            "The step adds the widget inside the HUD prefab.");
        }

        [Test]
        public void CreateWidgetFor_ExplainsWhatToAdd_AndPlacesWidgets()
        {
            var nothing = FrameworkWidgetActions.CreateWidgetFor(typeof(TestGameState), WidgetPlacement.PrefabOnly);

            Assert.IsFalse(nothing.Ok);

            StringAssert.Contains("Observable", nothing.Problems[0], "Says what to add.");

            var cli = UEArchitectureCli.Execute(Parse_Internal("create-widget-for", "class=" + typeof(TestGameState).FullName, "placement=prefab"));

            Assert.IsFalse(cli.Ok);

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("create-widget-for", "class=" + typeof(BindableState).FullName, "placement=somewhere")).Ok, "Unknown placement.");

            // UI in the test runner's own scene, removed afterwards (closing a level with a Canvas crashes batch runs).
            var scene = SceneManager.GetActiveScene();

            var before = new System.Collections.Generic.HashSet<GameObject>(scene.GetRootGameObjects());

            var canvas = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));

            try
            {
                var placed = FrameworkWidgetActions.CreateWidgetFor(typeof(BindableState), WidgetPlacement.Canvas, canvas);

                Assert.IsTrue(placed.Ok, string.Join("\n", placed.Problems));

                var widget = canvas.GetComponentInChildren<BindableStateWidget>(true);

                Assert.IsNotNull(widget, "BindableStateWidget, the one widget class of BindableState, is under the Canvas.");

                Assert.IsNotNull(widget.GetComponent<FrameworkBinder>(), "Outside a HUD it gets a Framework Binder.");

                var again = FrameworkWidgetActions.CreateWidgetFor(typeof(BindableState), WidgetPlacement.Canvas, canvas);

                Assert.IsTrue(again.Ok, string.Join("\n", again.Problems));

                CollectionAssert.IsEmpty(again.Created, "The second run reuses it.");

                Assert.AreEqual(1, canvas.GetComponentsInChildren<BindableStateWidget>(true).Length);
            }
            finally
            {
                Undo.ClearAll();

                foreach (var root in scene.GetRootGameObjects().Where(root => !before.Contains(root)))
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void CreateWidgetFor_InsideAHud_Twice_FromTheCommandLine()
        {
            var hudPrefab = Prefab_Internal("RoundHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            var first = Twice_Internal("create-widget-for", "class=" + typeof(BindableState).FullName, "placement=hud", "target=" + AssetDatabase.GetAssetPath(hudPrefab));

            Assert.IsTrue(first.Created.Any(), string.Join(", ", first.Created));

            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponentsInChildren<BindableStateWidget>(true).Length);

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponentInChildren<FrameworkBinder>(true), "The HUD binds it: no binder.");
        }

        [Test]
        public void ValidationRulesAndLevelProblems_ListUnboundWidgetsWithAFix()
        {
            var level = Level_Internal("Checked");

            var canvas = Root_Internal(level, "Canvas");

            var loose = Child_Internal<ScoreWidget>(canvas, "Carry");

            var other = Child_Internal<MatchWidget>(canvas, "Round");

            Refresh_Internal();

            var issues = FrameworkWidgetHooks.Rule_Internal(loose, _levelPath).ToList();

            Assert.AreEqual(1, issues.Count);

            Assert.AreEqual(BindWidgetIssueSeverity.Warning, issues[0].Severity);

            StringAssert.Contains("not in a HUD and has no Framework Binder", issues[0].Problem);

            Assert.AreEqual("Add Framework Binder", issues[0].Fix.Label);

            var report = ProblemProviders.ForLevel(level);

            var problem = report.Problems.Single(item => item.FixId == "add-framework-binders");

            Assert.AreEqual("Bind 2 widgets", problem.FixLabel);

            Assert.IsTrue(report.Problems.Any(item => item.Message.Contains("has framework widgets but no GameMode")), "No GameMode in this level.");

            Assert.IsTrue(issues[0].Fix.Apply().Ok, "The fix adds the binder.");

            Assert.IsNotNull(loose.GetComponent<FrameworkBinder>());

            Refresh_Internal();

            var withBinder = FrameworkWidgetHooks.Rule_Internal(loose, _levelPath).ToList();

            Assert.AreEqual(1, withBinder.Count, "Bound now, but the level has no GameMode.");

            StringAssert.Contains("has framework widgets but no GameMode", withBinder[0].Problem);

            Assert.IsTrue(problem.Fix().Ok);

            Refresh_Internal();

            Assert.IsNotNull(other.GetComponent<FrameworkBinder>());

            Assert.IsFalse(ProblemProviders.ForLevel(level).Problems.Any(item => item.FixId == "add-framework-binders" || item.FixId == "add-framework-binder"), "Nothing left to bind.");

            var validated = UEArchitectureActions.Validate("open", true);

            Assert.IsFalse(validated.Problems.Any(line => line.Contains("Framework Binder")), string.Join("\n", validated.Problems));
        }

        [Test]
        public void APrefabOutsideAHud_DependsOnWhereItIsPlaced()
        {
            var prefab = Prefab_Internal("LooseScore", typeof(RectTransform), typeof(ScoreWidget));

            var path = AssetDatabase.GetAssetPath(prefab);

            var widget = prefab.GetComponent<ScoreWidget>();

            var hudPrefab = Prefab_Internal("NoteHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            AddChild_Internal(hudPrefab, "Score", typeof(ScoreWidget));

            var inHud = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).transform.Find("Score").GetComponent<ScoreWidget>();

            var level = Level_Internal("Placed");

            var loose = Child_Internal<ScoreWidget>(Root_Internal(level, "Canvas"), "Loose");

            Refresh_Internal();

            Assert.AreEqual(BindingSource.DependsOnPlacement, BindingPreview.Of(widget).Source);

            var issues = FrameworkWidgetHooks.Rule_Internal(widget, path).ToList();

            Assert.AreEqual(BindingSource.HUD, BindingPreview.Of(inHud).Source);

            Assert.IsFalse(FrameworkWidgetHooks.Rule_Internal(inHud, AssetDatabase.GetAssetPath(hudPrefab)).Any(issue => issue.Problem.Contains("depends on where you place it")), "Inside a HUD the HUD binds it: no note.");

            Assert.AreEqual(BindWidgetIssueSeverity.Warning, FrameworkWidgetHooks.Rule_Internal(loose, _levelPath).Single().Severity, "A widget of a level that nothing binds stays a warning.");

#if UEA_OBSERVERVM_INFO
            Assert.AreEqual(1, issues.Count);

            Assert.AreEqual(BindWidgetIssueSeverity.Info, issues[0].Severity);

            Assert.AreEqual("LooseScore in LooseScore.prefab: what binds it depends on where you place it (inside a HUD, or with a Framework Binder).", issues[0].Problem);

            Assert.IsNull(issues[0].Fix, "Nothing to fix: where it is placed decides.");

            // ObserverVM lists it as a note: never an error or a warning, never a problem of "-ovm validate".
            var errorsOnly = new List<BindWidgetIssue>();

            BindWidgetValidator.ValidateHierarchy(prefab, path, errorsOnly);

            Assert.IsEmpty(errorsOnly);

            var all = new List<BindWidgetIssue>();

            BindWidgetValidator.ValidateHierarchy(prefab, path, all, true);

            Assert.AreEqual(1, BindWidgetValidator.Count(all, BindWidgetIssueSeverity.Info));

            Assert.AreEqual(0, BindWidgetValidator.CountErrors(all) + BindWidgetValidator.Count(all, BindWidgetIssueSeverity.Warning));

            var check = ObserverVMCli.Execute(new[] { "-ovm", "validate", $"target={path}" }, out var code);

            Assert.AreEqual(ObserverVMCli.ExitOk, code, check.ToJson());

            Assert.IsTrue(check.Notes.Any(note => note.StartsWith("Note: ", StringComparison.Ordinal) && note.Contains("depends on where you place it")), check.ToJson());

            // The Level overlay in Prefab Mode: an info note, never a problem; widgets of a level get none.
            var problems = ProblemProviders.For(widget);

            Assert.AreEqual(1, problems.Count);

            Assert.AreEqual(ProblemSeverity.Info, problems[0].Severity);

            Assert.AreEqual(issues[0].Problem, problems[0].Message);

            CollectionAssert.IsEmpty(ProblemProviders.For(inHud));

            CollectionAssert.IsEmpty(ProblemProviders.For(loose));

            Assert.IsFalse(UEArchitectureActions.Validate("open", true).Problems.Any(line => line.Contains("depends on where you place it")));
#else
            Assert.IsEmpty(issues, "Before ObserverVM 2.4 (no Info level) a prefab outside a HUD is not reported.");

            CollectionAssert.IsEmpty(ProblemProviders.For(widget));
#endif
        }

        [Test]
        public void WidgetsMadeForAFrameworkObject_GetABinderOutsideAHud()
        {
            var previous = UEArchitectureEditorSettings.instance.AutoAddFrameworkBinder;

            try
            {
                var level = Level_Internal("Created");

                var canvas = Root_Internal(level, "Canvas");

                var widget = Child_Internal<ScoreWidget>(canvas, "Coins");

                var hudObject = Root_Internal(level, "HUD");

                hudObject.AddComponent<TestHUD>();

                var inHud = Child_Internal<ScoreWidget>(hudObject, "Coins");

                UEArchitectureEditorSettings.instance.AutoAddFrameworkBinder = false;

                WidgetCreationHooks.Raise(widget, WidgetCreationKind.ForViewModel);

                Assert.IsNull(widget.GetComponent<FrameworkBinder>(), "Off in the settings.");

                UEArchitectureEditorSettings.instance.AutoAddFrameworkBinder = true;

                Refresh_Internal();

                WidgetCreationHooks.Raise(widget, WidgetCreationKind.Prefab);

                Assert.IsNull(widget.GetComponent<FrameworkBinder>(), "Only widgets added for a ViewModel.");

                WidgetCreationHooks.Raise(widget, WidgetCreationKind.ForViewModel);

                Assert.IsNotNull(widget.GetComponent<FrameworkBinder>());

                WidgetCreationHooks.Raise(inHud, WidgetCreationKind.ForViewModel);

                Assert.IsNull(inHud.GetComponent<FrameworkBinder>(), "The HUD binds its widgets.");
            }
            finally
            {
                UEArchitectureEditorSettings.instance.AutoAddFrameworkBinder = previous;
            }
        }

        [Test]
        public void TheKitPreset_AddsWidgetsForTheStates()
        {
            var preset = HudPresets.Find(FrameworkWidgetHooks.KitPresetId);

            Assert.IsNotNull(preset, "Registered by the integration.");

            Assert.IsFalse(preset.Available(typeof(TestGameState), typeof(TestPlayerState)), "Nothing to show.");

            Assert.IsTrue(preset.Available(typeof(BindableState), typeof(TestPlayerState)));

            var hudPrefab = Prefab_Internal("KitHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            using (var op = Scaffold.Begin("Kit preset"))
            {
                preset.Apply(op, hudPrefab, typeof(BindableState), typeof(TestPlayerState));

                Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));
            }

            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab)).GetComponentsInChildren<BindableStateWidget>(true).Length);

            var emptyHud = Prefab_Internal("EmptyKitHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            using (var op = Scaffold.Begin("Kit preset, nothing to show"))
            {
                preset.Apply(op, emptyHud, typeof(TestGameState), typeof(TestPlayerState));

                Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));

                Assert.IsTrue(op.Result.Notes.Any(note => note.Contains("nothing to show yet") && note.Contains("added no widgets")), "Says why the HUD got nothing: " + string.Join("\n", op.Result.Notes));
            }

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(emptyHud)).GetComponentInChildren<Widget>(true));
        }

        [Test]
        public void ABinderInsideAHud_IsAWarning_ItBindsItsPlayerBeforeTheHudCan()
        {
            var hudPrefab = Prefab_Internal("BinderHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            var gameModePrefab = Prefab_Internal("BinderMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameModePrefab.GetComponent<GameMode>(), "hud", hudPrefab);

            AddChild_Internal(hudPrefab, "Score", typeof(ScoreWidget));

            using (var scope = new PrefabUtility.EditPrefabContentsScope(AssetDatabase.GetAssetPath(hudPrefab)))
            {
                scope.prefabContentsRoot.transform.Find("Score").gameObject.AddComponent<FrameworkBinder>();
            }

            Refresh_Internal();

            var hudAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(hudPrefab));

            var widget = hudAsset.transform.Find("Score").GetComponent<ScoreWidget>();

            var binding = BindingPreview.Of(widget);

            Assert.AreEqual(BindingSource.FrameworkBinder, binding.Source, "GameMode.AddPlayer spawns the HUD after the level began: the binder binds first.");

            Assert.IsTrue(binding.BinderInsideHud);

            StringAssert.Contains("it binds Player 1 before the HUD can; remove it", binding.Text);

            Assert.AreEqual(BoundByTone.Warning, FrameworkWidgetHooks.BoundBy_Internal(widget).Value.Tone);

            Assert.IsTrue(FrameworkWidgetHooks.Rule_Internal(widget, "test").Any(issue => issue.Problem.Contains("remove it")));

            Assert.IsFalse(FrameworkWidgetActions.DescribeHudWidgets(hudAsset.GetComponent<HUD>(), null, true).Ok, "The HUD's table flags it.");
        }

        [Test]
        public void ABinderForAPlayerTheGameModeDoesNotStart_GetsNoPlayerObjects()
        {
            var level = Level_Internal("NoPlayers");

            var gameModePrefab = Prefab_Internal("NoPlayersMode", typeof(TestGameMode));

            var serialized = new SerializedObject(gameModePrefab.GetComponent<GameMode>());

            serialized.FindProperty("playersAtStart").intValue = 0;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();

            UEArchitectureActions.UseGameModeInLevel(gameModePrefab, level);

            var canvas = Root_Internal(level, "Canvas");

            var score = Child_Internal<ScoreWidget>(canvas, "Score");

            score.gameObject.AddComponent<FrameworkBinder>();

            Refresh_Internal();

            var binding = BindingPreview.Of(score);

            Assert.AreEqual(BindingSource.FrameworkBinder, binding.Source);

            Assert.IsFalse(binding.Set.HasPlayer, "A GameMode with no players at start has no Player 1.");

            Assert.IsFalse(binding.Prediction.Matches);

            StringAssert.Contains("only sees players that exist when the level begins", binding.Tooltip);
        }

        [Test]
        public void APrefabPlacedInsideAHudPrefab_IsNotOfferedABinder()
        {
            var hudPrefab = Prefab_Internal("NestHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            var coins = Prefab_Internal("NestCoins", typeof(RectTransform), typeof(ScoreWidget));

            using (var scope = new PrefabUtility.EditPrefabContentsScope(AssetDatabase.GetAssetPath(hudPrefab)))
            {
                var nested = (GameObject)PrefabUtility.InstantiatePrefab(coins, scope.prefabContentsRoot.scene);

                nested.transform.SetParent(scope.prefabContentsRoot.transform, false);
            }

            var loose = Prefab_Internal("LooseCoins", typeof(RectTransform), typeof(ScoreWidget));

            Refresh_Internal();

            var candidate = FrameworkWidgetActions.BinderCandidates(coins).Single();

            Assert.IsFalse(candidate.Selected, "Inside NestHUD the HUD binds it; a binder would bind Player 1 in every player's HUD.");

            StringAssert.Contains("NestHUD", candidate.Reason);

            CollectionAssert.IsEmpty(FrameworkWidgetActions.AddFrameworkBinders(coins).Changed);

            Assert.IsFalse(FrameworkWidgetActions.AddFrameworkBinder(coins.GetComponent<ScoreWidget>()).Ok);

            Assert.IsTrue(FrameworkWidgetActions.BinderCandidates(loose).Single().Selected, "A prefab no HUD has stays ticked.");
        }

        [Test]
        public void AHudVariant_IsCheckedAgainstTheGameModesThatUseTheVariant()
        {
            var basePrefab = Prefab_Internal("BaseHUD", typeof(RectTransform), typeof(TestHUD));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

            var variant = PrefabUtility.SaveAsPrefabAsset(instance, $"{Folder}/VariantHUD.prefab");

            Object.DestroyImmediate(instance);

            var gameModePrefab = Prefab_Internal("VariantMode", typeof(TestGameMode));

            UEArchitectureActions.Assign(gameModePrefab.GetComponent<GameMode>(), "hud", variant);

            var level = Level_Internal("VariantLevel");

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(variant, level);

            Refresh_Internal();

            CollectionAssert.AreEqual(new[] { gameModePrefab.GetComponent<GameMode>() }, BindingPreview.GameModesUsing(placed.GetComponent<HUD>()), "The variant it comes from, not the variant's base.");
        }

        [Test]
        public void AddFrameworkBinders_OnAPrefab_TellsSameNamedSiblingsApart()
        {
            var prefab = Prefab_Internal("Twins", typeof(RectTransform));

            AddChild_Internal(prefab, "Coins", typeof(ScoreWidget));

            AddChild_Internal(prefab, "Coins", typeof(ScoreWidget));

            var result = FrameworkWidgetActions.AddFrameworkBinders(prefab);

            Assert.IsTrue(result.Ok, string.Join("\n", result.Problems));

            Assert.AreEqual(2, result.Changed.Count, string.Join("\n", result.Changed));

            Assert.AreEqual(2, AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)).GetComponentsInChildren<FrameworkBinder>(true).Length, "Each Coins got its own binder.");
        }

        [Test]
        public void AddFrameworkBinders_OnAPlacedPrefab_IsAnOverride_OrGoesInThePrefab()
        {
            var overlay = Prefab_Internal("PlacedOverlay", typeof(RectTransform));

            AddChild_Internal(overlay, "Coins", typeof(ScoreWidget));

            var shared = Prefab_Internal("SharedOverlay", typeof(RectTransform));

            AddChild_Internal(shared, "Coins", typeof(ScoreWidget));

            var level = Level_Internal("Instances");

            var copy = (GameObject)PrefabUtility.InstantiatePrefab(overlay, level);

            var sharedCopy = (GameObject)PrefabUtility.InstantiatePrefab(shared, level);

            EditorSceneManager.SaveScene(level);

            Refresh_Internal();

            var asOverride = FrameworkWidgetActions.AddFrameworkBinders(copy);

            Assert.IsTrue(asOverride.Ok, string.Join("\n", asOverride.Problems));

            StringAssert.Contains("override on this copy of the prefab", asOverride.Changed.Single());

            Assert.IsNotNull(copy.transform.Find("Coins").GetComponent<FrameworkBinder>());

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(overlay)).transform.Find("Coins").GetComponent<FrameworkBinder>(), "Only this copy.");

            Assert.IsFalse(UEArchitectureCli.Execute(Parse_Internal("add-framework-binders", $"target={_levelPath}#{sharedCopy.name}", "apply-to=somewhere")).Ok, "apply-to is instance or prefab.");

            var inPrefab = Twice_Internal("add-framework-binders", $"target={_levelPath}#{sharedCopy.name}", "apply-to=prefab");

            StringAssert.Contains("every copy", inPrefab.Changed.Single());

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(shared)).transform.Find("Coins").GetComponent<FrameworkBinder>(), "In the prefab.");
        }

        [Test]
        public void TheKitPresetAndTheHudWidgetStep_SaveTheHudFile_InsteadOfOpeningPrefabMode()
        {
            // Batch mode never opens Prefab Mode: these go through it like the Editor does.
            var force = typeof(ObserverVMActions).Assembly.GetType("ModernWestern.UI.ObserverVM.EditorTools.EditContext")
                                                 .GetProperty("ForcePrefabMode_Internal", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(force);

            var openPrefabMode = ObserverVMActions.OpenPrefabMode;

            try
            {
                ObserverVMActions.OpenPrefabMode = true;

                force.SetValue(null, true);

                var hudPrefab = Prefab_Internal("SavedHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

                var path = AssetDatabase.GetAssetPath(hudPrefab);

                using (var op = Scaffold.Begin("Kit preset"))
                {
                    HudPresets.Find(FrameworkWidgetHooks.KitPresetId).Apply(op, hudPrefab, typeof(BindableState), typeof(TestPlayerState));

                    Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));
                }

                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(), "The kit's HUD does not open in Prefab Mode.");

                Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<BindableStateWidget>(true).Length, "Saved in the file.");

                Assert.IsTrue(ObserverVMActions.OpenPrefabMode, "Put back.");

                var result = new ActionResult("test");

                Assert.IsTrue(PendingSteps.TryTarget(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<HUD>(), string.Empty, result, out var target), string.Join("\n", result.Problems));

                var step = PendingStep.Create(FrameworkWidgetActions.HudWidgetKind).WithScript($"{Folder}/MatchWidget.cs", typeof(MatchWidget).FullName);

                step.targets.Add(target);

                using (var op = Scaffold.Begin("Finish the HUD widget"))
                {
                    FrameworkWidgetActions.FinishHudWidget_Internal(new PendingStepContext(step, new[] { typeof(MatchWidget) }, op));

                    Assert.IsTrue(op.Result.Ok, string.Join("\n", op.Result.Problems));
                }

                Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(), "The step does not open Prefab Mode after the compile.");

                Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MatchWidget>(true).Length, "Saved in the file when the step is done.");
            }
            finally
            {
                force.SetValue(null, null);

                ObserverVMActions.OpenPrefabMode = openPrefabMode;

                var stage = PrefabStageUtility.GetCurrentPrefabStage();

                if (stage != null)
                {
                    stage.ClearDirtiness();

                    StageUtility.GoToMainStage();
                }
            }
        }

        [Test]
        public void AddHudWidget_WhenTheFileDeclaresAnotherClass_SaysSoInsteadOfWaiting()
        {
            var hudPrefab = Prefab_Internal("ClashHUD", typeof(RectTransform), typeof(Canvas), typeof(TestHUD));

            var hud = hudPrefab.GetComponent<HUD>();

            var written = FrameworkWidgetActions.AddHudWidget(hud, typeof(PlayerState), null, HiddenFolder);

            Assert.IsTrue(written.Ok, string.Join("\n", written.Problems));

            foreach (var step in PendingSteps.All().Where(step => step.kind == FrameworkWidgetActions.HudWidgetKind).ToArray())
            {
                PendingSteps.Cancel(step.id);
            }

            var previous = FrameworkWidgetActions.ClassOfScript_Internal;

            try
            {
                // The file is there but compiles to another class: no compile will ever bring the widget class.
                FrameworkWidgetActions.ClassOfScript_Internal = _ => typeof(ScoreWidget);

                var clash = FrameworkWidgetActions.AddHudWidget(hud, typeof(PlayerState), null, HiddenFolder);

                Assert.IsFalse(clash.Ok);

                Assert.IsFalse(clash.Pending);

                StringAssert.Contains("declares " + typeof(ScoreWidget).FullName, clash.Problems[0]);

                Assert.IsFalse(PendingSteps.All().Any(step => step.kind == FrameworkWidgetActions.HudWidgetKind), "Nothing queued to wait for it.");
            }
            finally
            {
                FrameworkWidgetActions.ClassOfScript_Internal = previous;
            }
        }

        private Scene Level_Internal(string name)
        {
            _levelPath = $"{Folder}/{name}.unity";

            _previousActive = SceneManager.GetActiveScene();

            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), _levelPath, true));

            _level = EditorSceneManager.OpenScene(_levelPath, OpenSceneMode.Additive);

            foreach (var root in _level.GetRootGameObjects())
            {
                Object.DestroyImmediate(root);
            }

            EditorSceneManager.SaveScene(_level);

            return _level;
        }

        private static GameObject Root_Internal(Scene scene, string name)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));

            SceneManager.MoveGameObjectToScene(gameObject, scene);

            return gameObject;
        }

        private static T Child_Internal<T>(GameObject parent, string name) where T : Component
        {
            var child = new GameObject(name, typeof(RectTransform));

            child.transform.SetParent(parent.transform, false);

            return child.AddComponent<T>();
        }

        private static GameObject Prefab_Internal(string name, params Type[] types)
        {
            using (var op = Scaffold.Begin("Test prefab " + name))
            {
                var prefab = op.FindOrCreatePrefab($"{Folder}/{name}.prefab", name, types);

                Assert.IsNotNull(prefab, string.Join("\n", op.Result.Problems));

                return prefab;
            }
        }

        private static void AddChild_Internal(GameObject prefab, string name, Type type)
        {
            var path = AssetDatabase.GetAssetPath(prefab);

            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                var child = new GameObject(name, typeof(RectTransform));

                child.transform.SetParent(scope.prefabContentsRoot.transform, false);

                child.AddComponent(type);
            }
        }

        private static void Refresh_Internal()
        {
            FrameworkIndex.Invalidate();

            ProblemProviders.Invalidate();

            WidgetInspectorExtensions.InvalidateCache();
        }

        private static ActionResult Twice_Internal(string verb, params string[] pairs)
        {
            var first = UEArchitectureCli.Execute(Parse_Internal(verb, pairs));

            Assert.IsTrue(first.Ok, $"{verb}: {string.Join("\n", first.Problems)}");

            Assert.AreEqual(verb, first.Verb);

            Refresh_Internal();

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
