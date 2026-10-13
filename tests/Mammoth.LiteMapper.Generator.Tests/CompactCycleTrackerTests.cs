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
    public sealed class CompactCycleTrackerTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FirstReferenceAndDuplicateNeedNoSpillEvenWhenFirstIsNull(bool nullSource)
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            var first = nullSource ? null : new object();
            state.Enter(first);
            state.AssertFirst(first);
            Assert.IsNull(state.Spill, "One active reference needs no hash set.");
            state.AssertDuplicate(first);
            state.AssertFirst(first);
            Assert.IsNull(state.Spill, "Failed Enter occurs outside try and must not create spill storage.");
            state.Exit(first);
            state.AssertEmpty();
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        public void SpillContainsEveryActiveReferenceAndReusesStorageAcrossSiblingsAndTraversals(bool nullFirst, bool nullSecond)
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            var first = nullFirst ? null : new object();
            var second = nullSecond ? null : new object();
            state.Enter(first);
            state.Enter(second);
            var spill = state.Spill!;
            Assert.AreEqual(2, spill.Count, "Promotion retains both the first and second active references.");
            Assert.IsTrue(spill.Contains(first!));
            Assert.IsTrue(spill.Contains(second!));
            state.AssertDuplicate(first);
            state.AssertDuplicate(second);
            state.AssertFirst(first);
            Assert.AreSame(spill, state.Spill);
            Assert.AreEqual(2, spill.Count, "Failed duplicate Enter must preserve active state.");
            Assert.IsTrue(spill.Contains(second!));
            state.Exit(second);
            Assert.AreEqual(1, spill.Count);
            state.AssertFirst(first);
            var sibling = new object();
            state.Enter(sibling);
            Assert.AreSame(spill, state.Spill, "Sibling traversal must reuse emptied spill storage.");
            Assert.AreEqual(2, spill.Count);
            state.Exit(sibling);
            state.Exit(first);
            state.AssertEmpty();
            state.Enter(second);
            state.AssertFirst(second);
            Assert.AreSame(spill, state.Spill);
            Assert.AreEqual(1, spill.Count, "Once promoted, every new traversal reuses the retained spill storage without demotion.");
            state.Exit(second);
            state.AssertEmpty();
        }

        [TestMethod]
        public void UserEqualityAndHashOverridesNeverParticipateInInlineOrSpillTracking()
        {
            var source = ChainFixture(true).Replace("public static Node Chain(int depth)", @"
    public override bool Equals(object? other) => throw new InvalidOperationException(""Equals must not run"");
    public override int GetHashCode() => throw new InvalidOperationException(""GetHashCode must not run"");
    public static Node Chain(int depth)");
            var assembly = Compile(Generate(source));
            var state = new TrackerState(assembly);
            var first = Activator.CreateInstance(assembly.GetType("Node")!)!;
            var second = Activator.CreateInstance(assembly.GetType("Node")!)!;
            state.Enter(first);
            state.Enter(second);
            state.AssertDuplicate(first);
            state.AssertDuplicate(second);
            state.Exit(second);
            state.Exit(first);
            state.AssertEmpty();
            var chain = assembly.GetType("Node")!.GetMethod("Chain")!.Invoke(null, new object[] { 16 });
            Assert.IsNotNull(assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { chain }));
        }

        [TestMethod]
        [DataRow(1, false)]
        [DataRow(4, false)]
        [DataRow(1, true)]
        [DataRow(4, true)]
        public void ShallowAndDeepGetterOrConverterFailuresReleaseAllEnteredReferences(int depth, bool converter)
        {
            var source = ChainFixture(true).Replace("    public int Value { get; set; }", @"
    public static readonly InvalidOperationException Failure = new InvalidOperationException(""mapping failed"");
    public bool ThrowGetter;
    private int _value;
    public int Value { get => ThrowGetter ? throw Failure : _value; set => _value = value; }");
            if (converter) source = source.Replace("public static partial NodeDto Map(Node source);", @"
public static partial NodeDto Map(Node source);
[MappingConverter] private static int Convert(int value) => value < 0 ? throw Node.Failure : value;");
            var assembly = Compile(Generate(source));
            var state = new TrackerState(assembly);
            var helper = assembly.GetType("Mapper")!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name.StartsWith("MapNested_", StringComparison.Ordinal));
            var node = assembly.GetType("Node")!;
            var root = node.GetMethod("Chain")!.Invoke(null, new object[] { depth })!;
            var tail = root;
            while (node.GetProperty("Child")!.GetValue(tail) is object child) tail = child;
            if (converter) node.GetProperty("Value")!.SetValue(tail, -1);
            else node.GetField("ThrowGetter")!.SetValue(tail, true);
            var failure = Assert.ThrowsExactly<TargetInvocationException>(() => helper.Invoke(null, new[] { root, state.Tracker, state.EmptyPath, "Map" }));
            Assert.AreSame(node.GetField("Failure")!.GetValue(null), failure.InnerException);
            state.AssertEmpty();
            var nullFailure = Assert.ThrowsExactly<TargetInvocationException>(() => helper.Invoke(null, new[] { null, state.Tracker, state.EmptyPath, "Map" }));
            Assert.IsInstanceOfType<NullReferenceException>(nullFailure.InnerException, "GuardNonNullSource defaults to false; source! is not a runtime guard.");
            state.AssertEmpty();
            if (converter) node.GetProperty("Value")!.SetValue(tail, 0);
            else node.GetField("ThrowGetter")!.SetValue(tail, false);
            Assert.IsNotNull(helper.Invoke(null, new[] { root, state.Tracker, state.EmptyPath, "Map" }));
            state.AssertEmpty();
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(4)]
        public void ReentrantPublicCallsOnTheSameSourceOwnIndependentTrackers(int depth)
        {
            var source = ChainFixture(true).Replace("public static partial NodeDto Map(Node source);", @"
public static partial NodeDto Map(Node source);
public static Node Root = null!;
private static bool _inside;
[MappingConverter] private static int Convert(int value)
{
    if (!_inside)
    {
        _inside = true;
        try { if (Map(Root).Value != 0) throw new InvalidOperationException(""reentrant result""); }
        finally { _inside = false; }
    }
    return value;
}");
            var assembly = Compile(Generate(source));
            var root = assembly.GetType("Node")!.GetMethod("Chain")!.Invoke(null, new object[] { depth });
            var mapper = assembly.GetType("Mapper")!;
            mapper.GetField("Root")!.SetValue(null, root);
            Assert.IsNotNull(mapper.GetMethod("Map")!.Invoke(null, new[] { root }));
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(4)]
        public void ClassStructClassCyclesTrackReferencesWithoutBoxingAndKeepFullPaths(int depth)
        {
            var result = Generate(@"
using System;
using System.Linq;
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper { public static partial NodeDto Map(Node source); }
public sealed class Node { public Link Link { get; set; } }
public struct Link { public Node? Next { get; set; } }
public sealed class NodeDto { public LinkDto Link { get; set; } }
public struct LinkDto { public NodeDto? Next { get; set; } }
public static class Probe
{
    public static bool Run(int depth)
    {
        var root = new Node(); var tail = root;
        for (var i = 1; i < depth; i++) { var next = new Node(); tail.Link = new Link { Next = next }; tail = next; }
        if (Mapper.Map(root) == null) return false;
        tail.Link = new Link { Next = root };
        try { Mapper.Map(root); return false; }
        catch (LiteMapperCycleException error)
        {
            if (error.SourceType != typeof(Node) || error.DestinationType != typeof(NodeDto) ||
                error.MappingMethod != ""Map"" || error.MemberPath != string.Join(""."", Enumerable.Repeat(""Link.Next"", depth))) return false;
        }
        tail.Link = default;
        return Mapper.Map(root) != null;
    }
}
");
            Assert.AreEqual(true, Compile(result).GetType("Probe")!.GetMethod("Run")!.Invoke(null, new object[] { depth }));
            var methods = CSharpSyntaxTree.ParseText(result.Source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods.Where(method => method.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString() == "Link"))
                Assert.IsFalse(method.ToString().Contains(".Enter(", StringComparison.Ordinal), "Struct bridges must forward the tracker without boxing.");
            foreach (var method in methods.Where(method => method.Body?.ToString().Contains(".Enter(", StringComparison.Ordinal) == true))
            {
                var body = method.Body!;
                var attempt = body.Statements.OfType<TryStatementSyntax>().Single();
                Assert.IsTrue(body.Statements.TakeWhile(statement => statement != attempt).Any(statement => statement.ToString().Contains(".Enter(", StringComparison.Ordinal)),
                    "Enter must run before try so a failed duplicate cannot trigger Exit.");
                Assert.IsNotNull(attempt.Finally);
                StringAssert.Contains(attempt.Finally.ToString(), ".Exit(");
            }
        }

        [TestMethod]
        public void TrackerSelfSourceEscapesTheEmptySentinelWithoutLosingOccupancy()
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            state.Enter(state.Tracker);
            var spill = state.Spill!;
            Assert.IsNotNull(spill, "The reserved empty sentinel must escape into tagged spill storage as an ordinary element.");
            Assert.AreEqual(1, spill.Count);
            Assert.IsTrue(spill.Contains(state.Tracker));
            state.AssertDuplicate(state.Tracker);
            state.Exit(state.Tracker);
            state.AssertEmpty();
            Assert.AreSame(spill, state.Spill);
            state.Enter(null);
            Assert.AreEqual(1, spill.Count);
            Assert.IsTrue(spill.Contains(null!));
            state.AssertDuplicate(null);
            state.Exit(null);
            state.AssertEmpty();
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void NullAndTrackerSelfRemainDistinctSimultaneouslyActiveSources(bool selfFirst)
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            var first = selfFirst ? state.Tracker : null;
            var second = selfFirst ? null : state.Tracker;
            state.Enter(first);
            state.AssertFirst(first);
            var initialSpill = state.Spill;
            state.Enter(second);
            var spill = state.Spill!;
            Assert.IsNotNull(spill);
            if (selfFirst) Assert.AreSame(initialSpill, spill, "The second source must reuse the self-escape spill.");
            else Assert.IsNull(initialSpill, "A null first source stays raw until promotion.");
            Assert.AreEqual(2, spill.Count, "Null and the reserved self reference must occupy distinct active entries.");
            Assert.IsTrue(spill.Contains(null!));
            Assert.IsTrue(spill.Contains(state.Tracker));
            state.AssertDuplicate(null);
            state.AssertDuplicate(state.Tracker);
            state.Exit(second);
            Assert.AreEqual(1, spill.Count);
            Assert.IsFalse(spill.Contains(second!));
            Assert.IsTrue(spill.Contains(first!));
            Assert.AreSame(spill, state.Spill, "Exiting one reserved source must preserve the other and retain tagged storage.");
            state.Exit(first);
            state.AssertEmpty();
            Assert.AreSame(spill, state.Spill);
        }

        [TestMethod]
        public void PrivateSpillSourceEscapesItsReservedStateTagAsAnOrdinaryReference()
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            var reserved = (System.Collections.Generic.HashSet<object>)Activator.CreateInstance(state.SpillType, nonPublic: true)!;
            state.Enter(reserved);
            var spill = state.Spill!;
            Assert.IsNotNull(spill);
            Assert.AreNotSame(reserved, spill, "The source must not become active storage merely because it has the private spill type.");
            Assert.AreEqual(0, reserved.Count, "Tracking must not mutate a reserved-tag source collection.");
            Assert.AreEqual(1, spill.Count);
            Assert.IsTrue(spill.Contains(reserved));
            state.AssertDuplicate(reserved);
            state.Exit(reserved);
            state.AssertEmpty();
            Assert.AreSame(spill, state.Spill);
        }

        [TestMethod]
        public void CurrentPrivateSpillStorageCanItselfBeAnActiveSource()
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            var first = new object(); var second = new object();
            state.Enter(first);
            state.Enter(second);
            var spill = state.Spill!;
            state.Enter(spill);
            Assert.AreSame(spill, state.Spill, "An already promoted tracker must track its storage reference as an ordinary element.");
            Assert.AreEqual(3, spill.Count);
            Assert.IsTrue(spill.Contains(spill));
            state.AssertDuplicate(spill);
            state.Exit(spill);
            Assert.AreEqual(2, spill.Count);
            state.Exit(second);
            state.Exit(first);
            state.AssertEmpty();
            Assert.AreSame(spill, state.Spill);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UserHashSetsAndThrowingUserSubclassesRemainRawSourcesUntilPromotion(bool subclass)
        {
            var state = new TrackerState(Compile(Generate(ChainFixture(true))));
            System.Collections.Generic.HashSet<object> source = subclass ? new ThrowingSet() : new System.Collections.Generic.HashSet<object>();
            state.Enter(source);
            Assert.AreSame(source, state.State, "Only the private sealed spill subtype is tagged; user sets are ordinary raw references.");
            Assert.IsNull(state.Spill);
            state.AssertDuplicate(source);
            var second = new object();
            state.Enter(second);
            var spill = state.Spill!;
            Assert.AreEqual(2, spill.Count);
            Assert.IsTrue(spill.Contains(source));
            Assert.IsTrue(spill.Contains(second));
            Assert.AreEqual(0, source.Count, "Tracking must not mutate a user HashSet.");
            state.AssertDuplicate(source);
            state.Exit(second);
            state.Exit(source);
            state.AssertEmpty();
        }

        private sealed class ThrowingSet : System.Collections.Generic.HashSet<object>
        {
            public override bool Equals(object? other) => throw new InvalidOperationException("User set equality must not run.");
            public override int GetHashCode() => throw new InvalidOperationException("User set hashing must not run.");
        }

        [TestMethod]
        public void CompactLayoutAndGeneratedTraversalAllocationsMatchOriginalOncePromoted()
        {
            var result = Generate(ChainFixture(true) + @"
public static class Probe
{
    public static long[] Measure()
    {
        var depths = new[] { 1, 2, 16, 128 };
        var values = new long[depths.Length];
        for (var d = 0; d < depths.Length; d++)
        {
            var root = Node.Chain(depths[d]);
            for (var warmup = 0; warmup < 80; warmup++) GC.KeepAlive(Mapper.Map(root));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++) GC.KeepAlive(Mapper.Map(root));
            values[d] = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
        }
        return values;
    }
}
");
            var wide = Generate(ChainFixture(true).Replace("NodeDto Map(Node source)", "NodeDto[] Map(Node[] source)") + @"
public static class Probe
{
    public static long Measure()
    {
        var shared = Node.Chain(2);
        var roots = Enumerable.Repeat(shared, 256).ToArray();
        for (var warmup = 0; warmup < 80; warmup++) GC.KeepAlive(Mapper.Map(roots));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) GC.KeepAlive(Mapper.Map(roots));
        return (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
    }
}
");
            var originalBytes = (long[])Compile(OriginalStyleTracker(result)).GetType("Probe")!.GetMethod("Measure")!.Invoke(null, null)!;
            var compactBytes = (long[])Compile(result).GetType("Probe")!.GetMethod("Measure")!.Invoke(null, null)!;
            var originalWide = (long)Compile(OriginalStyleTracker(wide)).GetType("Probe")!.GetMethod("Measure")!.Invoke(null, null)!;
            var compactWide = (long)Compile(wide).GetType("Probe")!.GetMethod("Measure")!.Invoke(null, null)!;
            Console.WriteLine("Original-style bytes/op at 1/2/16/128/wide: " + string.Join(", ", originalBytes) + ", " + originalWide);
            Console.WriteLine("Compact bytes/op at 1/2/16/128/wide: " + string.Join(", ", compactBytes) + ", " + compactWide);
            Assert.IsTrue(compactBytes[0] < originalBytes[0], "One ordinary active reference must avoid all spill allocations.");
            for (var d = 1; d < compactBytes.Length; d++) Assert.AreEqual(originalBytes[d], compactBytes[d],
                "Promotion must match the original active-reference allocation.");
            Assert.AreEqual(originalWide, compactWide, "Wide shared siblings must reuse spill storage without allocating a tracker or set per sibling.");

            // Instrument only the test compilation so the allocation loop can construct the private spill directly.
            var layoutTree = result.Compilation.SyntaxTrees.Single(tree => tree.GetText().ToString() == result.Source);
            var layoutRoot = layoutTree.GetRoot();
            var tracker = layoutRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "__LiteMapperCycleTracker");
            var instrumentedTracker = tracker.AddMembers(SyntaxFactory.ParseMemberDeclaration("public static object CreateSpillForLayout() => new __ActiveReferences();")!);
            var options = (CSharpParseOptions)layoutTree.Options;
            var instrumentedTree = CSharpSyntaxTree.Create((CSharpSyntaxNode)layoutRoot.ReplaceNode(tracker, instrumentedTracker), options);
            var layout = result.Compilation.ReplaceSyntaxTree(layoutTree, instrumentedTree).AddSyntaxTrees(CSharpSyntaxTree.ParseText(@"
using System;
public static partial class Mapper
{
    public static long[] LayoutBytes()
    {
        Func<object>[] factories = {
            () => new __LiteMapperCycleTracker(),
            () => __LiteMapperCycleTracker.CreateSpillForLayout(),
            () => new System.Collections.Generic.HashSet<object>(__ReferenceIdentityComparer.Instance)
        };
        var values = new long[factories.Length * 2];
        for (var d = 0; d < factories.Length; d++)
        {
            for (var warmup = 0; warmup < 80; warmup++) GC.KeepAlive(factories[d]());
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) GC.KeepAlive(factories[d]());
            values[d + factories.Length] = GC.GetAllocatedBytesForCurrentThread() - before;
            values[d] = values[d + factories.Length] / 1000;
        }
        return values;
    }
}
", options));
            var sizes = (long[])Compile(new GeneratorRun(layout, result.Source)).GetType("Mapper")!.GetMethod("LayoutBytes")!.Invoke(null, null)!;
            Console.WriteLine("Tracker/private spill/base HashSet bytes per object: " + string.Join(", ", sizes.Take(3)));
            Console.WriteLine("Raw constructor totals for 1000 objects each: " + string.Join(", ", sizes.Skip(3)));
            for (var d = 3; d < sizes.Length; d++) Assert.AreEqual(0L, sizes[d] % 1000, "Direct allocation measurements must be integral without hiding counter noise.");
            Assert.AreEqual(3L * IntPtr.Size, sizes[0], "One object field must have the expected compact CLR allocation size.");
            Assert.AreEqual(sizes[2], sizes[1], "The sealed fieldless spill subtype must not grow HashSet allocation size.");
        }

        private static GeneratorRun OriginalStyleTracker(GeneratorRun result)
        {
            var tree = result.Compilation.SyntaxTrees.Single(candidate => candidate.GetText().ToString() == result.Source);
            var root = tree.GetRoot();
            var tracker = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "__LiteMapperCycleTracker");
            var baseline = CSharpSyntaxTree.ParseText(@"
private sealed class __LiteMapperCycleTracker
{
    private readonly global::System.Collections.Generic.HashSet<object> _active = new global::System.Collections.Generic.HashSet<object>(__ReferenceIdentityComparer.Instance);
    public void Enter(object source, global::System.Type sourceType, global::System.Type destinationType, string mappingMethod, Path memberPath)
    {
        if (!_active.Add(source))
        {
            throw new global::Mammoth.LiteMapper.LiteMapperCycleException(sourceType, destinationType, mappingMethod, memberPath.ToString());
        }
    }
    public void Exit(object source) { _active.Remove(source); }
}
").GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            baseline = baseline.WithMembers(baseline.Members.AddRange(tracker.Members.OfType<StructDeclarationSyntax>()));
            var replaced = CSharpSyntaxTree.Create((CSharpSyntaxNode)root.ReplaceNode(tracker, baseline), (CSharpParseOptions)tree.Options);
            return new GeneratorRun(result.Compilation.ReplaceSyntaxTree(tree, replaced), replaced.GetText().ToString());
        }

        private sealed class TrackerState
        {
            public TrackerState(Assembly assembly)
            {
                var type = assembly.GetType("Mapper")!.GetNestedType("__LiteMapperCycleTracker", BindingFlags.NonPublic)!;
                Tracker = Activator.CreateInstance(type)!;
                _state = type.GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Assert.IsNotNull(_state, "One object field must represent empty, raw-first, and spilled states.");
                Assert.AreEqual(typeof(object), _state.FieldType);
                Assert.AreEqual(1, type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length,
                    "Compact tracker must not grow beyond one instance field.");
                SpillType = type.GetNestedTypes(BindingFlags.NonPublic).Single(nested => nested.BaseType == typeof(System.Collections.Generic.HashSet<object>));
                Assert.IsTrue(SpillType.IsSealed, "Only the exact private spill subtype is a state tag.");
                Assert.AreEqual(0, SpillType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length,
                    "The spill tag must not add instance allocation overhead.");
                Assert.AreEqual(0, SpillType.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length);
                Assert.IsFalse(SpillType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(method => method.Name == "Finalize"));
                Assert.AreSame(Tracker, State, "The tracker itself is the empty sentinel.");
                _enter = type.GetMethod("Enter")!;
                _exit = type.GetMethod("Exit")!;
                var path = _enter.GetParameters()[4].ParameterType;
                EmptyPath = Activator.CreateInstance(path)!;
                _path = Activator.CreateInstance(path, new object[] { "Parent.Child" })!;
            }
            public object Tracker { get; }
            public object EmptyPath { get; }
            public Type SpillType { get; }
            public object? State => _state.GetValue(Tracker);
            public System.Collections.Generic.HashSet<object>? Spill => State?.GetType() == SpillType ? (System.Collections.Generic.HashSet<object>)State : null;
            private readonly FieldInfo _state;
            private readonly MethodInfo _enter, _exit;
            private readonly object _path;
            public void Enter(object? source) => _enter.Invoke(Tracker, new[] { source, typeof(object), typeof(string), "Entry", _path });
            public void Exit(object? source) => _exit.Invoke(Tracker, new[] { source });
            public void AssertFirst(object? source)
            {
                if (Spill is System.Collections.Generic.HashSet<object> spill) Assert.IsTrue(spill.Contains(source!));
                else Assert.AreSame(source, State);
            }
            public void AssertDuplicate(object? source)
            {
                var before = State;
                var count = Spill?.Count;
                var failure = Assert.ThrowsExactly<TargetInvocationException>(() => Enter(source));
                Assert.IsInstanceOfType<LiteMapperCycleException>(failure.InnerException);
                var cycle = (LiteMapperCycleException)failure.InnerException!;
                Assert.AreEqual(typeof(object), cycle.SourceType);
                Assert.AreEqual(typeof(string), cycle.DestinationType);
                Assert.AreEqual("Entry", cycle.MappingMethod);
                Assert.AreEqual("Parent.Child", cycle.MemberPath);
                Assert.AreSame(before, State, "Failed Enter cannot replace raw state or spill storage.");
                Assert.AreEqual(count, Spill?.Count, "Failed Enter cannot mutate the caller's active references.");
            }
            public void AssertEmpty()
            {
                if (Spill is System.Collections.Generic.HashSet<object> spill) Assert.AreEqual(0, spill.Count, "Every promoted reference must unwind.");
                else Assert.AreSame(Tracker, State, "Raw-reference exit restores the self sentinel, including after a null source.");
            }
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
            var compilation = CSharpCompilation.Create("CompactCycleTrackerTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
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
