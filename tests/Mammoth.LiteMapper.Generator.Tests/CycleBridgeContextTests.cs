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
    public sealed class CycleBridgeContextTests
    {
        [TestMethod]
        [DataRow(0, "static")]
        [DataRow(1, "static")]
        [DataRow(3, "static")]
        [DataRow(3, "instance")]
        [DataRow(3, "update")]
        [DataRow(3, "constructor")]
        [DataRow(3, "struct")]
        public void ObjectBridgesForwardContextWithoutTrackingAcyclicVertices(int depth, string mode)
        {
            var result = Generate(ObjectFixture(depth, mode));
            AssertRuns(result);
            AssertTrackingBoundary(result.Source, "Node");
        }

        [TestMethod]
        [DataRow("array", false)]
        [DataRow("list", false)]
        [DataRow("dictionary", false)]
        [DataRow("array", true)]
        [DataRow("list", true)]
        [DataRow("dictionary", true)]
        public void CollectionBridgesPreserveContextAtMemberAndPublicEntries(string shape, bool rootCollection)
        {
            var sourceType = shape == "array" ? "Bridge[]" : shape == "list" ? "List<Bridge>" : "Dictionary<string, Bridge>";
            var targetType = sourceType.Replace("Bridge", "BridgeDto");
            var initial = shape == "array" ? "new[] { bridge, bridge }" : shape == "list" ? "new List<Bridge> { bridge, bridge }" : "new Dictionary<string, Bridge> { [\"first\"] = bridge, [\"second\"] = bridge }";
            var first = shape == "dictionary" ? "[\"first\"]" : "[0]";
            var second = shape == "dictionary" ? "[\"second\"]" : "[1]";
            var source = @"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper { public static partial TARGET Map(SOURCE source); }
public sealed class Root { public SOURCE_ITEMS Items { get; set; } = default!; }
public sealed class RootDto { public TARGET_ITEMS Items { get; set; } = default!; }
public sealed class Bridge { public Node Node { get; set; } = new Node(); }
public sealed class BridgeDto { public NodeDto Node { get; set; } = new NodeDto(); }
NODE_TYPES
public static class Probe
{
    public static bool Run()
    {
        var node = new Node { Value = 7, Next = new Node { Value = 9 } };
        var bridge = new Bridge { Node = node };
        var items = INITIAL;
        var source = INPUT;
        var mapped = Mapper.Map(source)ACCESS;
        if (mappedFIRST.Node.Value != 7 || mappedSECOND.Node.Next?.Value != 9 ||
            ReferenceEquals(mappedFIRST, mappedSECOND) || ReferenceEquals(mappedFIRST.Node, mappedSECOND.Node)) return false;
        node.Next = node;
        try { Mapper.Map(source); return false; }
        catch (LiteMapperCycleException error)
        {
            if (error.MemberPath != ""CYCLE_PATH"" || error.MappingMethod != ""Map"" ||
                error.SourceType != typeof(Node) || error.DestinationType != typeof(NodeDto)) return false;
        }
        node.Next = null;
        return Mapper.Map(source)ACCESSFIRST.Node.Next == null;
    }
}
".Replace("SOURCE_ITEMS", sourceType).Replace("TARGET_ITEMS", targetType)
                .Replace("TARGET", rootCollection ? targetType : "RootDto").Replace("SOURCE", rootCollection ? sourceType : "Root")
                .Replace("NODE_TYPES", NodeTypes).Replace("INITIAL", initial)
                .Replace("INPUT", rootCollection ? "items" : "new Root { Items = items }")
                .Replace("ACCESS", rootCollection ? "" : ".Items")
                .Replace("FIRST", first).Replace("SECOND", second)
                .Replace("CYCLE_PATH", rootCollection ? "Node.Next" : "Items.Node.Next");
            var result = Generate(source);
            AssertRuns(result);
            AssertTrackingBoundary(result.Source, "Node");
        }

        [TestMethod]
        [DataRow("None", true)]
        [DataRow("ThrowOnCycle", false)]
        public void BridgesWithoutTrackedRecursionAllocateNoTracker(string policy, bool recursive)
        {
            var source = ObjectFixture(3, "static", policy, recursive);
            var result = Generate(source);
            AssertRuns(result);
            Assert.IsFalse(result.Source.Contains("__LiteMapperCycleTracker", StringComparison.Ordinal), result.Source);
            Assert.AreEqual(recursive ? 1 : 0, result.Diagnostics.Count(d => d.Id == "LITEMAPPER6001"));
        }

        [TestMethod]
        public void SeparateCallsAndSharedBranchesUseIndependentTrackingState()
        {
            var result = Generate(ObjectFixture(3, "static"));
            var assembly = Compile(result);
            var run = assembly.GetType("Probe")!.GetMethod("Run")!;
            var outcomes = Enumerable.Range(0, 16).AsParallel().Select(_ => run.Invoke(null, null)).ToArray();
            Assert.IsTrue(outcomes.All(outcome => Equals(true, outcome)));
            AssertTrackingBoundary(result.Source, "Node");
        }

        [TestMethod]
        public void BridgeOutputIsDeterministic()
        {
            var source = ObjectFixture(3, "static");
            var first = Generate(source);
            var second = Generate(source);
            Assert.AreEqual(first.Source, second.Source);
            AssertRuns(first);
        }

        [TestMethod]
        public void DirectRecursiveRootStillTracksItsOwnReferenceAndReleasesIt()
        {
            var result = Generate(@"
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper { public static partial NodeDto Map(Node source); }
" + NodeTypes + @"
public static class Probe
{
    public static bool Run()
    {
        var node = new Node { Value = 7 };
        node.Next = node;
        try { Mapper.Map(node); return false; }
        catch (LiteMapperCycleException error)
        {
            if (error.MemberPath != ""Next"" || error.MappingMethod != ""Map"" ||
                error.SourceType != typeof(Node) || error.DestinationType != typeof(NodeDto)) return false;
        }
        node.Next = null;
        return Mapper.Map(node).Value == 7;
    }
}
");
            AssertRuns(result);
            AssertTrackingBoundary(result.Source, "Node");
        }

        [TestMethod]
        public void BridgeIntoMutuallyRecursiveHelpersPreservesTheFullPath()
        {
            var result = Generate(@"
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper { public static partial RootDto Map(Root source); }
public sealed class Root { public Bridge Envelope { get; set; } = new Bridge(); }
public sealed class RootDto { public BridgeDto Envelope { get; set; } = new BridgeDto(); }
public sealed class Bridge { public A Node { get; set; } = new A(); }
public sealed class BridgeDto { public ADto Node { get; set; } = new ADto(); }
public sealed class A { public B? Next { get; set; } }
public sealed class B { public A? Back { get; set; } }
public sealed class ADto { public BDto? Next { get; set; } }
public sealed class BDto { public ADto? Back { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var node = new A { Next = new B() };
        var source = new Root { Envelope = new Bridge { Node = node } };
        if (Mapper.Map(source).Envelope.Node.Next?.Back != null) return false;
        node.Next.Back = node;
        try { Mapper.Map(source); return false; }
        catch (LiteMapperCycleException error)
        {
            return error.MemberPath == ""Envelope.Node.Next.Back"" && error.MappingMethod == ""Map"" &&
                error.SourceType == typeof(A) && error.DestinationType == typeof(ADto);
        }
    }
}
");
            AssertRuns(result);
            AssertTrackingBoundary(result.Source, "A", "B");
        }

        [TestMethod]
        public void DeclaredRecursionKeepsMixedCycleVerticesSeparateFromAcyclicSideBridges()
        {
            var result = Generate(@"
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial ADto MapA(A source);
    public static partial BDto MapB(B source);
}
public sealed class A { public Link Child { get; set; } = new Link(); public Side? Side { get; set; } }
public sealed class B { public A? Back { get; set; } }
public sealed class Link { public B? Node { get; set; } }
public sealed class Side { public Node Node { get; set; } = new Node(); }
public sealed class ADto { public LinkDto Child { get; set; } = new LinkDto(); public SideDto? Side { get; set; } }
public sealed class BDto { public ADto? Back { get; set; } }
public sealed class LinkDto { public BDto? Node { get; set; } }
public sealed class SideDto { public NodeDto Node { get; set; } = new NodeDto(); }
" + NodeTypes + @"
public static class Probe
{
    public static bool Run()
    {
        var source = new A();
        source.Child.Node = new B { Back = source };
        try { Mapper.MapA(source); return false; }
        catch (LiteMapperCycleException error)
        {
            if (error.MappingMethod != ""MapA"" || error.MemberPath != ""Child.Node.Back"" || error.SourceType != typeof(A)) return false;
        }
        source.Child.Node = null;
        source.Side = new Side();
        source.Side.Node.Next = source.Side.Node;
        try { Mapper.MapA(source); return false; }
        catch (LiteMapperCycleException error)
        {
            return error.MappingMethod == ""MapA"" && error.MemberPath == ""Side.Node.Next"" && error.SourceType == typeof(Node);
        }
    }
}
");
            AssertRuns(result);
            var methods = CSharpSyntaxTree.ParseText(result.Source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var sideHelpers = methods.Where(method => method.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString() == "Side").ToArray();
            Assert.IsTrue(sideHelpers.Length > 0);
            Assert.IsTrue(sideHelpers.All(method => !method.ToString().Contains(".Enter(", StringComparison.Ordinal)),
                "A side bridge leading into an independent recursive component must only forward context.");
            var mixedHelpers = methods.Where(method => method.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString() == "Link").ToArray();
            Assert.IsTrue(mixedHelpers.Length > 0);
            Assert.IsTrue(mixedHelpers.All(method => method.ToString().Contains(".Enter(", StringComparison.Ordinal)),
                "The bridge inside a declared/helper recursive component must remain an active-path vertex.");
        }

        private const string NodeTypes = @"
public sealed class Node
{
    public int Value { get; set; }
    public Node? Next { get; set; }
    public override bool Equals(object? other) => other is Node;
    public override int GetHashCode() => 0;
}
public sealed class NodeDto { public int Value { get; set; } public NodeDto? Next { get; set; } }
";

        private static string ObjectFixture(int depth, string mode, string policy = "ThrowOnCycle", bool recursive = true)
        {
            var types = NodeTypes;
            var value = "node";
            var sourceType = "Node";
            var targetType = "NodeDto";
            var access = "";
            for (var index = 0; index < depth; index++)
            {
                var kind = mode == "struct" ? "struct" : "sealed class";
                var initializer = mode == "struct" ? "" : " = default!;";
                types += "public " + kind + " Bridge" + index + " { public " + sourceType + " Child { get; set; }" + initializer + " }\n";
                types += "public " + kind + " Bridge" + index + "Dto { public " + targetType + " Child { get; set; }" + initializer + " }\n";
                value = "new Bridge" + index + " { Child = " + value + " }";
                sourceType = "Bridge" + index;
                targetType = sourceType + "Dto";
                access += ".Child";
            }
            types += "public sealed class Root { public " + sourceType + " First { get; set; } = default!; public " + sourceType + " Second { get; set; } = default!; }\n";
            types += mode == "constructor"
                ? "public sealed class RootDto { public " + targetType + " First { get; } public " + targetType + " Second { get; } public RootDto(" + targetType + " first, " + targetType + " second) { First = first; Second = second; } }\n"
                : "public sealed class RootDto { public " + targetType + " First { get; set; } = default!; public " + targetType + " Second { get; set; } = default!; }\n";
            var method = mode == "update" ? "public static partial RootDto Map(Root source, RootDto destination);"
                : "public " + (mode == "instance" ? "" : "static ") + "partial RootDto Map(Root source);";
            var call = mode == "instance" ? "new Mapper(10).Map(source)" : mode == "update" ? "Mapper.Map(source, destination)" : "Mapper.Map(source)";
            var cycle = policy == "None" || !recursive ? "" : @"
        node.Next = node;
        try { CALL; return false; }
        catch (LiteMapperCycleException error)
        {
            if (error.MemberPath != ""PATH.Next"" || error.MappingMethod != ""Map"" ||
                error.SourceType != typeof(Node) || error.DestinationType != typeof(NodeDto)) return false;
        }
        var tail = new Node { Value = 11, Next = node };
        node.Next = tail;
        try { CALL; return false; }
        catch (LiteMapperCycleException error) { if (error.MemberPath != ""PATH.Next.Next"") return false; }
        node.Next = null;
        if (CALL.FirstACCESS.Next != null) return false;
";
            var fixture = @"
using System;
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.POLICY)]
public MAPPER_KIND partial class Mapper { METHOD }
TYPES
public static class Probe
{
    public static bool Run()
    {
        var node = new Node { Value = 7, Next = new Node { Value = 9 } };
        var bridge = VALUE;
        var source = new Root { First = bridge, Second = bridge };
        DESTINATION
        var mapped = CALL;
        if (mapped.FirstACCESS.Value != 7 || mapped.SecondACCESS.Next?.Value != 9 ||
            ReferenceEquals(mapped.FirstACCESS, mapped.SecondACCESS)) return false;
        CYCLE
        return true;
    }
}
".Replace("POLICY", policy).Replace("MAPPER_KIND", mode == "instance" ? "sealed" : "static")
                .Replace("METHOD", method + (mode == "instance" ? " private readonly int offset; public Mapper(int offset) { this.offset = offset; } [MappingConverter] private int Convert(int value) => offset + value;" : ""))
                .Replace("TYPES", types).Replace("VALUE", value)
                .Replace("DESTINATION", mode == "update" ? "var destination = new RootDto();" : "")
                .Replace("CYCLE", cycle).Replace("CALL", call).Replace("ACCESS", access)
                .Replace("PATH", (mode == "constructor" ? "first" : "First") + access);
            if (mode == "instance")
            {
                fixture = fixture.Replace(".Value != 7", ".Value != 17").Replace(".Next?.Value != 9", ".Next?.Value != 19");
            }
            return recursive ? fixture : fixture.Replace("public Node? Next { get; set; }", "public Leaf? Next { get; set; }")
                .Replace("public NodeDto? Next { get; set; }", "public LeafDto? Next { get; set; }")
                .Replace("Next = new Node { Value = 9 }", "Next = new Leaf { Value = 9 }")
                + "public sealed class Leaf { public int Value { get; set; } } public sealed class LeafDto { public int Value { get; set; } }";
        }

        private static void AssertTrackingBoundary(string generated, params string[] recursiveSources)
        {
            var root = CSharpSyntaxTree.ParseText(generated).GetRoot();
            var trackerCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Count(expression => expression.Type.ToString() == "__LiteMapperCycleTracker");
            Assert.AreEqual(1, trackerCreations, "Exactly one tracker must be created by the public recursive call.");
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method => method.Body != null))
            {
                var enters = method.Body!.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Enter").ToArray();
                var sourceType = method.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString();
                Assert.AreEqual(recursiveSources.Contains(sourceType) ? 1 : 0, enters.Length,
                    "Only recursive source vertices participate in active-path tracking: " + method.Identifier + "\n" + method);
                if (enters.Length != 0)
                {
                    Assert.IsTrue(method.Body.DescendantNodes().OfType<FinallyClauseSyntax>().Any(), "Recursive helpers must release their source reference in finally.");
                }
            }
        }

        private static void AssertRuns(GeneratorRun result)
        {
            var assembly = Compile(result);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null), result.Source);
        }

        private static Assembly Compile(GeneratorRun result)
        {
            var errors = result.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
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
            var compilation = CSharpCompilation.Create("CycleBridgeContextTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            var result = driver.GetRunResult();
            Assert.AreEqual(0, result.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Warning), string.Join(Environment.NewLine, result.Diagnostics));
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return new GeneratorRun(output, result.GeneratedTrees.Single().GetText().ToString(), result.Diagnostics.ToArray());
        }

        private sealed class GeneratorRun
        {
            public GeneratorRun(Compilation compilation, string source, Diagnostic[] diagnostics)
            { Compilation = compilation; Source = source; Diagnostics = diagnostics; }
            public Compilation Compilation { get; }
            public string Source { get; }
            public Diagnostic[] Diagnostics { get; }
        }
    }
}
