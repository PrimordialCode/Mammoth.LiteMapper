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
    public sealed class ConfiguredPathConverterContractTests
    {
        [TestMethod]
        public void NullablePathInvokesNullableInputConverterWithNull()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static int Calls;

    [MapProperty(Source = ""Address.Code"", Target = nameof(Target.Code), Use = nameof(Normalize))]
    public static partial Target Map(Source source);

    private static string? Normalize(string? value)
    {
        Calls++;
        return value ?? ""missing"";
    }
}

public sealed class Source { public Address? Address { get; set; } }
public sealed class Address { public string Code { get; set; } = string.Empty; }
public sealed class Target { public string? Code { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        Mapper.Calls = 0;
        var target = Mapper.Map(new Source { Address = null });
        return Mapper.Calls == 1 && target.Code == ""missing"";
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void NullablePathToNonNullConverterReportsMismatchEvenForNullableTarget()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = ""Address.Code"", Target = nameof(Target.Code), Use = nameof(Normalize))]
    public static partial Target Map(Source source);

    private static string Normalize(string value) => value;
}

public sealed class Source { public Address? Address { get; set; } }
public sealed class Address { public string Code { get; set; } = string.Empty; }
public sealed class Target { public string? Code { get; set; } }
");

            AssertDiagnostic(result.RunResult, "LITEMAPPER2001");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        public void NullablePathThrowChecksFullPathBeforeNonNullConverter()
        {
            var result = RunGenerator(@"
#nullable enable
using System;
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    public static int Calls;

    [MapProperty(Source = ""Address.Code"", Target = nameof(Target.Code), Use = nameof(Normalize))]
    public static partial Target Map(Source source);

    private static string Normalize(string value)
    {
        Calls++;
        return value;
    }
}

public sealed class Source { public Address? Address { get; set; } }
public sealed class Address { public string Code { get; set; } = string.Empty; }
public sealed class Target { public string? Code { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        Mapper.Calls = 0;
        try
        {
            Mapper.Map(new Source { Address = null });
            return false;
        }
        catch (InvalidOperationException exception)
        {
            return Mapper.Calls == 0 && exception.Message.Contains(""Address.Code"", StringComparison.Ordinal);
        }
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        private static void AssertProbe(GeneratorRun result)
        {
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Id == id),
                "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Id)));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "ConfiguredPathConverterContractTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static MetadataReference[] References()
        {
            return AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) })
                .ToArray();
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
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
    }
}
