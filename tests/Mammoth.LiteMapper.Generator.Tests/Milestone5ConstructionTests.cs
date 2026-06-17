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
    public sealed class Milestone5ConstructionTests
    {
        [TestMethod]
        public void ParameterizedConstructorIsSelectedAndBoundMembersAreNotAssignedAgain()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
public sealed class Target
{
    public Target(int id) { Id = id; }
    public int Id { get; }
    public string Name { get; set; } = string.Empty;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "new Target(id: source.Id)");
            Assert.IsFalse(generated.Contains("target.Id =", StringComparison.Ordinal), generated);
            StringAssert.Contains(generated, "Name = source.Name");

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Id")!.SetValue(source, 42);
            source.GetType().GetProperty("Name")!.SetValue(source, "Ada");
            var target = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(42, target.GetType().GetProperty("Id")!.GetValue(target));
            Assert.AreEqual("Ada", target.GetType().GetProperty("Name")!.GetValue(target));
        }

        [TestMethod]
        public void ConstructorDiagnosticsUseStableIds()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } public int Other { get; set; } }
public sealed class Target
{
    public Target(int id) { }
    public Target(int other) { }
}
").RunResult, "LITEMAPPER1011");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target
{
    [MappingConstructor] public Target(int id) { }
}
").RunResult, "LITEMAPPER1013");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target
{
    [MappingConstructor] public Target(int id) { }
    [MappingConstructor] public Target() { }
}
").RunResult, "LITEMAPPER1012");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target
{
    private Target() { }
}
").RunResult, "LITEMAPPER1010");
        }

        [TestMethod]
        public void OptionalParametersRecordsInitRequiredAndValueTypesAreSupported()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
    public static partial PersonRecord ToRecord(Source source);
    public static partial PointStruct ToStruct(Source source);
}

public sealed class Source { public int Id { get; set; } public string Name { get; set; } = string.Empty; public int X { get; set; } public int Y { get; set; } }
public sealed class Target
{
    public Target(int id, int optional = 9) { Id = id; Optional = optional; }
    public int Id { get; }
    public int Optional { get; }
    public required string Name { get; init; }
}
public record PersonRecord(int Id) { public required string Name { get; init; } }
public readonly record struct PointStruct(int X, int Y);
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static t => t.GetText().ToString()));
            StringAssert.Contains(generated, "new Target(id: source.Id)");
            Assert.IsFalse(generated.Contains("default!", StringComparison.Ordinal), generated);
            StringAssert.Contains(generated, "Name = source.Name");
            StringAssert.Contains(generated, "new PersonRecord(Id: source.Id)");
            StringAssert.Contains(generated, "new PointStruct(X: source.X, Y: source.Y)");
        }

        [TestMethod]
        public void RequiredReadOnlyMembersRequireConstructorBindingUnlessConstructorSetsRequiredMembers()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target { public required string Name { get; } }
").RunResult, "LITEMAPPER1002");

            var result = RunGenerator(@"
using System.Diagnostics.CodeAnalysis;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target
{
    [SetsRequiredMembers]
    public Target() { Name = ""set""; }
    public required string Name { get; }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
        }

        [TestMethod]
        public void PrivateConstructorIsUsableWhenMapperIsNestedInTargetType()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

public sealed partial class Target
{
    private Target() { }
    public int Id { get; init; }

    [LiteMapper]
    public static partial class Mapper
    {
        public static partial Target ToTarget(Source source);
    }
}

public sealed class Source { public int Id { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            StringAssert.Contains(SingleGeneratedSource(result.RunResult), "new Target()");
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

        private static GeneratorRun RunGenerator(string source, LanguageVersion languageVersion = LanguageVersion.CSharp11)
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
