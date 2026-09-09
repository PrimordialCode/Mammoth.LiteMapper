using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class Milestone14PackagingAndAotTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ZeroWarningBuildSummaryIsAcceptedOnEitherOutputStream(bool standardError)
        {
            const string summary = "Mammoth.LiteMapper.Samples.Basic -> /work/bin/Release/net10.0/Mammoth.LiteMapper.Samples.Basic.dll\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n";
            AssertNoLiteMapperWarnings(standardError ? string.Empty : summary, standardError ? summary : string.Empty);
        }

        [TestMethod]
        [DataRow(false, "Consumer.cs(12,5): warning LITEMAPPER1001: Target member is unmapped. [Mammoth.LiteMapper.Samples.Basic.csproj]")]
        [DataRow(true, "Consumer.cs(12,5): warning LITEMAPPER1001: Target member is unmapped. [Mammoth.LiteMapper.Samples.Basic.csproj]")]
        [DataRow(false, "Consumer.cs(12,5): warning IL2026: Calling a method requiring unreferenced code. [Consumer.csproj]")]
        [DataRow(true, "Consumer.cs(12,5): warning IL2026: Calling a method requiring unreferenced code. [Consumer.csproj]")]
        [DataRow(false, "Mammoth.LiteMapper.Samples.Basic -> /work/bin/Release/net10.0/Mammoth.LiteMapper.Samples.Basic.dll\nBuild succeeded.\n    1 Warning(s)\n    0 Error(s)\n")]
        [DataRow(true, "Mammoth.LiteMapper.Samples.Basic -> /work/bin/Release/net10.0/Mammoth.LiteMapper.Samples.Basic.dll\nBuild succeeded.\n    1 Warning(s)\n    0 Error(s)\n")]
        public void ActualWarningsRemainRejectedOnEitherOutputStream(bool standardError, string warning)
        {
            Assert.ThrowsExactly<AssertFailedException>(() => AssertNoLiteMapperWarnings(
                standardError ? string.Empty : warning,
                standardError ? warning : string.Empty));
        }

        [TestMethod]
        public void PrimaryPackageContainsAnalyzerAndNoRoslynRuntimeAssets()
        {
            var feed = CreateFeed();
            PackAll(feed);

            var package = SinglePackage(feed, "Mammoth.LiteMapper");
            using var archive = ZipFile.OpenRead(package);
            var entries = archive.Entries.Select(static e => e.FullName).OrderBy(static e => e, StringComparer.Ordinal).ToArray();

            CollectionAssert.Contains(entries, "lib/netstandard2.0/Mammoth.LiteMapper.dll");
            CollectionAssert.Contains(entries, "analyzers/dotnet/cs/Mammoth.LiteMapper.Generator.dll");
            Assert.IsFalse(entries.Any(static e => e.Contains("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, entries));
        }

        [TestMethod]
        public void GeneratorPackageContainsAnalyzerAsset()
        {
            var feed = CreateFeed();
            PackAll(feed);

            using var archive = ZipFile.OpenRead(SinglePackage(feed, "Mammoth.LiteMapper.Generator"));
            var entries = archive.Entries.Select(static e => e.FullName).ToArray();

            CollectionAssert.Contains(entries, "analyzers/dotnet/cs/Mammoth.LiteMapper.Generator.dll");
        }

        [TestMethod]
        public void OnePackageConsumerBuildsRunsAndDoesNotCopyGeneratorOrRoslyn()
        {
            var feed = CreateFeed();
            PackAll(feed);
            var consumer = CreateConsumer(feed, "net10.0");

            RunDotnet("restore --no-cache", consumer);
            RunDotnet("run --no-restore", consumer);

            var output = Path.Combine(consumer, "bin", "Debug", "net10.0");
            Assert.IsTrue(File.Exists(Path.Combine(output, "Consumer.dll")), output);
            Assert.IsFalse(Directory.EnumerateFiles(output, "Mammoth.LiteMapper.Generator.dll", SearchOption.AllDirectories).Any());
            Assert.IsFalse(Directory.EnumerateFiles(output, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any());
        }

        [TestMethod]
        public void AbstractionsPackageCanBeConsumedIndependently()
        {
            var feed = CreateFeed();
            PackAll(feed);
            var consumer = CreateProject(feed, "AbstractionsOnly", "net10.0", "Mammoth.LiteMapper.Abstractions");
            File.WriteAllText(Path.Combine(consumer, "Program.cs"), @"
using Mammoth.LiteMapper;

var attribute = new LiteMapperAttribute();
if (attribute.NameMatching != NameMatching.Unspecified)
{
    throw new System.InvalidOperationException();
}
");

            RunDotnet("restore --no-cache", consumer);
            RunDotnet("run --no-restore", consumer);
        }

        [TestMethod]
        public void PackageEntriesAreDeterministic()
        {
            var first = CreateFeed();
            var second = CreateFeed();
            PackAll(first);
            PackAll(second);

            var firstPackages = Directory.GetFiles(first).Where(static p => p.EndsWith(".nupkg", StringComparison.Ordinal) || p.EndsWith(".snupkg", StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
            var secondPackages = Directory.GetFiles(second).Where(static p => p.EndsWith(".nupkg", StringComparison.Ordinal) || p.EndsWith(".snupkg", StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
            Assert.AreEqual(6, firstPackages.Length, "Every shipping package and symbol package must be compared.");
            CollectionAssert.AreEqual(firstPackages, secondPackages);
            foreach (var name in firstPackages)
            {
                CollectionAssert.AreEqual(PackageContentFingerprint(Path.Combine(first, name!)), PackageContentFingerprint(Path.Combine(second, name!)), name);
            }
        }

        [TestMethod]
        [DataRow("lib/netstandard2.0/Library.dll")]
        [DataRow("analyzers/dotnet/cs/Generator.dll")]
        [DataRow("Library.nuspec")]
        [DataRow("README.md")]
        [DataRow("Mammoth.png")]
        [DataRow("lib/netstandard2.0/Library.pdb")]
        [DataRow("build/Library.targets")]
        [DataRow("[Content_Types].xml")]
        public void PackageFingerprintDetectsSameLengthPayloadChanges(string path)
        {
            var feed = CreateFeed();
            var first = Path.Combine(feed, "first.nupkg");
            var second = Path.Combine(feed, "second.nupkg");
            WriteArchive(first, path, "first");
            WriteArchive(second, path, "other");
            Assert.IsFalse(PackageContentFingerprint(first).SequenceEqual(PackageContentFingerprint(second)), "Same-size changes must invalidate the package fingerprint: " + path);
        }

        private static void WriteArchive(string path, string entryName, string content)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
            writer.Write(content);
        }

        [TestMethod]
        public void FingerprintNormalizesNuGetIdsButRetainsMetadataContent()
        {
            var feed = CreateFeed();
            var files = new[] { "first", "other", "third" }.Select(name => Path.Combine(feed, name + ".nupkg")).ToArray();
            for (var i = 0; i < files.Length; i++)
            {
                var metadata = "package/services/metadata/core-properties/" + i + ".psmdcp";
                using var archive = ZipFile.Open(files[i], ZipArchiveMode.Create);
                using (var writer = new StreamWriter(archive.CreateEntry("_rels/.rels").Open()))
                {
                    writer.Write("<Relationships><Relationship Id='R" + i + "' Type='core-properties' Target='/" + metadata + "' /></Relationships>");
                }

                using var content = new StreamWriter(archive.CreateEntry(metadata).Open());
                content.Write(i == 2 ? "<creator>other</creator>" : "<creator>first</creator>");
            }

            CollectionAssert.AreEqual(PackageContentFingerprint(files[0]), PackageContentFingerprint(files[1]));
            Assert.IsFalse(PackageContentFingerprint(files[0]).SequenceEqual(PackageContentFingerprint(files[2])), "NuGet metadata content remains part of reproducibility validation.");
        }

        [TestMethod]
        public void TrimmedConsumerPublishesAndRunsWithoutLiteMapperWarnings()
        {
            var feed = CreateFeed();
            PackAll(feed);
            var consumer = CreateConsumer(feed, "net10.0");

            RunDotnet("publish -c Release -p:PublishTrimmed=true -p:TreatWarningsAsErrors=true", consumer);
            RunPublishedExecutable(FindPublishDirectory(consumer), "Consumer");
        }

        [TestMethod]
        public void PublicPackagesPassApiCompatibilityValidation()
        {
            var feed = CreateFeed();
            PackAll(feed);
            RunDotnet("tool restore", Repository.Root);
            foreach (var package in new[] { "Mammoth.LiteMapper.Abstractions", "Mammoth.LiteMapper" })
            {
                RunDotnet("apicompat package \"" + SinglePackage(feed, package) + "\" --run-api-compat", Repository.Root);
            }
        }

        [TestMethod]
        public void NativeAotConsumerPublishesAndRunsWithoutLiteMapperWarnings()
        {
            var feed = CreateFeed();
            PackAll(feed);
            var consumer = CreateConsumer(feed, "net10.0");

            RunDotnetAllowingMissingNativeAotPrerequisites("publish -c Release -p:PublishAot=true -p:TreatWarningsAsErrors=true", consumer);
            RunPublishedExecutable(FindPublishDirectory(consumer), "Consumer");
        }

        internal static string CreateFeed()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperPackages", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static void PackAll(string feed)
        {
            RunDotnet("pack src\\Mammoth.LiteMapper.Abstractions\\Mammoth.LiteMapper.Abstractions.csproj -c Release -o \"" + feed + "\"", Repository.Root);
            RunDotnet("pack src\\Mammoth.LiteMapper.Generator\\Mammoth.LiteMapper.Generator.csproj -c Release -o \"" + feed + "\"", Repository.Root);
            RunDotnet("pack src\\Mammoth.LiteMapper\\Mammoth.LiteMapper.csproj -c Release -o \"" + feed + "\"", Repository.Root);
        }

        private static string CreateConsumer(string feed, string targetFramework)
        {
            var directory = CreateProject(feed, "Consumer", targetFramework, "Mammoth.LiteMapper");
            File.WriteAllText(Path.Combine(directory, "Program.cs"), @"
using System.Collections.Generic;
using Mammoth.LiteMapper;

var target = StaticMapper.ToTarget(new Source
{
    Name = ""Ada"",
    Scores = new[] { 1, 2, 3 },
    Children = new[] { new ChildSource { Value = 41 }, new ChildSource { Value = 42 } }
});
if (target.Name != ""Ada"" || target.Scores.Length != 3 || target.Children.Count != 2 || target.Children[1].Value != 42)
{
    throw new System.InvalidOperationException(""Static mapping failed."");
}

var instanceTarget = new InstanceMapper().ToTarget(new InstanceSource { Id = 7 });
if (instanceTarget.Id != 7)
{
    throw new System.InvalidOperationException(""Instance mapping failed."");
}

var node = new NodeSource();
node.Next = node;
try
{
    CycleMapper.ToTarget(node);
    throw new System.InvalidOperationException(""Cycle detection failed."");
}
catch (LiteMapperCycleException)
{
}

[LiteMapper]
public static partial class StaticMapper
{
    public static partial Target ToTarget(Source source);
}

[LiteMapper]
public sealed partial class InstanceMapper
{
    public partial InstanceTarget ToTarget(InstanceSource source);
}

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class CycleMapper
{
    public static partial NodeTarget ToTarget(NodeSource source);
}

public sealed class Source
{
    public string? Name { get; set; }
    public int[] Scores { get; set; } = new int[0];
    public ChildSource[] Children { get; set; } = new ChildSource[0];
}

public sealed class Target
{
    public string? Name { get; set; }
    public int[] Scores { get; set; } = new int[0];
    public List<ChildTarget> Children { get; set; } = new List<ChildTarget>();
}

public sealed class ChildSource
{
    public int Value { get; set; }
}

public sealed class ChildTarget
{
    public int Value { get; set; }
}

public sealed class InstanceSource
{
    public int Id { get; set; }
}

public sealed class InstanceTarget
{
    public int Id { get; set; }
}

public sealed class NodeSource
{
    public NodeSource? Next { get; set; }
}

public sealed class NodeTarget
{
    public NodeTarget? Next { get; set; }
}
");
            return directory;
        }

        private static string CreateProject(string feed, string name, string targetFramework, string package)
        {
            var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperConsumers", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".csproj"), @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>" + targetFramework + @"</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>" + feed + @";https://api.nuget.org/v3/index.json</RestoreSources>
    <RestorePackagesPath>" + Path.Combine(directory, ".packages") + @"</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""" + package + @""" Version=""1.0.0"" />
  </ItemGroup>
</Project>
");
            return directory;
        }

        private static string SinglePackage(string feed, string packageId)
        {
            var packages = Directory.GetFiles(feed, packageId + ".1.0.0.nupkg");
            Assert.AreEqual(1, packages.Length, packageId);
            return packages[0];
        }

        /// <summary>
        /// Compares every uncompressed payload byte, including symbols and package metadata.
        /// NuGet-generated relationship IDs and core-property filenames carry no package semantics;
        /// normalize those identifiers while retaining their targets and metadata content.
        /// </summary>
        private static string[] PackageContentFingerprint(string package)
        {
            using var archive = ZipFile.OpenRead(package);
            return archive.Entries
                .Select(static entry =>
                {
                    using var content = entry.Open();
                    if (entry.FullName == "_rels/.rels")
                    {
                        var relationships = XDocument.Load(content);
                        foreach (var relationship in relationships.Root!.Elements())
                        {
                            relationship.Attribute("Id")?.Remove();
                            var target = relationship.Attribute("Target");
                            if (target != null)
                            {
                                target.Value = NormalizePackageEntryName(target.Value);
                            }
                        }

                        return entry.FullName + "|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relationships.ToString(SaveOptions.DisableFormatting))));
                    }

                    return NormalizePackageEntryName(entry.FullName) + "|" + Convert.ToHexString(SHA256.HashData(content));
                })
                .OrderBy(static e => e, StringComparer.Ordinal)
                .ToArray();
        }

        private static string NormalizePackageEntryName(string name)
        {
            const string coreProperties = "package/services/metadata/core-properties/";
            return name.TrimStart('/').StartsWith(coreProperties, StringComparison.Ordinal) && name.EndsWith(".psmdcp", StringComparison.Ordinal)
                ? coreProperties + "metadata.psmdcp"
                : name;
        }

        private static void RunPublishedExecutable(string directory, string assemblyName)
        {
            var executable = OperatingSystem.IsWindows() ? Path.Combine(directory, assemblyName + ".exe") : Path.Combine(directory, assemblyName);
            if (!File.Exists(executable))
            {
                executable = Path.Combine(directory, assemblyName + ".dll");
                RunDotnet('"' + executable + '"', directory);
                return;
            }

            RunProcess(executable, string.Empty, directory);
        }

        private static string FindPublishDirectory(string consumer)
        {
            var release = Path.Combine(consumer, "bin", "Release", "net10.0");
            var publishDirectories = Directory.GetDirectories(release, "publish", SearchOption.AllDirectories);
            Assert.AreEqual(1, publishDirectories.Length, string.Join(Environment.NewLine, publishDirectories));
            return publishDirectories[0];
        }

        internal static void RunDotnet(string arguments, string workingDirectory)
        {
            RunProcess("dotnet", arguments, workingDirectory);
        }

        private static void RunDotnetAllowingMissingNativeAotPrerequisites(string arguments, string workingDirectory)
        {
            var result = RunProcessCore("dotnet", arguments, workingDirectory);
            if (result.ExitCode == 0)
            {
                AssertNoLiteMapperWarnings(result.Output, result.Error);
                return;
            }

            if (result.Output.Contains("Platform linker not found", StringComparison.OrdinalIgnoreCase) ||
                result.Error.Contains("Platform linker not found", StringComparison.OrdinalIgnoreCase))
            {
                if (RequiresNativeAot())
                {
                    Assert.Fail("Native AOT publish prerequisite missing while Native AOT validation is required." + Environment.NewLine + result.Output + Environment.NewLine + result.Error);
                }

                Assert.Inconclusive("Native AOT publish prerequisite missing: platform linker not found.");
            }

            Assert.Fail("dotnet " + arguments + Environment.NewLine + result.Output + Environment.NewLine + result.Error);
        }

        private static bool RequiresNativeAot()
        {
            var value = Environment.GetEnvironmentVariable("LITEMAPPER_REQUIRE_NATIVE_AOT");
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void RunProcess(string fileName, string arguments, string workingDirectory)
        {
            var result = RunProcessCore(fileName, arguments, workingDirectory);
            Assert.AreEqual(0, result.ExitCode, fileName + " " + arguments + Environment.NewLine + result.Output + Environment.NewLine + result.Error);
            AssertNoLiteMapperWarnings(result.Output, result.Error);
        }

        private static ProcessResult RunProcessCore(string fileName, string arguments, string workingDirectory)
        {
            return TestProcess.Run(fileName, arguments, workingDirectory, TimeSpan.FromMinutes(10));
        }

        private static void AssertNoLiteMapperWarnings(string output, string error)
        {
            foreach (var text in new[] { output, error })
            {
                Assert.IsFalse(text.Contains("IL2", StringComparison.OrdinalIgnoreCase), text);
                Assert.IsFalse(text.Contains("Mammoth.LiteMapper", StringComparison.OrdinalIgnoreCase) &&
                    text.Split('\n').Any(line =>
                        !line.Trim().Equals("0 Warning(s)", StringComparison.OrdinalIgnoreCase) &&
                        line.Contains("warning", StringComparison.OrdinalIgnoreCase)), text);
            }
        }

    }
}
