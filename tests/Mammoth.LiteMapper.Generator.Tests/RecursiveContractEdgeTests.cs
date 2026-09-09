using System;
using System.Reflection;
using Mammoth.LiteMapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class RecursiveContractEdgeTests : Milestone11EnumMappingTests
    {
        [TestMethod]
        public void ThrowOnCycleMapsFiniteMixedClassAndStructGraphsWithoutBoxingTheStruct()
        {
            // Section 17.4 permits value types on recursive paths: only the Node reference is tracked.
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial NodeDto Map(Node source);
}

public sealed class Node { public Link Link { get; set; } }
public struct Link { public Node? Next { get; set; } }
public sealed class NodeDto { public LinkDto Link { get; set; } }
public struct LinkDto { public NodeDto? Next { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            AssertNoRuntimeFeatures(generated);
            Assert.IsFalse(generated.Contains("__tracker.Enter(source!, typeof(Link)", StringComparison.Ordinal), generated);

            var assembly = Emit(result.Compilation);
            dynamic source = Activator.CreateInstance(assembly.GetType("Node")!)!;
            dynamic mapped = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { source })!;
            Assert.IsNull(mapped.Link.Next);
        }

        [TestMethod]
        public void ThrowOnCycleUsesReferenceIdentityAndReusesTheTrackerAfterSeparateBranches()
        {
            // Section 17.3 requires distinct references that compare equal to map independently after Exit.
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial PairDto Map(Pair source);
}

public sealed class Pair { public Node? Left { get; set; } public Node? Right { get; set; } }
public sealed class PairDto { public NodeDto? Left { get; set; } public NodeDto? Right { get; set; } }
public sealed class Node
{
    public int Value { get; set; }
    public Node? Next { get; set; }
    public override bool Equals(object? other) => other is Node;
    public override int GetHashCode() => 0;
}
public sealed class NodeDto { public int Value { get; set; } public NodeDto? Next { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var nodeType = assembly.GetType("Node")!;
            dynamic left = Activator.CreateInstance(nodeType)!;
            dynamic right = Activator.CreateInstance(nodeType)!;
            left.Value = 11;
            right.Value = 22;
            dynamic pair = Activator.CreateInstance(assembly.GetType("Pair")!)!;
            pair.Left = left;
            pair.Right = right;

            dynamic mapped = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { pair })!;
            Assert.AreEqual(11, mapped.Left.Value);
            Assert.AreEqual(22, mapped.Right.Value);
            Assert.AreNotSame(mapped.Left, mapped.Right);
        }

        [TestMethod]
        public void ThrowOnCycleReportsTheIndirectCyclePathAndAllExceptionDetails()
        {
            // Sections 17.2, 17.3, and 17.5 require one forwarded tracker and the failing nested path.
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial ADto Map(A source);
}

public sealed class A { public B? B { get; set; } }
public sealed class B { public C? C { get; set; } }
public sealed class C { public A? A { get; set; } }
public sealed class ADto { public BDto? B { get; set; } }
public sealed class BDto { public CDto? C { get; set; } }
public sealed class CDto { public ADto? A { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "__LiteMapperCycleTracker");

            var assembly = Emit(result.Compilation);
            dynamic a = Activator.CreateInstance(assembly.GetType("A")!)!;
            dynamic b = Activator.CreateInstance(assembly.GetType("B")!)!;
            dynamic c = Activator.CreateInstance(assembly.GetType("C")!)!;
            a.B = b;
            b.C = c;
            c.A = a;

            var exception = Assert.ThrowsExactly<TargetInvocationException>(
                () => assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { a }));
            var cycle = (LiteMapperCycleException)exception.InnerException!;
            Assert.AreEqual(assembly.GetType("A"), cycle.SourceType);
            Assert.AreEqual(assembly.GetType("ADto"), cycle.DestinationType);
            Assert.AreEqual("Map", cycle.MappingMethod);
            Assert.AreEqual("B.C.A", cycle.MemberPath);
            StringAssert.Contains(cycle.Message, cycle.SourceType.ToString());
            StringAssert.Contains(cycle.Message, cycle.DestinationType.ToString());
            StringAssert.Contains(cycle.Message, cycle.MappingMethod);
            StringAssert.Contains(cycle.Message, cycle.MemberPath);
        }

        [TestMethod]
        public void DeclaredRecursiveMappingComponentGeneratesSharedTrackingState()
        {
            // Sections 14.1 and 17.2 require declared mappings in a recursive component to share one tracker.
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial ADto MapA(A source);
    public static partial BDto MapB(B source);
}

public sealed class A { public B? B { get; set; } }
public sealed class B { public A? A { get; set; } }
public sealed class ADto { public BDto? B { get; set; } }
public sealed class BDto { public ADto? A { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "__LiteMapperCycleTracker",
                "Mutually recursive declared mappings must not call each other without shared cycle state.");
            StringAssert.Contains(generated, "__tracker",
                "The same tracker must be forwarded across the declared mapping component.");

            var assembly = Emit(result.Compilation);
            dynamic a = Activator.CreateInstance(assembly.GetType("A")!)!;
            dynamic b = Activator.CreateInstance(assembly.GetType("B")!)!;
            a.B = b;
            b.A = a;
            var exception = Assert.ThrowsExactly<TargetInvocationException>(
                () => assembly.GetType("Mapper")!.GetMethod("MapA", new[] { assembly.GetType("A")! })!.Invoke(null, new[] { a }));
            var cycle = (LiteMapperCycleException)exception.InnerException!;
            Assert.AreEqual("MapA", cycle.MappingMethod);
            Assert.AreEqual("B.A", cycle.MemberPath);
            Assert.AreEqual(assembly.GetType("A"), cycle.SourceType);
            Assert.AreEqual(assembly.GetType("ADto"), cycle.DestinationType);
        }

        [TestMethod]
        public void RecursiveTrackingUsesDeclaredSourceParameterNames()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial ADto MapA(A left);
    public static partial BDto MapB(B right);
}
public sealed class A { public B? B { get; set; } }
public sealed class B { public A? A { get; set; } }
public sealed class ADto { public BDto? B { get; set; } }
public sealed class BDto { public ADto? A { get; set; } }
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            Emit(result.Compilation);
        }
    }
}
