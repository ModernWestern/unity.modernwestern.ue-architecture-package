using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Scripting;

namespace ModernWestern.UEArchitecture.EditorTools.Tests
{
    public class LinkerTests
    {
        // A game never references the integration's types, so without this the IL2CPP linker removes the assembly and no
        // HUD widget gets its ViewModel.
        [Test]
        public void TheObserverVMIntegration_IsAlwaysLinked()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(each => each.GetName().Name == "UEArchitecture.ObserverVM");

            Assert.IsTrue(assembly.IsDefined(typeof(AlwaysLinkAssemblyAttribute), false));
        }
    }
}
