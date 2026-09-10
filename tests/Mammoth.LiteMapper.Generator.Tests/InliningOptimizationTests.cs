using System;
using System.IO;
using System.Linq;
using Mammoth.LiteMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class InliningOptimizationTests
    {
        [TestMethod]
        public void SmallStraightLineRootMappingUsesAggressiveInlining()
        {
            var result = RunGenerator(@"
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
");

            AssertNoDiagnostics(result);
            Assert.AreEqual(1, result.RunResult.GeneratedTrees.Length);

            var generated = result.RunResult.GeneratedTrees[0].GetText().ToString();
            StringAssert.Contains(
                generated,
                "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            StringAssert.Contains(generated, "partial Target Map(Source source)");
        }

        [TestMethod]
        public void GuardedRootMappingDoesNotRequestAggressiveInlining()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(GuardNonNullSource = true)]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}

public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertNoDiagnostics(result);
            Assert.IsFalse(
                result.RunResult.GeneratedTrees[0].GetText().ToString().Contains("MethodImplOptions.AggressiveInlining", StringComparison.Ordinal),
                "A generated null guard makes the mapper more than a direct straight-line copy.");
        }

        [TestMethod]
        public void CollectionRootMappingDoesNotRequestAggressiveInlining()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial int[] Map(int[] source);
}
");

            AssertNoDiagnostics(result);
            Assert.IsFalse(
                result.RunResult.GeneratedTrees[0].GetText().ToString().Contains("MethodImplOptions.AggressiveInlining", StringComparison.Ordinal),
                "Collection mappings contain generated control flow and must stay outside this optimization.");
        }

        [TestMethod]
        public void OtherGeneratedControlFlowDoesNotRequestAggressiveInlining()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class NullableMapper
{
    public static partial NullableTarget? Map(NullableSource? source);
}

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class RecursiveMapper
{
    public static partial RecursiveTarget Map(RecursiveSource source);
}

[LiteMapper]
public static partial class ConstructorMapper
{
    public static partial ConstructorTarget Map(ConstructorSource source);
}

[LiteMapper]
public static partial class NestedMapper
{
    public static partial NestedTarget Map(NestedSource source);
}

[LiteMapper]
public static partial class UpdateMapper
{
    public static partial void Map(UpdateSource source, UpdateTarget destination);
}

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class PreconditionMapper
{
    [MapProperty(Source = ""Child.Value"", Target = nameof(PreconditionTarget.Value))]
    public static partial PreconditionTarget Map(PreconditionSource source);
}

public sealed class NullableSource { public int Value { get; set; } }
public sealed class NullableTarget { public int Value { get; set; } }
public sealed class RecursiveSource { public int Value { get; set; } public RecursiveSource? Next { get; set; } }
public sealed class RecursiveTarget { public int Value { get; set; } public RecursiveTarget? Next { get; set; } }
public sealed class ConstructorSource { public int Value { get; set; } }
public sealed class ConstructorTarget { public ConstructorTarget(int value) { Value = value; } public int Value { get; } }
public sealed class NestedSource { public NestedChildSource Child { get; set; } = new NestedChildSource(); }
public sealed class NestedTarget { public NestedChildTarget Child { get; set; } = new NestedChildTarget(); }
public sealed class NestedChildSource { public int Value { get; set; } }
public sealed class NestedChildTarget { public int Value { get; set; } }
public sealed class UpdateSource { public int Value { get; set; } }
public sealed class UpdateTarget { public int Value { get; set; } }
public sealed class PreconditionSource { public PreconditionChild? Child { get; set; } }
public sealed class PreconditionChild { public int Value { get; set; } }
public sealed class PreconditionTarget { public int Value { get; set; } }
");

            AssertNoDiagnostics(result);
            var generated = string.Join(
                Environment.NewLine,
                result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
            StringAssert.Contains(generated, "source.Child?.Value ?? throw");
            Assert.IsFalse(
                generated.Contains("MethodImplOptions.AggressiveInlining", StringComparison.Ordinal),
                "Nullable, recursive, constructor-bound, nested-helper, update, and precondition mappings all require generated work outside a direct root copy.");
        }

        [TestMethod]
        public void InaccessibleOrInexactMethodImplSymbolsUsePortableFallback()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

namespace System.Runtime.CompilerServices
{
    public sealed class MethodImplOptions
    {
        private const int AggressiveInlining = 1;
    }

    public sealed class MethodImplAttribute : System.Attribute
    {
        public MethodImplAttribute(MethodImplOptions options) { }
    }
}

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}

public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertNoDiagnostics(result);
            Assert.IsFalse(
                result.RunResult.GeneratedTrees[0].GetText().ToString().Contains("MethodImplOptions.AggressiveInlining", StringComparison.Ordinal),
                "The generator must use ordinary portable code when the exact public framework symbols are unavailable.");
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static Compilation CreateCompilation(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static path => path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Append(MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location));

            return CSharpCompilation.Create(
                "InliningOptimizationTests",
                new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9)) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static void AssertNoDiagnostics(GeneratorRun result)
        {
            var diagnostics = result.RunResult.Diagnostics
                .Concat(result.Compilation.GetDiagnostics())
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.AreEqual(0, diagnostics.Length,
                string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
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
