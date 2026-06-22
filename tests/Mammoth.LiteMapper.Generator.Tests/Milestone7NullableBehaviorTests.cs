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
    public sealed class Milestone7NullableBehaviorTests
    {
        [TestMethod]
        public void NullableMismatchThrowEmitsRuntimeChecksForRootMemberAndSourcePath()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Country))]
    public static partial Target ToTarget(Source? source);
}

public sealed class Source
{
    public string? Name { get; set; }
    public Address? Address { get; set; }
}

public sealed class Address { public Country? Country { get; set; } }
public sealed class Country { public string? Code { get; set; } }

public sealed class Target
{
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "throw new global::System.ArgumentNullException(nameof(source))");
            StringAssert.Contains(generated, "Source member 'Name' was null");
            StringAssert.Contains(generated, "Source member path 'Address.Country.Code' was null");
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var method = assembly.GetType("Mapper")!.GetMethod("ToTarget")!;
            AssertThrowsTargetInvocation(() => method.Invoke(null, new object?[] { null }));

            var source = assembly.CreateInstance("Source")!;
            AssertThrowsTargetInvocation(() => method.Invoke(null, new[] { source }));
        }

        [TestMethod]
        public void NonNullRootSourceGuardIsDisabledByDefault()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public string Name { get; set; } = string.Empty; }
public sealed class Target { public string Name { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            Assert.IsFalse(generated.Contains("ArgumentNullException(nameof(source))", StringComparison.Ordinal), generated);
        }

        [TestMethod]
        public void GuardNonNullSourceCanBeEnabledAndOverriddenPerMethod()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(GuardNonNullSource = true)]
public static partial class Mapper
{
    public static partial Target Guarded(Source source);

    [MappingOptions(GuardNonNullSource = OptionState.Disabled)]
    public static partial Target Unguarded(Source source);
}

public sealed class Source { public string Name { get; set; } = string.Empty; }
public sealed class Target { public string Name { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);

            var guarded = SliceMethod(generated, "Guarded");
            var unguarded = SliceMethod(generated, "Unguarded");

            StringAssert.Contains(guarded, "throw new global::System.ArgumentNullException(nameof(source))");
            Assert.IsFalse(unguarded.Contains("ArgumentNullException(nameof(source))", StringComparison.Ordinal), unguarded);

            var assembly = Emit(result.Compilation);
            var mapper = assembly.GetType("Mapper")!;
            AssertThrowsTargetInvocation(() => mapper.GetMethod("Guarded")!.Invoke(null, new object?[] { null }));

            try
            {
                mapper.GetMethod("Unguarded")!.Invoke(null, new object?[] { null });
                Assert.Fail("Expected TargetInvocationException.");
            }
            catch (TargetInvocationException ex)
            {
                Assert.IsNotInstanceOfType(ex.InnerException, typeof(ArgumentNullException));
            }
        }

        [TestMethod]
        public void NullableMismatchErrorOmitsInvalidImplementation()
        {
            var result = RunGenerator(@"
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

            AssertDiagnostic(result.RunResult, "LITEMAPPER2001");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        public void NullableObliviousMembersDoNotProduceNullableMismatchDiagnostics()
        {
            var result = RunGenerator(@"
#nullable disable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public string Name { get; set; } }
public sealed class Target { public string Name { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static string SliceMethod(string generated, string methodName)
        {
            var start = generated.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, generated);
            var next = generated.IndexOf("public static partial", start + methodName.Length, StringComparison.Ordinal);
            return next < 0 ? generated.Substring(start) : generated.Substring(start, next - start);
        }

        private static void AssertNoRuntimeFeatures(string source)
        {
            Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("dynamic", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("Assembly", StringComparison.Ordinal), source);
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id), "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        private static void AssertThrowsTargetInvocation(Action action)
        {
            try
            {
                action();
                Assert.Fail("Expected TargetInvocationException.");
            }
            catch (TargetInvocationException)
            {
            }
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
