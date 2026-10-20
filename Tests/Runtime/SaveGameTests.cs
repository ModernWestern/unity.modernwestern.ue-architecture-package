using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using System.Collections;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace ModernWestern.UEArchitecture.Tests
{
    [Serializable]
    public sealed class TestProgress : SaveGame
    {
        public int level;

        public string playerName;

        public GameplayTagContainer unlocked = new();

        [NonSerialized] public int LoadedVersion;

        [NonSerialized] public bool Prepared;

        public override int Version => 3;

        protected override void OnBeforeSave() => Prepared = true;

        protected override void OnAfterLoad(int savedVersion) => LoadedVersion = savedVersion;
    }

    [Serializable]
    public sealed class OtherSave : SaveGame
    {
        public float volume;
    }

    public class SaveGameTests
    {
        private string _folder;

        [SetUp]
        public void UseATemporaryFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "UEArchitectureSaveTests_" + Guid.NewGuid().ToString("N"));

            SaveGames.UseFolder_Internal(_folder);
        }

        [TearDown]
        public void RemoveTheFolder()
        {
            SaveGames.UseFolder_Internal(null);

            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void ASaveGame_ComesBackAsItWent()
        {
            var progress = new TestProgress { level = 7, playerName = "Lobsang" };

            progress.unlocked.AddTag(GameplayTag.Request("Test.Unlock.DoubleJump"));

            Assert.IsTrue(SaveGames.Save(progress, "Slot 1"));

            Assert.IsTrue(progress.Prepared, "OnBeforeSave runs before writing.");

            Assert.IsTrue(SaveGames.Exists("Slot 1"));

            var loaded = SaveGames.Load<TestProgress>("Slot 1");

            Assert.AreEqual(7, loaded.level);

            Assert.AreEqual("Lobsang", loaded.playerName);

            Assert.IsTrue(loaded.unlocked.HasTag(GameplayTag.Request("Test.Unlock")));

            Assert.AreEqual(3, loaded.LoadedVersion, "OnAfterLoad gets the version of the file.");

            CollectionAssert.AreEqual(new[] { "Slot 1" }, SaveGames.Slots());

            Assert.IsEmpty(Directory.GetFiles(_folder, "*.tmp"), "No temporary file left behind.");
        }

        [Test]
        public void SavingAgain_ReplacesTheSlot()
        {
            SaveGames.Save(new TestProgress { level = 1 }, "Slot");

            SaveGames.Save(new TestProgress { level = 2 }, "Slot");

            Assert.AreEqual(2, SaveGames.Load<TestProgress>("Slot").level);

            Assert.AreEqual(1, SaveGames.Slots().Length);
        }

        [Test]
        public void AnEmptySlot_LoadsAsNull()
        {
            Assert.IsNull(SaveGames.Load<TestProgress>("Nothing here"));

            Assert.IsFalse(SaveGames.Exists("Nothing here"));

            Assert.IsFalse(SaveGames.Delete("Nothing here"));
        }

        [Test]
        public void Delete_RemovesTheSlot()
        {
            SaveGames.Save(new TestProgress(), "Slot");

            Assert.IsTrue(SaveGames.Delete("Slot"));

            Assert.IsFalse(SaveGames.Exists("Slot"));
        }

        [Test]
        public void AnotherType_IsNotLoadedAsThisOne()
        {
            SaveGames.Save(new OtherSave { volume = 0.5f }, "Settings");

            LogAssert.Expect(LogType.Warning, new Regex("holds a .*OtherSave"));

            Assert.IsNull(SaveGames.Load<TestProgress>("Settings"));
        }

        [Test]
        public void ADamagedFile_IsReported_NotThrown()
        {
            Directory.CreateDirectory(_folder);

            File.WriteAllText(Path.Combine(_folder, "Broken.sav"), "{ this is not json");

            LogAssert.Expect(LogType.Warning, new Regex("damaged"));

            Assert.IsNull(SaveGames.Load<TestProgress>("Broken"));
        }

        [Test]
        public void ABadSlotName_IsRejected()
        {
            LogAssert.Expect(LogType.Error, new Regex("not a valid save slot name"));

            Assert.IsFalse(SaveGames.Save(new TestProgress(), "a/b"));
        }

        [UnityTest]
        public IEnumerator AsyncSaveAndLoad_WorkToo()
        {
            var saving = SaveGames.SaveAsync(new TestProgress { level = 9 }, "Async");

            while (!saving.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(saving.Result);

            var loading = SaveGames.LoadAsync<TestProgress>("Async");

            while (!loading.IsCompleted)
            {
                yield return null;
            }

            Assert.AreEqual(9, loading.Result.level);
        }
    }
}
