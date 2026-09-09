using System;
using System.Linq;
using System.Reflection;
using Mammoth.LiteMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class RoslynHostMatrixTests
    {
        [TestMethod]
        public void RequestedCompilerHostIsLoadedWhileShippingBaselineRemainsUnchanged()
        {
            var configured = typeof(RoslynHostMatrixTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute => attribute.Key == "RoslynTestVersion")?.Value;
            Assert.IsNotNull(configured, "The test build must record the requested Roslyn host so an ignored matrix override cannot pass silently.");
            var requested = Version.Parse(configured);
            foreach (var assembly in new[] { typeof(Compilation).Assembly, typeof(CSharpCompilation).Assembly, typeof(Workspace).Assembly })
            {
                var actual = assembly.GetName().Version!;
                Assert.AreEqual(requested.Major, actual.Major, assembly.FullName);
                Assert.AreEqual(requested.Minor, actual.Minor, assembly.FullName);
            }

            var generatorReferences = typeof(LiteMapperGenerator).Assembly.GetReferencedAssemblies()
                .Where(reference => reference.Name == "Microsoft.CodeAnalysis" || reference.Name == "Microsoft.CodeAnalysis.CSharp").ToArray();
            Assert.AreEqual(2, generatorReferences.Length);
            foreach (var reference in generatorReferences)
            {
                Assert.AreEqual(new Version(4, 8, 0, 0), reference.Version,
                    "Changing the test host must not raise the shipping generator's compiler API baseline.");
            }
        }
    }
}
