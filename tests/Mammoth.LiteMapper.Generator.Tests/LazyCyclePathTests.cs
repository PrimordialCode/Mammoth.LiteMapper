using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mammoth.LiteMapper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class LazyCyclePathTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SuccessfulChainAllocationsScaleLinearlyAtSafeDepths(bool tracking)
        {
            var result = Generate(ChainFixture(tracking) + @"
public static class Probe
{
    public static long[] Measure()
    {
        var depths = new[] { 16, 32, 64, 128, 256 };
        var allocated = new long[depths.Length * 3];
        for (var d = 0; d < depths.Length; d++)
        {
            var root = Node.Chain(depths[d]);
            for (var warmup = 0; warmup < 40; warmup++) Validate(Mapper.Map(root), depths[d]);
            for (var round = 0; round < 3; round++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 100; i++) Validate(Mapper.Map(root), depths[d]);
                allocated[round * depths.Length + d] = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
            }
        }
        return allocated;
    }
    private static void Validate(NodeDto? value, int depth)
    {
        for (var i = 0; i < depth; i++, value = value!.Child)
            if (value == null || value.Value != i) throw new InvalidOperationException(""Invalid finite chain"");
        if (value != null) throw new InvalidOperationException(""Unexpected tail"");
    }
}
");
            var values = (long[])Compile(result).GetType("Probe")!.GetMethod("Measure")!.Invoke(null, null)!;
            Console.WriteLine((tracking ? "ThrowOnCycle" : "None") + " bytes/op at 16/32/64/128/256: " + string.Join(", ", values));
            for (var round = 0; round < 3; round++)
            {
                // A broad allocation ratio rejects quadratic prefixes without a machine-sensitive latency budget.
                Assert.IsTrue(values[round * 5 + 4] < values[round * 5 + 3] * 3,
                    "Doubling safe depth must not allocate quadratic full-path prefixes: " + string.Join(", ", values));
                if (!tracking)
                    Assert.AreEqual(values[round * 5] * 16, values[round * 5 + 4], "None must allocate only destination nodes.");
            }
        }

        [TestMethod]
        public void ShallowAndWideTraversalAllocationControls()
        {
            var result = Generate(ChainFixture(true) + @"
public static class Probe
{
    public static long[] Measure(bool tracking)
    {
        Func<Node, NodeDto> map = tracking ? source => Mapper.Map(source) : Manual;
        var roots = new[] { Node.Chain(1), Node.Chain(2) };
        var values = new long[6];
        for (var round = 0; round < 3; round++)
        for (var depth = 0; depth < roots.Length; depth++)
        {
            for (var warmup = 0; warmup < 40; warmup++) GC.KeepAlive(map(roots[depth]));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) GC.KeepAlive(map(roots[depth]));
            values[round * 2 + depth] = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        }
        return values;
    }
    private static NodeDto Manual(Node source) => new NodeDto { Value = source.Value, Child = source.Child == null ? null : Manual(source.Child) };
}
");
            var measure = Compile(result).GetType("Probe")!.GetMethod("Measure")!;
            var values = (long[])measure.Invoke(null, new object[] { true })!;
            var manual = (long[])measure.Invoke(null, new object[] { false })!;
            Console.WriteLine("ThrowOnCycle bytes/op at depth 1/2: " + string.Join(", ", values));
            // The first named edge already used a literal; it must not gain a path-node allocation.
            for (var round = 0; round < 3; round++) Assert.AreEqual(manual[round * 2 + 1] - manual[round * 2], values[round * 2 + 1] - values[round * 2]);

            var wide = Generate(ChainFixture(true).Replace("NodeDto Map(Node source)", "NodeDto[] Map(Node[] source)") + @"
public static class Probe
{
    public static long Measure(bool tracking)
    {
        Func<Node[], NodeDto[]> map = tracking ? source => Mapper.Map(source) : Manual;
        var roots = Enumerable.Range(0, 256).Select(_ => Node.Chain(2)).ToArray();
        for (var warmup = 0; warmup < 40; warmup++) GC.KeepAlive(map(roots));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) GC.KeepAlive(map(roots));
        return (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
    }
    private static NodeDto[] Manual(Node[] source)
    {
        var result = new NodeDto[source.Length];
        for (var i = 0; i < source.Length; i++) result[i] = new NodeDto
        {
            Value = source[i].Value,
            Child = new NodeDto { Value = source[i].Child!.Value }
        };
        return result;
    }
}
");
            var wideMeasure = Compile(wide).GetType("Probe")!.GetMethod("Measure")!;
            var bytes = (long)wideMeasure.Invoke(null, new object[] { true })!;
            var manualBytes = (long)wideMeasure.Invoke(null, new object[] { false })!;
            Console.WriteLine("ThrowOnCycle bytes/op for 256 sibling depth-2 chains: " + bytes);
            Assert.AreEqual(values[0] - manual[0], bytes - manualBytes, "Shallow siblings may share one tracker, but must not allocate a path object for every first edge.");
        }

        [TestMethod]
        public void GeneratedSuccessfulDescentStoresSegmentsAndFormatsOnlyAtCycleDetection()
        {
            var result = Generate(ChainFixture(true));
            Compile(result);
            var root = CSharpSyntaxTree.ParseText(result.Source).GetRoot();
            var tracker = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(type => type.Identifier.ValueText == "__LiteMapperCycleTracker");
            var path = tracker.Members.OfType<StructDeclarationSyntax>().SingleOrDefault();
            Assert.IsNotNull(path, "Diagnostic paths need compact linked segments instead of full string prefixes.");
            Assert.IsTrue(path.Members.OfType<FieldDeclarationSyntax>().All(field => field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)),
                "Immutable per-call segments cannot leak sibling or exception state.");
            Assert.AreEqual(1, path.Members.OfType<FieldDeclarationSyntax>().Count(field => field.Declaration.Type.ToString() == "string?"),
                "Each path entry retains one literal segment, never a materialized prefix.");
            Assert.IsFalse(result.Source.Contains("__memberPath +", StringComparison.Ordinal), result.Source);
            var enter = tracker.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Enter");
            var cycle = enter.DescendantNodes().OfType<ThrowStatementSyntax>().Single();
            StringAssert.Contains(cycle.ToString(), ".ToString()");
            Assert.AreEqual(1, root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "ToString"),
                "Full diagnostic formatting must occur only on the throwing path.");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(3)]
        [DataRow(64)]
        [DataRow(256)]
        public void DirectAndIndirectCyclesRetainExactPublicMetadata(int depth)
        {
            var result = Generate(ChainFixture(true) + @"
public static class Probe
{
    public static bool Run(int depth)
    {
        var source = Node.Chain(depth);
        var tail = source;
        while (tail.Child != null) tail = tail.Child;
        tail.Child = source;
        try { Mapper.Map(source); return false; }
        catch (LiteMapperCycleException error)
        {
            var path = string.Join(""."", Enumerable.Repeat(""Child"", depth));
            return error.SourceType == typeof(Node) && error.DestinationType == typeof(NodeDto) &&
                error.MappingMethod == ""Map"" && error.MemberPath == path &&
                error.Message == new LiteMapperCycleException(typeof(Node), typeof(NodeDto), ""Map"", path).Message;
        }
    }
}
");
            Assert.IsTrue((bool)Compile(result).GetType("Probe")!.GetMethod("Run")!.Invoke(null, new object[] { depth })!);
        }

        [TestMethod]
        public void SiblingBranchesAndConcurrentPublicCallsKeepIndependentPaths()
        {
            var result = Generate(ChainFixture(true).Replace("NodeDto Map(Node source)", "PairDto Map(Pair source)") + @"
public sealed class Pair { public Node First { get; set; } = null!; public Node Second { get; set; } = null!; }
public sealed class PairDto { public NodeDto First { get; set; } = null!; public NodeDto Second { get; set; } = null!; }
public static class Probe
{
    public static bool Run()
    {
        return Enumerable.Range(1, 16).AsParallel().All(depth =>
        {
            var shared = Node.Chain(depth);
            var pair = new Pair { First = shared, Second = shared };
            var mapped = Mapper.Map(pair);
            if (ReferenceEquals(mapped.First, mapped.Second) || mapped.Second.Value != 0) return false;
            var cycle = new Node(); cycle.Child = cycle;
            pair.Second = cycle;
            try { Mapper.Map(pair); return false; }
            catch (LiteMapperCycleException error)
            {
                if (error.MemberPath != ""Second.Child"" || error.MappingMethod != ""Map"") return false;
            }
            pair.Second = shared;
            return Mapper.Map(pair).Second.Value == 0;
        });
    }
}
");
            Assert.AreEqual(true, Compile(result).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TheSameTrackerCanBeReusedAfterCycleOrConverterFailure(bool converterFailure)
        {
            var result = Generate(ChainFixture(true).Replace("public static partial NodeDto Map(Node source);", @"
public static partial NodeDto Map(Node source);
public static readonly InvalidOperationException Failure = new InvalidOperationException(""converter failed"");
[MappingConverter] private static int Convert(int value) => value < 0 ? throw Failure : value;") + @"
public static class Probe
{
    public static Node Create() => Node.Chain(4);
    public static void Break(Node value, bool converter)
    {
        var tail = value; while (tail.Child != null) tail = tail.Child;
        if (converter) tail.Value = -1; else tail.Child = value;
    }
}
");
            var assembly = Compile(result);
            var mapper = assembly.GetType("Mapper")!;
            var trackerType = mapper.GetNestedType("__LiteMapperCycleTracker", BindingFlags.NonPublic)!;
            var tracker = Activator.CreateInstance(trackerType)!;
            var helper = mapper.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name.StartsWith("MapNested_", StringComparison.Ordinal));
            var pathType = helper.GetParameters()[2].ParameterType;
            object? EmptyPath() => pathType == typeof(string) ? string.Empty : Activator.CreateInstance(pathType);
            var probe = assembly.GetType("Probe")!;
            var broken = probe.GetMethod("Create")!.Invoke(null, null)!;
            probe.GetMethod("Break")!.Invoke(null, new[] { broken, converterFailure });
            var error = Assert.ThrowsExactly<TargetInvocationException>(() => helper.Invoke(null, new[] { broken, tracker, EmptyPath(), "Map" }));
            if (converterFailure) Assert.AreSame(mapper.GetField("Failure")!.GetValue(null), error.InnerException);
            else Assert.IsInstanceOfType<LiteMapperCycleException>(error.InnerException);
            var active = trackerType.GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker)!;
            Assert.AreEqual(0, active.GetType().GetProperty("Count")!.GetValue(active), "Every entered reference must unwind after failure.");
            var fresh = probe.GetMethod("Create")!.Invoke(null, null)!;
            Assert.IsNotNull(helper.Invoke(null, new[] { fresh, tracker, EmptyPath(), "Map" }));
            Assert.AreEqual(0, active.GetType().GetProperty("Count")!.GetValue(active));
        }

        [TestMethod]
        public void NonrecursiveMappingsEmitNoDiagnosticPathOrTrackerState()
        {
            var result = Generate(ChainFixture(true).Replace("public Node? Child { get; set; }", "public Leaf? Child { get; set; }")
                .Replace("public NodeDto? Child { get; set; }", "public LeafDto? Child { get; set; }")
                .Replace(ChainFactory, "") + @"
public sealed class Leaf { public int Value { get; set; } }
public sealed class LeafDto { public int Value { get; set; } }
");
            Compile(result);
            Assert.IsFalse(result.Source.Contains("__LiteMapperCycleTracker", StringComparison.Ordinal), result.Source);
        }

        [TestMethod]
        public void LazyDiagnosticPathEmissionIsDeterministic()
        {
            var source = ChainFixture(true);
            var first = Generate(source);
            var second = Generate(source);
            Compile(first);
            Assert.AreEqual(first.Source, second.Source);
        }

        private const string ChainFactory = @"
    public static Node Chain(int depth)
    {
        var root = new Node(); var tail = root;
        for (var i = 1; i < depth; i++) { tail.Child = new Node { Value = i }; tail = tail.Child; }
        return root;
    }";

        private static string ChainFixture(bool tracking) => @"
using System;
using System.Linq;
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling." + (tracking ? "ThrowOnCycle" : "None") + @")]
public static partial class Mapper { public static partial NodeDto Map(Node source); }
public sealed class Node
{
    public int Value { get; set; }
    public Node? Child { get; set; }
" + ChainFactory + @"
}
public sealed class NodeDto { public int Value { get; set; } public NodeDto? Child { get; set; } }
";

        private static Assembly Compile(GeneratorRun result)
        {
            var diagnostics = result.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray());
        }

        private static GeneratorRun Generate(string source)
        {
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create("LazyCyclePathTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                    optimizationLevel: OptimizationLevel.Release));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            var result = driver.GetRunResult();
            Assert.AreEqual(0, result.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Warning), string.Join(Environment.NewLine, result.Diagnostics));
            return new GeneratorRun(output, result.GeneratedTrees.Single().GetText().ToString());
        }

        private sealed class GeneratorRun
        {
            public GeneratorRun(Compilation compilation, string source) { Compilation = compilation; Source = source; }
            public Compilation Compilation { get; }
            public string Source { get; }
        }
    }
}
