using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class FinalConverterFallbackRegressionTests
    {
        [TestMethod]
        public void NullableDirectValuePatchSkipsNullAndConvertsPresentValue()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    public static int ConverterCalls;
    public static partial void Apply(Source source, Target target);

    [MappingConverter]
    private static string Convert(int value)
    {
        ConverterCalls++;
        return value.ToString();
    }
}

public sealed class Source { public int? Value { get; set; } }
public sealed class Target { public string Value { get; set; } = ""kept""; }

public static class Probe
{
    public static bool Run()
    {
        Mapper.ConverterCalls = 0;
        var target = new Target();
        Mapper.Apply(new Source { Value = null }, target);
        if (target.Value != ""kept"" || Mapper.ConverterCalls != 0) return false;

        Mapper.Apply(new Source { Value = 3 }, target);
        return target.Value == ""3"" && Mapper.ConverterCalls == 1;
    }
}
");

            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                "Section 16.3 requires the null guard to permit conversion of present values. " +
                string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null),
                "A null patch must leave the destination unchanged and skip the converter; a present 3 must invoke Convert(int) once.");
        }

        [TestMethod]
        public void NullablePathFallbackReportsDuplicateDefaultsForUnwrappedPair()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
[UseMapper(typeof(External))]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Value"", Target = nameof(Target.Value))]
    public static partial Target Map(Source source);

    [DefaultMapping]
    private static string Local(int value) => ""local"";
}

public static class External
{
    [DefaultMapping]
    public static string Registered(int value) => ""registered"";
}

public sealed class Source { public Data Data { get; } = new Data(); }
public sealed class Data { public int? Value { get; set; } = 3; }
public sealed class Target { public string Value { get; set; } = string.Empty; }
");

            Assert.IsTrue(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER3002"),
                "Section 11.2 forbids two visible defaults for int to string even when the configured int? path requires unwrapping. Actual diagnostics: " +
                string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var map = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map")
                .OfType<IMethodSymbol>().Single(method => method.PartialDefinitionPart == null);
            Assert.IsNull(map.PartialImplementationPart,
                "The duplicate-default error must omit the invalid implementation instead of silently choosing the local default.");
        }

        [TestMethod]
        public void DistinctNullableAndNonNullableDefaultPairsSelectNullableIdentityOverload()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static int NullableCalls;
    public static int NonNullableCalls;

    [MapProperty(Source = ""Data.Value"", Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source source);

    [DefaultMapping]
    private static string Convert(int? value)
    {
        NullableCalls++;
        return value.HasValue ? ""nullable:"" + value.Value : ""observed null"";
    }

    [DefaultMapping]
    private static string Convert(int value)
    {
        NonNullableCalls++;
        return ""non-nullable:"" + value;
    }
}

public sealed class Source { public Data Data { get; } = new Data(); }
public sealed class Data { public int? Value { get; set; } }
public sealed class Target { public string Value { get; set; } = string.Empty; }

public static class Probe
{
    public static bool Run()
    {
        Mapper.NullableCalls = Mapper.NonNullableCalls = 0;
        var source = new Source();
        source.Data.Value = 3;
        var present = Mapper.Map(source);
        source.Data.Value = null;
        var missing = Mapper.Map(source);
        return present.Value == ""nullable:3"" && missing.Value == ""observed null""
            && Mapper.NullableCalls == 2 && Mapper.NonNullableCalls == 0;
    }
}
");

            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                "Section 11.2 permits one default per distinct source/destination pair; int? to string and int to string must not produce LITEMAPPER3002. " +
                string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null),
                "Section 11.4 requires the nullable identity overload to win and receive null when the configured nullable leaf is null.");
        }

        [TestMethod]
        public void ExactDefaultDoesNotHideDuplicateDefaultsForFallbackPair()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
[UseMapper(typeof(External))]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Value"", Target = nameof(Target.Value))]
    public static partial Target Map(Source source);

    [DefaultMapping]
    private static string Local(int value) => ""local"";
}

public static class External
{
    [DefaultMapping]
    public static string Fallback(int value) => ""fallback"";

    [DefaultMapping]
    public static string Exact(int? value) => ""exact"";
}

public sealed class Source { public Data Data { get; } = new Data(); }
public sealed class Data { public int? Value { get; set; } = 3; }
public sealed class Target { public string Value { get; set; } = string.Empty; }
");

            Assert.IsTrue(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER3002"),
                "Section 11.2 forbids duplicate visible int-to-string defaults even when the distinct int?-to-string pair has one exact default. Actual diagnostics: " +
                string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var map = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map")
                .OfType<IMethodSymbol>().Single(method => method.PartialDefinitionPart == null);
            Assert.IsNull(map.PartialImplementationPart,
                "An exact default in a later stage must not hide the duplicate fallback pair selected by the local stage.");
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "FinalConverterFallback_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return (driver.GetRunResult(), updatedCompilation);
        }
    }
}
