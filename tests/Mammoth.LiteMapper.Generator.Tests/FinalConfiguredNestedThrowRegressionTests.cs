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
    public sealed class FinalConfiguredNestedThrowRegressionTests
    {
        // SPECIFICATION 9.4, 12.4, and 14.3 require the path check before nested mapping.
        [TestMethod]
        public void NullableConfiguredNestedLeafThrowsWithFullPathBeforeMappingAndMapsWhenPresent()
        {
            var result = RunGenerator(@"
#nullable enable
using System;
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Child"", Target = nameof(Target.Child))]
    public static partial Target Map(Source source);
}

public sealed class Source
{
    public Data Data { get; } = new Data();
}

public sealed class Data
{
    public ChildSource? Child { get; set; }
}

public sealed class ChildSource
{
    public int Value { get; set; }
}

public sealed class Target
{
    public ChildTarget Child { get; set; } = new ChildTarget();
}

public sealed class ChildTarget
{
    public int Value { get; set; }
}

public static class Probe
{
    public static bool Run()
    {
        var nullThrowsWithPath = false;
        try
        {
            Mapper.Map(new Source());
        }
        catch (InvalidOperationException exception)
        {
            nullThrowsWithPath = exception.Message.Contains(""Data.Child"", StringComparison.Ordinal);
        }

        var source = new Source();
        source.Data.Child = new ChildSource { Value = 3 };
        var present = Mapper.Map(source);
        return nullThrowsWithPath && present.Child.Value == 3;
    }
}
");

            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
            Assert.AreEqual(true, Execute(result.Compilation),
                "A null configured nested leaf must throw with the full path before the helper runs, while a present leaf must map normally.");
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "FinalConfiguredNestedThrowRegressionTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static bool Execute(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success,
                string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            return (bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
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
