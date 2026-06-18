using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Mammoth.LiteMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class Milestone13CapabilityBasedEmissionTests
    {
        [TestMethod]
        public void GeneratedSourceStaysCSharp9Compatible()
        {
            var result = RunGenerator(Source, LanguageVersion.CSharp9, Options.Empty);

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "partial Target Map(Source source)");
            Assert.IsFalse(generated.Contains("required ", StringComparison.Ordinal), generated);
            Assert.IsFalse(generated.Contains("init;", StringComparison.Ordinal), generated);
            AssertNoRuntimeFeatures(generated);
            AssertCompiles(result.Compilation);
        }

        [TestMethod]
        public void GeneratedSourceUsesAvailableFrameworkApiForEmptyArrays()
        {
            var result = RunGenerator(CollectionSource, LanguageVersion.CSharp11, Options.Empty);

            AssertNoLiteMapperDiagnostics(result.RunResult);
            StringAssert.Contains(SingleGeneratedSource(result.RunResult), "global::System.Array.Empty<int>()");
            AssertCompiles(result.Compilation);
        }

        [TestMethod]
        public void AnalyzerOptionsChangeGeneratedOutputWithoutChangingSemantics()
        {
            var first = RunGenerator(Source, LanguageVersion.CSharp11, Options.Empty).RunResult;
            var second = RunGenerator(Source, LanguageVersion.CSharp11, new Options(new Dictionary<string, string>
            {
                ["build_property.LiteMapper_IncludeGeneratedSourceComments"] = "true",
                ["build_property.LiteMapper_EmitDebugMetadata"] = "true"
            })).RunResult;

            var firstSource = SingleGeneratedSource(first);
            var secondSource = SingleGeneratedSource(second);
            Assert.AreNotEqual(firstSource, secondSource);
            StringAssert.Contains(secondSource, "// LiteMapper generated mapper: Mapper");
            StringAssert.Contains(secondSource, "// LiteMapper language version:");
            Assert.IsFalse(secondSource.Contains("System.Reflection", StringComparison.Ordinal), secondSource);
        }

        [TestMethod]
        public void TreatInternalGeneratorErrorsAsExceptionsOptionIsConsumed()
        {
            var result = RunGenerator(Source, LanguageVersion.CSharp11, new Options(new Dictionary<string, string>
            {
                ["build_property.LiteMapper_TreatInternalGeneratorErrorsAsExceptions"] = "true"
            }));

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertCompiles(result.Compilation);
        }

        private const string Source = @"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}

public sealed class Source
{
    public int Value { get; set; }
}

public sealed class Target
{
    public int Value { get; set; }
}
";

        private const string CollectionSource = @"
using Mammoth.LiteMapper;
using System.Collections.Generic;

[LiteMapper(NullCollections = NullCollectionStrategy.Empty)]
public static partial class Mapper
{
    public static partial int[] Map(List<int> source);
}
";

        private static GeneratorRun RunGenerator(string source, LanguageVersion languageVersion, AnalyzerConfigOptionsProvider options)
        {
            var compilation = CreateCompilation(source, languageVersion);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(languageVersion),
                optionsProvider: options,
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
                    p.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });

            return CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(languageVersion)) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static void AssertCompiles(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString())));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(static d => d.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
        }

        private static void AssertNoRuntimeFeatures(string source)
        {
            Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("dynamic", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("Assembly", StringComparison.Ordinal), source);
        }

        private sealed class Options : AnalyzerConfigOptionsProvider
        {
            public static readonly Options Empty = new Options(new Dictionary<string, string>());
            private readonly AnalyzerConfigOptions options;

            public Options(IReadOnlyDictionary<string, string> values)
            {
                options = new Values(values);
            }

            public override AnalyzerConfigOptions GlobalOptions => options;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => options;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => options;
        }

        private sealed class Values : AnalyzerConfigOptions
        {
            private readonly IReadOnlyDictionary<string, string> values;

            public Values(IReadOnlyDictionary<string, string> values)
            {
                this.values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                return values.TryGetValue(key, out value!);
            }
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
