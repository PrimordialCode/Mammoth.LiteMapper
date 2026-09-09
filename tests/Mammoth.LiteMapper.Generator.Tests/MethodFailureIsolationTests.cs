using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class MethodFailureIsolationTests
    {
        [TestMethod]
        [DataRow("public static partial BrokenTarget MInvalid(ref BrokenSource source);", "LITEMAPPER0005")]
        [DataRow("[MappingOptions(NameMatching = (NameMatching)999)] public static partial BrokenTarget MInvalid(BrokenSource source);", "LITEMAPPER0009")]
        [DataRow("public static partial BrokenTarget MInvalid(BrokenSource source);", "LITEMAPPER2004")]
        public void MethodErrorDoesNotSuppressIndependentMappings(string invalidMethod, string expectedDiagnostic)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target AValid(Source source);
    " + invalidMethod + @"
    public static partial Target ZValid(Source source);
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
public sealed class BrokenSource { public string Value { get; set; } = string.Empty; }
public sealed class BrokenTarget { public int Value { get; set; } }
");

            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == expectedDiagnostic),
                "The invalid method must retain its diagnostic: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var mapper = result.Compilation.GetTypeByMetadataName("Mapper")!;
            foreach (var methodName in new[] { "AValid", "ZValid" })
            {
                var method = mapper.GetMembers(methodName).OfType<IMethodSymbol>().Single();
                Assert.IsNotNull(method.PartialImplementationPart,
                    "Specification 20.3 requires independent methods to generate even before or after a failing method: " + methodName);
            }

            var invalid = mapper.GetMembers("MInvalid").OfType<IMethodSymbol>().Single();
            Assert.IsNull(invalid.PartialImplementationPart, "A fatal method error must not produce an invalid implementation.");
            AssertOnlyMissingImplementationError(result.Compilation);
        }

        [TestMethod]
        public void MapperConfigurationErrorSuppressesThatMapperOnly()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(NameMatching = (NameMatching)999)]
public static partial class InvalidMapper
{
    public static partial Target Map(Source source);
}
[LiteMapper]
public static partial class HealthyMapper
{
    public static partial Target Map(Source source);
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER0008"));
            var invalid = result.Compilation.GetTypeByMetadataName("InvalidMapper")!
                .GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNull(invalid.PartialImplementationPart, "Mapper-wide invalid configuration prevents every implementation in that mapper.");
            var healthy = result.Compilation.GetTypeByMetadataName("HealthyMapper")!
                .GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNotNull(healthy.PartialImplementationPart, "A mapper-wide error must not suppress an unrelated mapper.");
            AssertOnlyMissingImplementationError(result.Compilation);
        }

        private static void AssertOnlyMissingImplementationError(Compilation compilation)
        {
            var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(1, errors.Length,
                "Only the deliberately unimplemented invalid declaration may prevent compilation: " + string.Join(Environment.NewLine, errors.Select(static d => d.ToString())));
            Assert.AreEqual("CS8795", errors[0].Id, "Generated independent mappings must not introduce compiler errors.");
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
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
