using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class Milestone4FlatMappingTests
    {
        [TestMethod]
        public void FlatMappingGeneratesDirectAssignmentsAndRuns()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source
{
    public string? Name { get; set; }
    public int Age;
}

public sealed class Target
{
    public string? Name { get; set; }
    public int Age;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "var target = new Target();");
            StringAssert.Contains(generated, "target.Name = source.Name;");
            StringAssert.Contains(generated, "target.Age = source.Age;");
            Assert.AreEqual(
                Normalize(File.ReadAllText(Repository.Path("tests/Mammoth.LiteMapper.Generator.Tests/Snapshots/Milestone4FlatMapping.Mapper.g.cs"))),
                Normalize(generated));
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Name")!.SetValue(source, "Ada");
            source.GetType().GetField("Age")!.SetValue(source, 37);

            var target = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;

            Assert.AreEqual("Ada", target.GetType().GetProperty("Name")!.GetValue(target));
            Assert.AreEqual(37, target.GetType().GetField("Age")!.GetValue(target));
        }

        [TestMethod]
        public void NameMatchingPolicyControlsCaseFallback()
        {
            var exact = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(NameMatching = NameMatching.Exact)]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public int name { get; set; } }
public sealed class Target { public int Name { get; set; } }
");

            AssertDiagnostic(exact.RunResult, "LITEMAPPER1001");

            var fallback = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public int name { get; set; } }
public sealed class Target { public int Name { get; set; } }
");

            AssertNoLiteMapperDiagnostics(fallback.RunResult);
            StringAssert.Contains(SingleGeneratedSource(fallback.RunResult), "target.Name = source.name;");
        }

        [TestMethod]
        public void UnmappedPoliciesReportConfiguredDiagnostics()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(UnmappedSourceMembers = UnmappedMemberPolicy.Warning, UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source
{
    public int Used { get; set; }
    public int Extra { get; set; }
}

public sealed class Target
{
    public int Used { get; set; }
    public int Missing { get; set; }
}
");

            AssertDiagnostic(result.RunResult, "LITEMAPPER1001");
            AssertDiagnostic(result.RunResult, "LITEMAPPER1003");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        public void InvalidFlatMappingMembersReportDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public int Id { get; set; } public int ID { get; set; } }
public sealed class Target { public int id { get; set; } }
").RunResult, "LITEMAPPER1004");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public string Name { get; set; } = string.Empty; }
public sealed class Target { public int Name { get; set; } }
").RunResult, "LITEMAPPER2004");
        }

        [TestMethod]
        public void InheritedMembersMapAndHiddenMembersWarn()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public class SourceBase { public int Id { get; set; } public string? Name { get; set; } }
public sealed class Source : SourceBase { public new string? Name { get; set; } }
public class TargetBase { public int Id { get; set; } }
public sealed class Target : TargetBase { public string? Name { get; set; } }
");

            AssertDiagnostic(result.RunResult, "LITEMAPPER1005");
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "target.Id = source.Id;");
            StringAssert.Contains(generated, "target.Name = source.Name;");
        }

        [TestMethod]
        public void NullableRootAndMemberBehaviorIsEnforced()
        {
            var nullableReturn = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target? ToTarget(Source? source);
}

public sealed class Source { public string? Name { get; set; } }
public sealed class Target { public string? Name { get; set; } }
");

            AssertNoLiteMapperDiagnostics(nullableReturn.RunResult);
            StringAssert.Contains(SingleGeneratedSource(nullableReturn.RunResult), "if (source == null)");
            StringAssert.Contains(SingleGeneratedSource(nullableReturn.RunResult), "return null;");

            var nonNullReturn = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source? source);
}

public sealed class Source { public string? Name { get; set; } }
public sealed class Target { public string? Name { get; set; } }
");

            AssertDiagnostic(nonNullReturn.RunResult, "LITEMAPPER2001");

            var memberMismatch = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public string? Name { get; set; } }
public sealed class Target { public string Name { get; set; } = string.Empty; }
");

            AssertDiagnostic(memberMismatch.RunResult, "LITEMAPPER2001");
        }

        private static void AssertNoRuntimeFeatures(string source)
        {
            Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("dynamic", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("Assembly", StringComparison.Ordinal), source);
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").TrimEnd();
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id), "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(static d => d.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
        }

        private static GeneratorRun RunGenerator(string source, LanguageVersion languageVersion = LanguageVersion.CSharp9)
        {
            var compilation = CreateCompilation(source, languageVersion);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(languageVersion),
                optionsProvider: new TestAnalyzerConfigOptionsProvider(),
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static Compilation CreateCompilation(string source, LanguageVersion languageVersion)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Console.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });

            return CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(languageVersion)) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString())));
            stream.Position = 0;
            return Assembly.Load(stream.ToArray());
        }

        private sealed class GeneratorRun
        {
            public GeneratorRun(GeneratorDriverRunResult runResult, Compilation compilation)
            {
                RunResult = runResult;
                Compilation = compilation;
            }

            public GeneratorDriverRunResult RunResult { get; }

            public Compilation Compilation { get; }
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private static readonly AnalyzerConfigOptions Empty = new TestAnalyzerConfigOptions();

            public override AnalyzerConfigOptions GlobalOptions => Empty;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = string.Empty;
                return false;
            }
        }
    }
}
