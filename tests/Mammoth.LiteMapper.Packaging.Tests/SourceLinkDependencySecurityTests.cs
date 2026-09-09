using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class SourceLinkDependencySecurityTests
    {
        [TestMethod]
        [DataRow("Mammoth.LiteMapper")]
        [DataRow("Mammoth.LiteMapper.Abstractions")]
        [DataRow("Mammoth.LiteMapper.Generator")]
        public void ResolvedGitBuildTaskIsOutsideCve202662900AffectedVersions(string project)
        {
            const string packagePrefix = "Microsoft.Build.Tasks.Git/";
            const string advisory = "https://github.com/dotnet/sourcelink/security/advisories/GHSA-23fw-v26w-5fgq";
            var assetsPath = Repository.Path("src/" + project + "/obj/project.assets.json");
            Assert.IsTrue(File.Exists(assetsPath), "Restore the shipping projects before checking resolved build dependencies: " + assetsPath);
            using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
            var dependencies = assets.RootElement.GetProperty("libraries").EnumerateObject()
                .Where(library => library.Name.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(library => library.Name.Substring(packagePrefix.Length))
                .ToArray();
            Assert.AreEqual(1, dependencies.Length, project + " must resolve the Source Link Git build task so its version can be verified.");
            Assert.IsTrue(Version.TryParse(dependencies[0], out var parsed), "Review the advisory before accepting an unrecognized package version: " + dependencies[0]);
            var version = new Version(parsed!.Major, parsed.Minor, Math.Max(parsed.Build, 0));

            var affected = version == new Version(8, 0, 0) ||
                version >= new Version(10, 0, 102) && version <= new Version(10, 0, 110) ||
                version >= new Version(10, 0, 200) && version <= new Version(10, 0, 204) ||
                version >= new Version(10, 0, 300) && version <= new Version(10, 0, 301);
            Assert.IsFalse(affected, project + " resolves vulnerable Microsoft.Build.Tasks.Git " + dependencies[0] + ". See " + advisory);
        }
    }
}
