using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class ConsumerMatrixTests
    {
        private static string packageFeed = string.Empty;

        [ClassInitialize]
        public static void PackCurrentLibrary(TestContext context)
        {
            packageFeed = Milestone14PackagingAndAotTests.CreateFeed();
            Milestone14PackagingAndAotTests.PackAll(packageFeed);
        }

        [TestMethod]
        [DataRow("netstandard2.0", "9.0")]
        [DataRow("netstandard2.0", "14.0")]
        [DataRow("netstandard2.0", "latest")]
        [DataRow("net8.0", "9.0")]
        [DataRow("net8.0", "14.0")]
        [DataRow("net8.0", "latest")]
        [DataRow("net9.0", "9.0")]
        [DataRow("net9.0", "14.0")]
        [DataRow("net9.0", "latest")]
        [DataRow("net10.0", "9.0")]
        [DataRow("net10.0", "14.0")]
        [DataRow("net10.0", "latest")]
        public void PackageMappingsCompileAndExecuteAcrossFrameworksAndLanguages(string framework, string language)
        {
            var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperMatrix", Guid.NewGuid().ToString("N"));
            var consumer = Path.Combine(directory, "consumer");
            Directory.CreateDirectory(consumer);
            var standard = framework == "netstandard2.0";
            var runtimeFramework = standard ? "net10.0" : framework;
            var runtimeMajor = runtimeFramework.Substring(3, runtimeFramework.IndexOf('.') - 3);
            var properties = Properties(framework, language, Path.Combine(directory, ".packages"));
            properties.Add(new XElement("OutputType", standard ? "Library" : "Exe"),
                new XElement("EmitCompilerGeneratedFiles", "true"),
                new XElement("CompilerGeneratedFilesOutputPath", "obj/generated"));
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties,
                new XElement("ItemGroup", new XElement("PackageReference",
                    new XAttribute("Include", "Mammoth.LiteMapper"), new XAttribute("Version", "1.0.0")))))
                .Save(Path.Combine(consumer, "Consumer.csproj"));
            File.WriteAllText(Path.Combine(consumer, "Consumer.cs"), Fixture.Replace("EXPECTED_RUNTIME_MAJOR", runtimeMajor, StringComparison.Ordinal));
            PackageSources(packageFeed, "https://api.nuget.org/v3/index.json")
                .Save(Path.Combine(directory, "NuGet.Config"));

            Milestone14PackagingAndAotTests.RunDotnet("restore --no-cache", consumer);
            Milestone14PackagingAndAotTests.RunDotnet("build -c Release --no-restore", consumer);
            var generated = Directory.GetFiles(Path.Combine(consumer, "obj", "generated"), "*.g.cs", SearchOption.AllDirectories);
            Assert.AreEqual(4, generated.Length, "All four mapper containers must generate in " + framework + "/" + language + ".");

            var executable = consumer;
            if (standard)
            {
                // .NET Standard is a library contract; execute its already generated assembly from a supported runtime.
                executable = Path.Combine(directory, "runner");
                Directory.CreateDirectory(executable);
                var runnerProperties = Properties(runtimeFramework, language, Path.Combine(directory, ".packages"));
                runnerProperties.Add(new XElement("OutputType", "Exe"));
                new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), runnerProperties,
                    new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../consumer/Consumer.csproj")))))
                    .Save(Path.Combine(executable, "Runner.csproj"));
                File.WriteAllText(Path.Combine(executable, "Program.cs"), "public static class Program { public static void Main() => MatrixFixture.Run(); }");
                Milestone14PackagingAndAotTests.RunDotnet("restore --no-cache", executable);
                Milestone14PackagingAndAotTests.RunDotnet("build -c Release --no-restore", executable);
            }

            Milestone14PackagingAndAotTests.RunDotnet("run -c Release --no-build --no-restore", executable);
            var output = Path.Combine(executable, "bin", "Release", runtimeFramework);
            Assert.IsFalse(Directory.EnumerateFiles(output, "Mammoth.LiteMapper.Generator.dll", SearchOption.AllDirectories).Any());
            Assert.IsFalse(Directory.EnumerateFiles(output, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any());
        }

        [TestMethod]
        [DataRow("Mammoth.LiteMapper")]
        [DataRow("Mammoth.LiteMapper.Abstractions")]
        public void MissingLocalPackageCannotFallBackToAnotherFeed(string package)
        {
            var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperMatrix", Guid.NewGuid().ToString("N"));
            var emptyFeed = Path.Combine(directory, "empty-feed");
            Directory.CreateDirectory(emptyFeed);
            foreach (var localAvailable in new[] { true, false })
            {
                var consumer = Path.Combine(directory, localAvailable ? "local-present" : "local-missing");
                Directory.CreateDirectory(consumer);
                new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    Properties("net10.0", "9.0", Path.Combine(consumer, ".packages")),
                    new XElement("ItemGroup", new XElement("PackageReference",
                        new XAttribute("Include", package), new XAttribute("Version", "1.0.0")))))
                    .Save(Path.Combine(consumer, "Consumer.csproj"));
                // The secondary feed contains the same valid package, simulating an already published version.
                PackageSources(localAvailable ? packageFeed : emptyFeed, packageFeed)
                    .Save(Path.Combine(consumer, "NuGet.Config"));
                var result = TestProcess.Run("dotnet", "restore --no-cache", consumer, TimeSpan.FromSeconds(60));
                var output = result.Output + result.Error;
                if (localAvailable)
                {
                    Assert.AreEqual(0, result.ExitCode, "The real local package must be restorable. " + output);
                }
                else
                {
                    Assert.AreNotEqual(0, result.ExitCode, "A published package must not replace the missing package under test. " + output);
                    StringAssert.Contains(output, "NU1101");
                    StringAssert.Contains(output, package);
                }
            }
        }

        private static XDocument PackageSources(string localFeed, string secondaryFeed) =>
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "LocalPackages"), new XAttribute("value", localFeed)),
                new XElement("add", new XAttribute("key", "NuGet"), new XAttribute("value", secondaryFeed))),
                new XElement("packageSourceMapping", new XElement("clear"),
                    new XElement("packageSource", new XAttribute("key", "LocalPackages"),
                        new XElement("package", new XAttribute("pattern", "Mammoth.LiteMapper*"))),
                    new XElement("packageSource", new XAttribute("key", "NuGet"),
                        new XElement("package", new XAttribute("pattern", "*"))))));

        private static XElement Properties(string framework, string language, string packages) => new XElement("PropertyGroup",
            new XElement("TargetFramework", framework), new XElement("LangVersion", language),
            new XElement("Nullable", "enable"), new XElement("ImplicitUsings", "disable"),
            new XElement("TreatWarningsAsErrors", "true"), new XElement("RestorePackagesPath", packages));

        private const string Fixture = @"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

