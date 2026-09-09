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
    public sealed class Milestone6ConfigurationAndConverterTests
    {
        [TestMethod]
        public void MapPropertySourcePathsIgnoreAndDefaultsAreHonored()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(UnmappedSourceMembers = UnmappedMemberPolicy.Warning, UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper
{
    [MapProperty(Source = ""Identifier"", Target = nameof(Target.Id))]
    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Country))]
    [IgnoreTarget(nameof(Target.Ignored))]
    [IgnoreSource(nameof(Source.Unused))]
    [UseTargetDefault(nameof(Target.Defaulted))]
    public static partial Target ToTarget(Source source);
}

public sealed class Source
{
    public int Identifier { get; set; }
    public Address Address { get; set; } = new Address();
    public string Unused { get; set; } = string.Empty;
}

public sealed class Address { public Country Country { get; set; } = new Country(); }
public sealed class Country { public string Code { get; set; } = string.Empty; }

public sealed class Target
{
    public int Id { get; set; }
    public string Country { get; set; } = string.Empty;
    public int Ignored { get; set; }
    public int Defaulted { get; set; } = 17;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "Id = source.Identifier");
            StringAssert.Contains(generated, "Country = source.Address.Country.Code");
            Assert.IsFalse(generated.Contains("Ignored =", StringComparison.Ordinal), generated);
            Assert.IsFalse(generated.Contains("Defaulted =", StringComparison.Ordinal), generated);
            Assert.AreEqual(
                Normalize(File.ReadAllText(Repository.Path("tests/Mammoth.LiteMapper.Generator.Tests/Snapshots/Milestone6ExplicitConfiguration.Mapper.g.cs"))),
                Normalize(generated));
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Identifier")!.SetValue(source, 9);
            var address = source.GetType().GetProperty("Address")!.GetValue(source)!;
            var country = address.GetType().GetProperty("Country")!.GetValue(address)!;
            country.GetType().GetProperty("Code")!.SetValue(country, "IT");

            var target = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(9, target.GetType().GetProperty("Id")!.GetValue(target));
            Assert.AreEqual("IT", target.GetType().GetProperty("Country")!.GetValue(target));
            Assert.AreEqual(17, target.GetType().GetProperty("Defaulted")!.GetValue(target));
        }

        [TestMethod]
        public void ExplicitAndConventionConvertersUsePrecedenceOrder()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Explicit), Use = nameof(ConvertExplicit))]
    [MapProperty(Target = nameof(Target.Root), Use = nameof(ConvertRoot))]
    public static partial Target ToTarget(Source source);

    private static string ConvertExplicit(int value) => ""explicit:"" + value.ToString();
    private static string ConvertRoot(Source source) => ""root:"" + source.Value.ToString();
    [MappingConverter] private static string ConvertMarked(int value) => ""marked:"" + value.ToString();
    private static string MapNamed(long value) => ""named:"" + value.ToString();
}

public sealed class Source { public int Explicit { get; set; } public int Root { get; set; } public int Marked { get; set; } public long Named { get; set; } public int Value { get; set; } }
public sealed class Target { public string Explicit { get; set; } = string.Empty; public string Root { get; set; } = string.Empty; public string Marked { get; set; } = string.Empty; public string Named { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "Explicit = ConvertExplicit(source.Value)");
            StringAssert.Contains(generated, "Root = ConvertRoot(source)");
            StringAssert.Contains(generated, "Marked = ConvertMarked(source.Marked)");
            StringAssert.Contains(generated, "Named = MapNamed(source.Named)");
        }

        [TestMethod]
        public void ExternalConvertersAndMappingsAreResolved()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[UseMapper(typeof(ExternalConversions))]
[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public static class ExternalConversions
{
    [MappingConverter] public static string Convert(int value) => ""external:"" + value.ToString();
    public static NameDto ToNameDto(Name source) => new NameDto { Value = source.Value };
}

public sealed class Source { public int Count { get; set; } public Name Name { get; set; } = new Name(); }
public sealed class Name { public string Value { get; set; } = string.Empty; }
public sealed class Target { public string Count { get; set; } = string.Empty; public NameDto Name { get; set; } = new NameDto(); }
public sealed class NameDto { public string Value { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "Count = ExternalConversions.Convert(source.Count)");
            StringAssert.Contains(generated, "Name = ExternalConversions.ToNameDto(source.Name)");
        }

        [TestMethod]
        public void InvalidExplicitConfigurationReportsDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { [MapProperty(Source = ""Missing"", Target = nameof(Target.Id))] public static partial Target ToTarget(Source source); }
public sealed class Source { public short Id { get; set; } }
public sealed class Target { public int Id { get; set; } }
").RunResult, "LITEMAPPER1006");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { [MapProperty(Source = nameof(Source.Id), Target = ""Nested.Id"")] public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public int Id { get; set; } }
").RunResult, "LITEMAPPER1007");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { [MapProperty(Source = nameof(Source.Id), Target = nameof(Target.Id))][MapProperty(Source = nameof(Source.Other), Target = nameof(Target.Id))] public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } public int Other { get; set; } }
public sealed class Target { public int Id { get; set; } }
").RunResult, "LITEMAPPER1008");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { [UseTargetDefault(nameof(Target.Id))] public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public int Id { get; set; } }
").RunResult, "LITEMAPPER1014");
        }

        [TestMethod]
        public void InvalidConvertersAndAmbiguityReportDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
    [MappingConverter] private static string One(int value) => value.ToString();
    [MappingConverter] private static string Two(int value) => value.ToString();
}
public sealed class Source { public short Id { get; set; } }
public sealed class Target { public string Id { get; set; } = string.Empty; }
").RunResult, "LITEMAPPER2011");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Id), Target = nameof(Target.Id), Use = nameof(Convert))]
    public static partial Target ToTarget(Source source);
    private static string Convert(int value) => value.ToString();
    private static string Convert(long value) => value.ToString();
}
public sealed class Source { public short Id { get; set; } }
public sealed class Target { public string Id { get; set; } = string.Empty; }
").RunResult, "LITEMAPPER2011");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Id), Target = nameof(Target.Id), Use = nameof(Convert))]
    public static partial Target ToTarget(Source source);
    private static void Convert(int value) { }
}
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public string Id { get; set; } = string.Empty; }
").RunResult, "LITEMAPPER2009");
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
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
