using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class DiagnosticEmissionCoverageTests
    {
        [TestMethod]
        public void AutomaticNestedMappingFailureEmitsLitemapper3004()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target Map(Source source);
}
public sealed class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public sealed class Target { public ChildTarget Child { get; set; } = null!; }
public sealed class ChildSource { public int Value { get; set; } }
public sealed class ChildTarget { public int Other { get; set; } }
");

            AssertDiagnostic(result, "LITEMAPPER3004");
        }

        [TestMethod]
        public void ExistingTargetReturnTypeMismatchEmitsLitemapper5001()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial int Apply(Source source, Target target);
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertDiagnostic(result, "LITEMAPPER5001");
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Id == id),
                "Expected " + id + ": " + string.Join(Environment.NewLine, result.Diagnostics));
            Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER9001"),
                string.Join(Environment.NewLine, result.Diagnostics));
        }

        private static GeneratorDriverRunResult Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "DiagnosticEmissionCoverageTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return driver.GetRunResult();
        }
    }
}
