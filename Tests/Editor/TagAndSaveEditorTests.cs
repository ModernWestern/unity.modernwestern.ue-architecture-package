using NUnit.Framework;
using ModernWestern.UEArchitecture.Tests;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class TagAndSaveEditorTests
    {
        [Test]
        public void TheLinkXml_KeepsSaveGameClasses()
        {
            StringAssert.Contains(typeof(TestProgress).FullName, FrameworkLinkXml.Build());
        }

        [TestCase("Status.Burning", true)]
        [TestCase("Status", true)]
        [TestCase("Ability_1.Cooldown", true)]
        [TestCase("Status..Burning", false)]
        [TestCase(".Status", false)]
        [TestCase("Status.", false)]
        [TestCase("Status Burning", false)]
        [TestCase("", false)]
        public void TagNames_AreValidated(string name, bool valid)
        {
            Assert.AreEqual(valid, GameplayTagRegistry.IsValidName(name));
        }
    }
}
