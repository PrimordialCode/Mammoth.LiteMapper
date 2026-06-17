using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Runtime.Tests
{
    [TestClass]
    public sealed class Milestone1RuntimeSurfaceTests
    {
        [TestMethod]
        public void ShippingAssembliesExposeNoMappingApiBeforeMilestone2()
        {
            var runtimeTypes = Assembly.Load("Mammoth.LiteMapper").GetExportedTypes();
            var abstractionsTypes = Assembly.Load("Mammoth.LiteMapper.Abstractions").GetExportedTypes();

            Assert.AreEqual(0, runtimeTypes.Length);
            Assert.AreEqual(0, abstractionsTypes.Length);
        }
    }
}
