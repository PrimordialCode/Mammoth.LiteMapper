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
    public sealed class ConfiguredRootConverterIdentifierTests
    {
        [TestMethod]
        public void OmittedSourceConverterEscapesKeywordRootParameterIdentifier()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source @class);

    private static int Convert(Source source) => source.Value + 10;
}

public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }

public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { Value = 3 }).Value == 13;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            StringAssert.Contains(SingleGeneratedSource(result.RunResult), "Convert(@class)",
                "The root converter argument must use the escaped declaration identifier.");
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "ConfiguredRootConverterIdentifierTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                References(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static MetadataReference[] References()
        {
            return AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) })
                .ToArray();
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics
                .Where(static diagnostic => diagnostic.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(0, diagnostics.Length,
                string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success,
                string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
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
