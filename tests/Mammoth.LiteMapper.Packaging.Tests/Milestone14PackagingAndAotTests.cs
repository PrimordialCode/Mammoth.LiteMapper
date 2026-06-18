using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class Milestone14PackagingAndAotTests
    {
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

            CollectionAssert.AreEqual(
                PackageContentFingerprint(SinglePackage(first, "Mammoth.LiteMapper")),
                PackageContentFingerprint(SinglePackage(second, "Mammoth.LiteMapper")));
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
        public void NativeAotConsumerPublishesAndRunsWithoutLiteMapperWarnings()
        {
            var feed = CreateFeed();
            PackAll(feed);
            var consumer = CreateConsumer(feed, "net10.0");

            RunDotnetAllowingMissingNativeAotPrerequisites("publish -c Release -p:PublishAot=true -p:TreatWarningsAsErrors=true", consumer);
            RunPublishedExecutable(FindPublishDirectory(consumer), "Consumer");
        }

        private static string CreateFeed()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperPackages", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void PackAll(string feed)
        {
            RunDotnet("pack src\\Mammoth.LiteMapper.Abstractions\\Mammoth.LiteMapper.Abstractions.csproj -c Release -o \"" + feed + "\"", Repository.Root);
            RunDotnet("pack src\\Mammoth.LiteMapper.Generator\\Mammoth.LiteMapper.Generator.csproj -c Release -o \"" + feed + "\"", Repository.Root);
            RunDotnet("pack src\\Mammoth.LiteMapper\\Mammoth.LiteMapper.csproj -c Release -o \"" + feed + "\"", Repository.Root);
        }

        private static string CreateConsumer(string feed, string targetFramework)
        {
            var directory = CreateProject(feed, "Consumer", targetFramework, "Mammoth.LiteMapper");
            File.WriteAllText(Path.Combine(directory, "Program.cs"), @"
using Mammoth.LiteMapper;

var target = Mapper.ToTarget(new Source { Name = ""Ada"", Scores = new[] { 1, 2, 3 }, Child = new ChildSource { Value = 42 } });
if (target.Name != ""Ada"" || target.Scores.Length != 3 || target.Child.Value != 42)
{
    throw new System.InvalidOperationException(""Mapping failed."");
}

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source
{
    public string? Name { get; set; }
    public int[] Scores { get; set; } = new int[0];
    public ChildSource Child { get; set; } = new ChildSource();
}

public sealed class Target
{
    public string? Name { get; set; }
    public int[] Scores { get; set; } = new int[0];
    public ChildTarget Child { get; set; } = new ChildTarget();
}

public sealed class ChildSource
{
    public int Value { get; set; }
}

public sealed class ChildTarget
{
    public int Value { get; set; }
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

        private static string[] PackageContentFingerprint(string package)
        {
            using var archive = ZipFile.OpenRead(package);
            return archive.Entries
                .Where(static e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) ||
                    e.FullName.StartsWith("analyzers/", StringComparison.Ordinal) ||
                    e.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                .Select(static e => e.FullName + "|" + e.Length)
                .OrderBy(static e => e, StringComparer.Ordinal)
                .ToArray();
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

        private static void RunDotnet(string arguments, string workingDirectory)
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
                Assert.Inconclusive("Native AOT publish prerequisite missing: platform linker not found.");
            }

            Assert.Fail("dotnet " + arguments + Environment.NewLine + result.Output + Environment.NewLine + result.Error);
        }

        private static void RunProcess(string fileName, string arguments, string workingDirectory)
        {
            var result = RunProcessCore(fileName, arguments, workingDirectory);
            Assert.AreEqual(0, result.ExitCode, fileName + " " + arguments + Environment.NewLine + result.Output + Environment.NewLine + result.Error);
            AssertNoLiteMapperWarnings(result.Output, result.Error);
        }

        private static ProcessResult RunProcessCore(string fileName, string arguments, string workingDirectory)
        {
            using var process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, output, error);
        }

        private static void AssertNoLiteMapperWarnings(string output, string error)
        {
            Assert.IsFalse(output.Contains("IL2", StringComparison.OrdinalIgnoreCase), output);
            Assert.IsFalse(error.Contains("IL2", StringComparison.OrdinalIgnoreCase), error);
            Assert.IsFalse(output.Contains("Mammoth.LiteMapper", StringComparison.OrdinalIgnoreCase) && output.Contains("warning", StringComparison.OrdinalIgnoreCase), output);
            Assert.IsFalse(error.Contains("Mammoth.LiteMapper", StringComparison.OrdinalIgnoreCase) && error.Contains("warning", StringComparison.OrdinalIgnoreCase), error);
        }

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }

            public int ExitCode { get; }

            public string Output { get; }

            public string Error { get; }
        }
    }
}
