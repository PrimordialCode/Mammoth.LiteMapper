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
    public sealed class HelperNameCollisionTests
    {
        [TestMethod]
        public void HandwrittenMemberCannotCollideWithGeneratedHelperSignature()
        {
            var helperName = "MapNested_Child_To_ChildDto_3A787437";
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
    private static ChildDto " + helperName + @"(Child source) => new ChildDto { Value = 99 };
}

public sealed class Source { public Child Child { get; set; } = new Child { Value = 7 }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Value { get; set; } }

public static class Probe
{
    public static bool Run() => Mapper.Map(new Source()).Child.Value == 7;
}
", "handwritten");

            AssertNoDiagnostics(result);
            var generated = GeneratedSource(result);
            StringAssert.Contains(generated, "Child = " + helperName + "_");
            Assert.IsFalse(generated.Contains("Child = " + helperName + "(source.Child)", StringComparison.Ordinal));
            Assert.IsTrue(EmitAndRun(result), generated);
        }

        [TestMethod]
        public void HandwrittenMemberCannotCollideWithGeneratedCollectionHelperSignature()
        {
            var helperName = "MapCollection_List_Child_To_List_ChildDto_F9A3C010";
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
    private static List<ChildDto> " + helperName + @"(List<Child> source) => new List<ChildDto>();
}

public sealed class Source { public List<Child> Items { get; set; } = new List<Child> { new Child { Value = 11 } }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public List<ChildDto> Items { get; set; } = null!; }
public sealed class ChildDto { public int Value { get; set; } }

public static class Probe
{
    public static bool Run() => Mapper.Map(new Source()).Items[0].Value == 11;
}
", "collection-handwritten");

            AssertNoDiagnostics(result);
            var generated = GeneratedSource(result);
            StringAssert.Contains(generated, "Items = " + helperName + "_");
            Assert.IsFalse(generated.Contains("Items = " + helperName + "(source.Items)", StringComparison.Ordinal));
            Assert.IsTrue(EmitAndRun(result), generated);
        }

        [TestMethod]
        public void DistinctClosedPairsWithPreferredNameHashCollisionReceiveDistinctHelpers()
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
    public N95286.Child Left { get; set; } = new N95286.Child { Value = 1 };
    public N127645.Child Right { get; set; } = new N127645.Child { Value = 2 };
}

public sealed class Target
{
    public M95286.ChildDto Left { get; set; } = null!;
    public M127645.ChildDto Right { get; set; } = null!;
}

namespace N95286 { public sealed class Child { public int Value { get; set; } } }
namespace N127645 { public sealed class Child { public int Value { get; set; } } }
namespace M95286 { public sealed class ChildDto { public int Value { get; set; } } }
namespace M127645 { public sealed class ChildDto { public int Value { get; set; } } }

public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new Source());
        return mapped.Left.Value == 1 && mapped.Right.Value == 2;
    }
}
", "hash-collision");

            AssertNoDiagnostics(result);
            var generated = GeneratedSource(result);
            StringAssert.Contains(generated, "MapNested_Child_To_ChildDto_964C6A1D_");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(
                generated, "private static .*MapNested_Child_To_ChildDto_964C6A1D").Count);
            Assert.IsTrue(EmitAndRun(result), generated);
        }

        [TestMethod]
        public void HelperAllocationIsStableWhenEquivalentSyntaxTreesAreReordered()
        {
            const string mapper = @"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}
public sealed class Source { public N95286.Child Left { get; set; } = new N95286.Child(); public N127645.Child Right { get; set; } = new N127645.Child(); }
public sealed class Target { public M95286.ChildDto Left { get; set; } = null!; public M127645.ChildDto Right { get; set; } = null!; }
";
            const string models = @"
namespace N95286 { public sealed class Child { public int Value { get; set; } } }
namespace N127645 { public sealed class Child { public int Value { get; set; } } }
namespace M95286 { public sealed class ChildDto { public int Value { get; set; } } }
namespace M127645 { public sealed class ChildDto { public int Value { get; set; } } }
";

            var first = RunGenerator(new[] { mapper, models }, "ordered");
            var second = RunGenerator(new[] { models, mapper }, "reversed");
            AssertNoDiagnostics(first);
            AssertNoDiagnostics(second);
            Assert.AreEqual(GeneratedSource(first), GeneratedSource(second));
        }

        [TestMethod]
        public void SameClosedHelperIdentityIsReusedAcrossDeclaredMappings()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target MapLeft(Source source);
    public static partial Target MapRight(Source source);
}
public sealed class Source { public Child Child { get; set; } = new Child { Value = 3 }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Value { get; set; } }
public static class Probe
{
    public static bool Run() => Mapper.MapLeft(new Source()).Child.Value == 3 && Mapper.MapRight(new Source()).Child.Value == 3;
}
", "shared");

            AssertNoDiagnostics(result);
            var generated = GeneratedSource(result);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(
                generated, "private static ChildDto MapNested_Child_To_ChildDto_3A787437").Count);
            Assert.IsTrue(EmitAndRun(result), generated);
        }

        [TestMethod]
        public void DiscardedMappingDoesNotConsumeHelperAllocation()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target MapBad(BadSource source);
    public static partial Target MapGood(GoodSource source);
}
public sealed class BadSource { public Child Child { get; set; } = new Child { Value = 5 }; }
public sealed class GoodSource { public Child Child { get; set; } = new Child { Value = 5 }; public string Required { get; set; } = ""ok""; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; public required string Required { get; init; } }
public sealed class ChildDto { public int Value { get; set; } }
", "discarded");

            var generated = GeneratedSource(result);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(
                generated, "private static ChildDto MapNested_Child_To_ChildDto_3A787437").Count);
        }

        [TestMethod]
        public void MappingRejectedAfterRecursiveAnalysisDoesNotConsumeHelperAllocation()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial GoodWrapperDto MapBad(BadWrapper source);
    [MappingOptions(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial GoodWrapperDto MapGood(GoodWrapper source);
}
public sealed class BadWrapper { public A Value { get; set; } = new A(); }
public sealed class GoodWrapper { public A Value { get; set; } = new A(); }
public sealed class GoodWrapperDto { public ADto Value { get; set; } = null!; }
public sealed class A { public A? Next { get; set; } }
public sealed class ADto { public ADto? Next { get; set; } }
", "recursive-discarded");

            var generated = GeneratedSource(result);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(
                generated, "private static ADto MapNested_A_To_ADto_").Count, generated);
        }

        private static GeneratorResult RunGenerator(string source, string assemblyName)
        {
            return RunGenerator(new[] { source }, assemblyName);
        }

        private static GeneratorResult RunGenerator(string[] sources, string assemblyName)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)).ToArray();
            var compilation = CSharpCompilation.Create(
                "HelperNameCollision_" + assemblyName,
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorResult(driver.GetRunResult(), updatedCompilation);
        }

        private static string GeneratedSource(GeneratorResult result)
        {
            return string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        }

        private static void AssertNoDiagnostics(GeneratorResult result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        private static bool EmitAndRun(GeneratorResult result)
        {
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            return (bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }

        private sealed class GeneratorResult
        {
            public GeneratorResult(GeneratorDriverRunResult runResult, Compilation compilation)
            {
                RunResult = runResult;
                Compilation = compilation;
            }

            public GeneratorDriverRunResult RunResult { get; }

            public Compilation Compilation { get; }
        }
    }
}
