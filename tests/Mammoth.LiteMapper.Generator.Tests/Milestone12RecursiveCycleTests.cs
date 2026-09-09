using System;
using System.Linq;
using System.Reflection;
using Mammoth.LiteMapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class Milestone12RecursiveCycleTests : Milestone11EnumMappingTests
    {
        [TestMethod]
        public void FiniteRecursiveTreeMapsWithoutTrackerWhenReferenceHandlingIsNone()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial NodeDto ToDto(Node source);
}

public sealed class Node { public string? Name { get; set; } public Node? Child { get; set; } }
public sealed class NodeDto { public string? Name { get; set; } public NodeDto? Child { get; set; } }
");

            AssertDiagnostic(result.RunResult, "LITEMAPPER6001");
            var generated = SingleGeneratedSource(result.RunResult);
            Assert.IsFalse(generated.Contains("__LiteMapperCycleTracker", StringComparison.Ordinal), generated);
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var nodeType = assembly.GetType("Node")!;
            dynamic root = Activator.CreateInstance(nodeType)!;
            dynamic child = Activator.CreateInstance(nodeType)!;
            root.Name = "root";
            child.Name = "child";
            root.Child = child;

            dynamic mapped = assembly.GetType("Mapper")!.GetMethod("ToDto")!.Invoke(null, new[] { root })!;
            Assert.AreEqual("root", mapped.Name);
            Assert.AreEqual("child", mapped.Child.Name);
        }

        [TestMethod]
        public void ThrowOnCycleReportsDirectCycleWithMethodAndMemberPath()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial NodeDto ToDto(Node source);
}

public sealed class Node { public string? Name { get; set; } public Node? Child { get; set; } }
public sealed class NodeDto { public string? Name { get; set; } public NodeDto? Child { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "__LiteMapperCycleTracker");
            StringAssert.Matches(generated, new System.Text.RegularExpressions.Regex(
                "source\\.Child is \\{ \\} (__sourcePath_Child_[0-9A-F]{8}) \\? MapNested_Node_To_NodeDto_[0-9A-F]{8}\\(\\1, __tracker, \\\"Child\\\"\\)"));
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var nodeType = assembly.GetType("Node")!;
            dynamic root = Activator.CreateInstance(nodeType)!;
            root.Name = "root";
            root.Child = root;

            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("ToDto")!.Invoke(null, new[] { root }));
            var cycle = (LiteMapperCycleException)exception.InnerException!;
            Assert.AreEqual(nodeType, cycle.SourceType);
            Assert.AreEqual(assembly.GetType("NodeDto"), cycle.DestinationType);
            Assert.AreEqual("ToDto", cycle.MappingMethod);
            Assert.AreEqual("Child", cycle.MemberPath);
        }

        [TestMethod]
        public void ThrowOnCycleAllowsSharedNonCyclicReferencesAndConcurrentCalls()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial PairDto ToDto(Pair source);
}

public sealed class Pair { public Node? Left { get; set; } public Node? Right { get; set; } }
public sealed class PairDto { public NodeDto? Left { get; set; } public NodeDto? Right { get; set; } }
public sealed class Node { public string? Name { get; set; } public Node? Child { get; set; } }
public sealed class NodeDto { public string? Name { get; set; } public NodeDto? Child { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var pairType = assembly.GetType("Pair")!;
            var nodeType = assembly.GetType("Node")!;
            var mapper = assembly.GetType("Mapper")!.GetMethod("ToDto")!;
            dynamic shared = Activator.CreateInstance(nodeType)!;
            shared.Name = "shared";
            dynamic pair = Activator.CreateInstance(pairType)!;
            pair.Left = shared;
            pair.Right = shared;

            var mapped = Enumerable.Range(0, 8).AsParallel().Select(_ => mapper.Invoke(null, new[] { pair })!).ToArray();
            foreach (dynamic item in mapped!)
            {
                Assert.AreEqual("shared", item.Left.Name);
                Assert.AreEqual("shared", item.Right.Name);
                Assert.AreNotSame(item.Left, item.Right);
            }
        }

        [TestMethod]
        public void ThrowOnCycleDetectsCyclesThroughCollections()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial NodeDto ToDto(Node source);
}

public sealed class Node { public List<Node> Children { get; set; } = new List<Node>(); }
public sealed class NodeDto { public List<NodeDto> Children { get; set; } = new List<NodeDto>(); }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var nodeType = assembly.GetType("Node")!;
            dynamic root = Activator.CreateInstance(nodeType)!;
            root.Children.Add(root);

            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("ToDto")!.Invoke(null, new[] { root }));
            var cycle = (LiteMapperCycleException)exception.InnerException!;
            Assert.AreEqual("Children", cycle.MemberPath);
        }

        [TestMethod]
        public void ThrowOnCycleDoesNotAllocateTrackerForAcyclicTypeGraphs()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial ParentDto ToDto(Parent source);
}

public sealed class Parent { public Child? Child { get; set; } }
public sealed class ParentDto { public ChildDto? Child { get; set; } }
public sealed class Child { public string? Name { get; set; } }
public sealed class ChildDto { public string? Name { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            Assert.IsFalse(generated.Contains("__LiteMapperCycleTracker", StringComparison.Ordinal), generated);
        }

        [TestMethod]
        public void InvalidReferenceHandlingAndUntrackableCyclePathsReportDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = (ReferenceHandling)42)] public static partial class Mapper { public static partial NodeDto ToDto(Node source); }
public sealed class Node { public Node? Child { get; set; } }
public sealed class NodeDto { public NodeDto? Child { get; set; } }
").RunResult, "LITEMAPPER0008");

        }
    }
}
