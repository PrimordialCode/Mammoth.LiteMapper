using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class Milestone1RepositoryShapeTests
    {
        private static readonly string[] RequiredProjects =
        {
            "src/Mammoth.LiteMapper/Mammoth.LiteMapper.csproj",
            "src/Mammoth.LiteMapper.Abstractions/Mammoth.LiteMapper.Abstractions.csproj",
            "src/Mammoth.LiteMapper.Generator/Mammoth.LiteMapper.Generator.csproj",
            "tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj",
            "tests/Mammoth.LiteMapper.Runtime.Tests/Mammoth.LiteMapper.Runtime.Tests.csproj",
            "tests/Mammoth.LiteMapper.Packaging.Tests/Mammoth.LiteMapper.Packaging.Tests.csproj",
            "tests/Mammoth.LiteMapper.IntegrationTests/Mammoth.LiteMapper.IntegrationTests.csproj",
            "samples/Mammoth.LiteMapper.Samples.Basic/Mammoth.LiteMapper.Samples.Basic.csproj",
            "samples/Mammoth.LiteMapper.Samples.Collections/Mammoth.LiteMapper.Samples.Collections.csproj",
            "samples/Mammoth.LiteMapper.Samples.AspNetCore/Mammoth.LiteMapper.Samples.AspNetCore.csproj",
            "benchmarks/Mammoth.LiteMapper.Benchmarks/Mammoth.LiteMapper.Benchmarks.csproj",
        };

        [TestMethod]
        public void RequiredProjectsAndSolutionsExist()
        {
            foreach (var project in RequiredProjects)
            {
                Assert.IsTrue(File.Exists(Repository.Path(project)), project);
            }

            Assert.IsTrue(File.Exists(Repository.Path("Mammoth.LiteMapper.sln")));
            Assert.IsTrue(File.Exists(Repository.Path("Mammoth.LiteMapper.slnx")));
        }

        [TestMethod]
        public void SolutionFilesContainEveryProject()
        {
            var sln = File.ReadAllText(Repository.Path("Mammoth.LiteMapper.sln"));
            var slnx = File.ReadAllText(Repository.Path("Mammoth.LiteMapper.slnx"));

            foreach (var project in RequiredProjects)
            {
                var windowsPath = project.Replace('/', '\\');
                Assert.IsTrue(sln.Contains(windowsPath), windowsPath);
                Assert.IsTrue(slnx.Contains(windowsPath) || slnx.Contains(project), project);
            }
        }

        [TestMethod]
        public void ProjectsUseMammothLiteMapperNames()
        {
            foreach (var project in RequiredProjects)
            {
                var expected = Path.GetFileNameWithoutExtension(project);
                Assert.IsTrue(expected.StartsWith("Mammoth.LiteMapper"), expected);

                var xml = XDocument.Load(Repository.Path(project));
                var packageId = ValueOrDefault(xml, "PackageId", expected);
                var assemblyName = ValueOrDefault(xml, "AssemblyName", expected);
                var rootNamespace = ValueOrDefault(xml, "RootNamespace", expected);

                Assert.IsTrue(packageId.StartsWith("Mammoth.LiteMapper"), project);
                Assert.IsTrue(assemblyName.StartsWith("Mammoth.LiteMapper"), project);
                Assert.IsTrue(rootNamespace.StartsWith("Mammoth.LiteMapper"), project);
            }
        }

        [TestMethod]
        public void TestProjectsUseOnlyMSTestAssertions()
        {
            foreach (var project in RequiredProjects.Where(p => p.StartsWith("tests/")))
            {
                var xml = XDocument.Load(Repository.Path(project));
                var references = xml.Descendants("PackageReference")
                    .Select(r => (string?)r.Attribute("Include"))
                    .Where(name => name != null)
                    .Cast<string>()
                    .ToArray();

                CollectionAssert.Contains(references, "MSTest.TestFramework");
                CollectionAssert.DoesNotContain(references, "FluentAssertions");
                CollectionAssert.DoesNotContain(references, "Shouldly");
            }
        }

        [TestMethod]
        public void PrimaryPackageDoesNotReferenceRoslynAsRuntimeDependency()
        {
            var xml = XDocument.Load(Repository.Path("src/Mammoth.LiteMapper/Mammoth.LiteMapper.csproj"));
            var packageReferences = xml.Descendants("PackageReference")
                .Select(r => (string?)r.Attribute("Include"))
                .Where(name => name != null)
                .Cast<string>()
                .ToArray();

            Assert.IsFalse(packageReferences.Any(name => name.StartsWith("Microsoft.CodeAnalysis")));
            Assert.IsTrue(xml.Descendants("ProjectReference").Any(r =>
                ((string?)r.Attribute("Include"))?.Contains("Mammoth.LiteMapper.Abstractions.csproj") == true));
            Assert.IsTrue(xml.Descendants("ProjectReference").Any(r =>
                ((string?)r.Attribute("Include"))?.Contains("Mammoth.LiteMapper.Generator.csproj") == true &&
                (string?)r.Attribute("OutputItemType") == "Analyzer" &&
                (string?)r.Attribute("ReferenceOutputAssembly") == "false" &&
                (string?)r.Attribute("PrivateAssets") == "all"));
        }

        [TestMethod]
        public void AbstractionsUsePublicApiAnalyzersAsPrivateBuildDependency()
        {
            var xml = XDocument.Load(Repository.Path("src/Mammoth.LiteMapper.Abstractions/Mammoth.LiteMapper.Abstractions.csproj"));
            var analyzerReference = xml.Descendants("PackageReference").SingleOrDefault(r =>
                (string?)r.Attribute("Include") == "Microsoft.CodeAnalysis.PublicApiAnalyzers");

            Assert.IsNotNull(analyzerReference);
            Assert.AreEqual("all", (string?)analyzerReference.Attribute("PrivateAssets"));
            Assert.IsTrue(File.Exists(Repository.Path("src/Mammoth.LiteMapper.Abstractions/PublicApi.Shipped.txt")));
            Assert.IsTrue(File.Exists(Repository.Path("src/Mammoth.LiteMapper.Abstractions/PublicApi.Unshipped.txt")));
        }

        private static string ValueOrDefault(XDocument document, string name, string defaultValue)
        {
            return document.Descendants(name).Select(e => e.Value).FirstOrDefault() ?? defaultValue;
        }
    }
}