public static class MatrixFixture
{
    public static void Main() => Run();

    public static void Run()
    {
        if (Environment.Version.Major != EXPECTED_RUNTIME_MAJOR) throw new InvalidOperationException(""Wrong runtime was used."");
        var source = new Source { Name = ""Ada"", Scores = new[] { 1, 2 }, Children = new[] { new ChildSource { Value = 7 } }, State = SourceState.Ready };
        var target = StaticMapper.Map(source);
        if (target.Name != ""Ada"" || target.Children.Count != 1 || target.Children[0].Value != 7 || target.State != TargetState.Ready)
            throw new InvalidOperationException(""Static, constructor, nested collection, or enum mapping failed."");
        if (ReferenceEquals(source.Scores, target.Scores) || target.Scores.Length != 2 || target.Scores[1] != 2)
            throw new InvalidOperationException(""Mutable arrays must be copied."");
        var instance = new InstanceMapper().Map(new ChildSource { Value = 9 });
        if (instance.Value != 9) throw new InvalidOperationException(""Instance mapping failed."");
        var existing = new ChildTarget { Value = 1 };
        if (!ReferenceEquals(existing, UpdateMapper.Apply(new ChildSource { Value = 11 }, existing)) || existing.Value != 11)
            throw new InvalidOperationException(""Existing-target identity or assignment failed."");
        try
        {
            StaticMapper.Map(null!);
            throw new InvalidOperationException(""The configured root guard was not emitted."");
        }
        catch (ArgumentNullException) { }
        var node = new NodeSource();
        node.Next = node;
        try
        {
            CycleMapper.Map(node);
            throw new InvalidOperationException(""Cycle detection failed."");
        }
        catch (LiteMapperCycleException exception)
        {
            if (exception.SourceType != typeof(NodeSource) || exception.DestinationType != typeof(NodeTarget))
                throw new InvalidOperationException(""Cycle details were lost."");
        }
    }
}

[LiteMapper(GuardNonNullSource = true)]
public static partial class StaticMapper { public static partial Target Map(Source source); }
[LiteMapper]
public sealed partial class InstanceMapper { public partial ChildTarget Map(ChildSource source); }
[LiteMapper]
public static partial class UpdateMapper { public static partial ChildTarget Apply(ChildSource source, ChildTarget destination); }
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class CycleMapper { public static partial NodeTarget Map(NodeSource source); }

public enum SourceState { None, Ready }
public enum TargetState { None, Ready = 10 }
public sealed class Source
{
    public string Name { get; set; } = string.Empty;
    public int[] Scores { get; set; } = new int[0];
    public ChildSource[] Children { get; set; } = new ChildSource[0];
    public SourceState State { get; set; }
}
public sealed class Target
{
    public Target(string name) { Name = name; }
    public string Name { get; }
    public int[] Scores { get; set; } = new int[0];
    public List<ChildTarget> Children { get; set; } = new List<ChildTarget>();
    public TargetState State { get; set; }
}
public sealed class ChildSource { public int Value { get; set; } }
public sealed class ChildTarget { public int Value { get; set; } }
public sealed class NodeSource { public NodeSource? Next { get; set; } }
public sealed class NodeTarget { public NodeTarget? Next { get; set; } }
";
    }
}
