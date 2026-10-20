using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using System.Text.RegularExpressions;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace ModernWestern.UEArchitecture.Tests
{
    public class GameplayTagTests
    {
        private static readonly GameplayTag Status = GameplayTag.Request("Test.Status");

        private static readonly GameplayTag Burning = GameplayTag.Request("Test.Status.Burning");

        private static readonly GameplayTag Frozen = GameplayTag.Request("Test.Status.Frozen");

        private static readonly GameplayTag Team = GameplayTag.Request("Test.Team.Red");

        [System.Serializable]
        private sealed class Holder
        {
            public GameplayTag tag;

            public GameplayTagContainer tags = new();
        }

        [Test]
        public void ATag_MatchesItselfAndItsParents_NotTheOtherWayAround()
        {
            Assert.IsTrue(Burning.MatchesTag(Status));

            Assert.IsTrue(Burning.MatchesTag(GameplayTag.Request("Test")));

            Assert.IsTrue(Burning.MatchesTag(Burning));

            Assert.IsFalse(Status.MatchesTag(Burning));

            Assert.IsFalse(Burning.MatchesTag(Frozen));

            Assert.IsFalse(Burning.MatchesTagExact(Status));

            Assert.IsFalse(Burning.MatchesTag(GameplayTag.None));
        }

        [Test]
        public void Requesting_RegistersParents_AndReturnsTheSameTag()
        {
            Assert.AreEqual(Status, Burning.Parent);

            Assert.AreEqual("Test.Status.Burning", Burning.Name);

            Assert.AreEqual(Burning, GameplayTag.Request("Test.Status.Burning"));

            Assert.IsFalse(GameplayTag.None.IsValid);

            Assert.AreEqual("None", GameplayTag.None.ToString());
        }

        [Test]
        public void AnInvalidName_GivesNone_WithAWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("not a valid gameplay tag"));

            Assert.IsFalse(GameplayTag.Request("Bad name.").IsValid);
        }

        [Test]
        public void AContainer_AnswersWithParents()
        {
            var tags = new GameplayTagContainer(Burning, Team);

            Assert.IsTrue(tags.HasTag(Status));

            Assert.IsFalse(tags.HasTagExact(Status));

            Assert.IsTrue(tags.HasTagExact(Burning));

            Assert.IsFalse(tags.HasTag(Frozen));

            Assert.IsTrue(tags.HasAny(new GameplayTagContainer(Frozen, Status)));

            Assert.IsTrue(tags.HasAll(new GameplayTagContainer(Status, GameplayTag.Request("Test.Team"))));

            Assert.IsFalse(tags.HasAll(new GameplayTagContainer(Status, Frozen)));

            Assert.IsTrue(tags.HasAll(new GameplayTagContainer()), "Nothing required: true.");

            Assert.IsFalse(tags.HasAny(new GameplayTagContainer()), "Nothing to match: false.");

            tags.AddTag(Burning);

            Assert.AreEqual(2, tags.Count, "No duplicates.");

            Assert.IsTrue(tags.RemoveTag(Burning));

            Assert.IsFalse(tags.HasTag(Status));
        }

        [Test]
        public void TagsSurviveSerialization_AndBadEntriesAreDropped()
        {
            var holder = new Holder { tag = Frozen };

            holder.tags.AddTag(Burning);

            holder.tags.AddTag(Team);

            var json = JsonUtility.ToJson(holder);

            var copy = JsonUtility.FromJson<Holder>(json);

            Assert.AreEqual(Frozen, copy.tag);

            Assert.IsTrue(copy.tag.MatchesTag(Status));

            Assert.IsTrue(copy.tags.HasTag(Status));

            Assert.IsTrue(copy.tags.HasTagExact(Team));

            var messy = JsonUtility.FromJson<Holder>("{\"tags\":{\"tags\":[\"Test.Status.Burning\",\"\",\"Test.Status.Burning\"]}}");

            Assert.AreEqual(1, messy.tags.Count, "Empty and repeated names are dropped.");
        }

        [Test]
        public void Queries_DoNotAllocate()
        {
            var tags = new GameplayTagContainer(Burning, Team);

            var required = new GameplayTagContainer(Status);

            Query_Internal(tags, required);

            Assert.That(() => Query_Internal(tags, required), Is.Not.AllocatingGCMemory());
        }

        private static void Query_Internal(GameplayTagContainer tags, GameplayTagContainer required)
        {
            _ = tags.HasTag(Status);

            _ = tags.HasAll(required);

            _ = tags.HasAny(required);

            _ = Burning.MatchesTag(Status);

            tags.AddTag(Frozen);

            tags.RemoveTag(Frozen);
        }
    }
}
