using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class HelperSemanticIsolationTests
    {
        [TestMethod]
        [DataRow(false, false, false)]
        [DataRow(false, false, true)]
        [DataRow(false, true, false)]
        [DataRow(false, true, true)]
        [DataRow(true, false, false)]
        [DataRow(true, false, true)]
        [DataRow(true, true, false)]
        [DataRow(true, true, true)]
        public void NumericHelpersRespectEachEntryPolicy(bool collection, bool reverseNames, bool reverseDeclarations)
        {
            var source = @"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
__MODELS__
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        if (__READ_CHECKED__ != 7 || __READ_UNCHECKED__ != 7) return false;
        __SET_OVERFLOW__
        if (__READ_UNCHECKED__ != 44) return false;
        try { Mapper.__FIRST__(source); return false; }
        catch (OverflowException) { return true; }
    }
}
";
            source = source.Replace("__MODELS__", collection
                ? @"public sealed class Source { public List<int> Values { get; set; } = new List<int> { 7 }; }
public sealed class Target { public List<byte> Values { get; set; } = null!; }"
                : @"public sealed class Source { public Child Child { get; set; } = new Child { Value = 7 }; }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public byte Value { get; set; } }")
                .Replace("__READ_CHECKED__", collection ? "Mapper.__FIRST__(source).Values[0]" : "Mapper.__FIRST__(source).Child.Value")
                .Replace("__READ_UNCHECKED__", collection ? "Mapper.__SECOND__(source).Values[0]" : "Mapper.__SECOND__(source).Child.Value")
                .Replace("__SET_OVERFLOW__", collection ? "source.Values[0] = 300;" : "source.Child.Value = 300;");
            source = WithMethods(source, "NumericConversion = NumericConversion.Checked", "NumericConversion = NumericConversion.Unchecked", reverseNames, reverseDeclarations);

            var result = RunGenerator(source, "numeric-" + collection + reverseNames + reverseDeclarations);
            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
            Assert.AreEqual(2, Helpers(result, collection ? "MapCollection_" : "MapNested_").Length);
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void ExistingTargetHelpersRespectEachEntryNumericPolicy(bool reverseNames, bool reverseDeclarations)
        {
            var first = "[MappingOptions(NumericConversion = NumericConversion.Checked)] public static partial void " +
                FirstName(reverseNames) + "(Source source, Target target);";
            var second = "[MappingOptions(NumericConversion = NumericConversion.Unchecked)] public static partial void " +
                SecondName(reverseNames) + "(Source source, Target target);";
            var source = @"
using System;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public sealed class Source { public Child Child { get; set; } = new Child { Value = 7 }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = new ChildDto(); }
public sealed class ChildDto { public byte Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var checkedTarget = new Target();
        var uncheckedTarget = new Target();
        Mapper.__FIRST__(source, checkedTarget);
        Mapper.__SECOND__(source, uncheckedTarget);
        if (checkedTarget.Child.Value != 7 || uncheckedTarget.Child.Value != 7) return false;
        var previousChild = checkedTarget.Child;
        source.Child.Value = 300;
        Mapper.__SECOND__(source, uncheckedTarget);
        if (uncheckedTarget.Child.Value != 44) return false;
        try { Mapper.__FIRST__(source, checkedTarget); return false; }
        catch (OverflowException)
        {
            return ReferenceEquals(previousChild, checkedTarget.Child) && checkedTarget.Child.Value == 7;
        }
    }
}
".Replace("__METHODS__", reverseDeclarations ? second + Environment.NewLine + first : first + Environment.NewLine + second)
                .Replace("__FIRST__", FirstName(reverseNames)).Replace("__SECOND__", SecondName(reverseNames));

            var result = RunGenerator(source, "update-numeric-" + reverseNames + reverseDeclarations);
            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void ExplicitNullCollectionErrorCannotReuseAContextualNullPreservingHelper(bool reverseNames, bool reverseDeclarations)
        {
            const string template = @"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class Child { public List<int>? Values { get; set; } }
public sealed class ChildDto { public List<int>? Values { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        if (Mapper.__FIRST__(source).Child.Values != null) return false;
        source.Child.Values = new List<int> { 19 };
        var mapped = Mapper.__FIRST__(source).Child.Values;
        return mapped != null && mapped.Count == 1 && mapped[0] == 19 && !ReferenceEquals(mapped, source.Child.Values);
    }
}
";
            var source = WithMethods(template, "", "NullCollections = NullCollectionStrategy.Error", reverseNames, reverseDeclarations);
            var result = RunGenerator(source, "explicit-null-error-" + reverseNames + reverseDeclarations);
            var errors = result.RunResult.Diagnostics.Where(diagnostic => diagnostic.Id == "LITEMAPPER2002").ToArray();
            Assert.AreEqual(1, errors.Length, DiagnosticsText(result));
            Assert.AreEqual(DiagnosticSeverity.Error, errors[0].Severity);
            StringAssert.Contains(errors[0].GetMessage(), "Null collection strategy");
            Assert.AreEqual("Values", errors[0].Location.SourceTree!.GetText().ToString(errors[0].Location.SourceSpan));
            Assert.IsTrue(GeneratedMethods(result).Any(method => method.Identifier.ValueText == FirstName(reverseNames)));
            Assert.IsFalse(GeneratedMethods(result).Any(method => method.Identifier.ValueText == SecondName(reverseNames)));
            AssertOnlyMissingPartialImplementation(result);

            // The invalid public partial declaration intentionally prevents emission. The same
            // implicit mapping without that declaration must preserve null and copy non-null lists.
            var validSource = template.Replace("__METHODS__", "public static partial Target " + FirstName(reverseNames) + "(Source source);")
                .Replace("__FIRST__", FirstName(reverseNames));
            var valid = RunGenerator(validSource, "contextual-null-control-" + reverseNames + reverseDeclarations);
            AssertNoDiagnostics(valid);
            Assert.IsTrue(EmitAndRun(valid), GeneratedSource(valid));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void SharedNestedChildCollectionsRespectPreserveAndEmpty(bool reverseNames, bool reverseDeclarations)
        {
            var source = WithMethods(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class Child { public List<int>? Values { get; set; } }
public sealed class ChildDto { public List<int>? Values { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var preserved = Mapper.__FIRST__(source).Child.Values;
        var empty = Mapper.__SECOND__(source).Child.Values;
        if (preserved != null || empty == null || empty.Count != 0) return false;
        source.Child.Values = new List<int> { 11 };
        var first = Mapper.__FIRST__(source).Child.Values;
        var second = Mapper.__SECOND__(source).Child.Values;
        return first != null && second != null && first[0] == 11 && second[0] == 11 &&
            !ReferenceEquals(first, source.Child.Values) && !ReferenceEquals(second, source.Child.Values);
    }
}
", "NullCollections = NullCollectionStrategy.Preserve", "NullCollections = NullCollectionStrategy.Empty", reverseNames, reverseDeclarations);

            var result = RunGenerator(source, "null-collections-" + reverseNames + reverseDeclarations);
            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void SharedNestedAndElementHelpersRespectEnumMapping(bool reverseNames, bool reverseDeclarations)
        {
            var source = WithMethods(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public enum From { Ready = 1 }
public enum To { Ready = 9 }
public sealed class Source
{
    public Child Child { get; set; } = new Child();
    public List<From> Values { get; set; } = new List<From> { From.Ready };
}
public sealed class Target
{
    public ChildDto Child { get; set; } = null!;
    public List<To> Values { get; set; } = null!;
}
public sealed class Child { public From Value { get; set; } = From.Ready; }
public sealed class ChildDto { public To Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var first = Mapper.__FIRST__(new Source());
        var second = Mapper.__SECOND__(new Source());
        return first.Child.Value == To.Ready && first.Values[0] == To.Ready &&
            (int)second.Child.Value == 1 && (int)second.Values[0] == 1;
    }
}
", "EnumMapping = EnumMappingStrategy.ByName", "EnumMapping = EnumMappingStrategy.ByValue", reverseNames, reverseDeclarations);

            var result = RunGenerator(source, "enum-" + reverseNames + reverseDeclarations);
            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void NullableErrorPolicyIsValidatedDespiteAReusableThrowHelper(bool reverseNames, bool reverseDeclarations)
        {
            var source = WithMethods(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class Child { public string? Value { get; set; } }
public sealed class ChildDto { public string Value { get; set; } = null!; }
", "NullableMismatch = NullableMismatchPolicy.Throw", "NullableMismatch = NullableMismatchPolicy.Error", reverseNames, reverseDeclarations);

            var result = RunGenerator(source, "nullable-policy-" + reverseNames + reverseDeclarations);
            var mismatch = result.RunResult.Diagnostics.Where(diagnostic => diagnostic.Id == "LITEMAPPER2001").ToArray();
            Assert.AreEqual(1, mismatch.Length, DiagnosticsText(result));
            Assert.AreEqual(DiagnosticSeverity.Error, mismatch[0].Severity);
            StringAssert.Contains(mismatch[0].GetMessage(), "Value");
            Assert.AreEqual("Value", mismatch[0].Location.SourceTree!.GetText().ToString(mismatch[0].Location.SourceSpan));
            Assert.IsTrue(GeneratedMethods(result).Any(method => method.Identifier.ValueText == FirstName(reverseNames)));
            Assert.IsFalse(GeneratedMethods(result).Any(method => method.Identifier.ValueText == SecondName(reverseNames)));
            AssertOnlyMissingPartialImplementation(result);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ReferenceHandlingPoliciesKeepIndependentRecursivePlans(bool reverseNames)
        {
            var source = WithMethods(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public sealed class Source { public Node? Child { get; set; } = new Node(); }
public sealed class Target { public NodeDto? Child { get; set; } }
public sealed class Node { public int Value { get; set; } public Node? Next { get; set; } }
public sealed class NodeDto { public int Value { get; set; } public NodeDto? Next { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source { Child = new Node { Value = 3, Next = new Node { Value = 7 } } };
        if (Mapper.__FIRST__(source).Child?.Next?.Value != 7 || Mapper.__SECOND__(source).Child?.Next?.Value != 7) return false;
        source.Child!.Next = source.Child;
        try { Mapper.__SECOND__(source); return false; }
        catch (LiteMapperCycleException cycle)
        {
            return cycle.SourceType == typeof(Node) && cycle.DestinationType == typeof(NodeDto) &&
                cycle.MappingMethod == ""__SECOND__"" && cycle.MemberPath == ""Child.Next"";
        }
    }
}
", "ReferenceHandling = ReferenceHandling.None", "ReferenceHandling = ReferenceHandling.ThrowOnCycle", reverseNames, reverseDeclarations: true);

            var result = RunGenerator(source, "reference-policy-" + reverseNames);
            AssertNoDiagnostics(result, "LITEMAPPER6001");
            Assert.AreEqual(1, result.RunResult.Diagnostics.Count(diagnostic => diagnostic.Id == "LITEMAPPER6001"), DiagnosticsText(result));
            var plain = GeneratedMethods(result).Single(method => method.Identifier.ValueText == FirstName(reverseNames));
            var tracked = GeneratedMethods(result).Single(method => method.Identifier.ValueText == SecondName(reverseNames));
            Assert.IsFalse(plain.ToString().Contains("__tracker", StringComparison.Ordinal), plain.ToString());
            // Check tracker plumbing before invoking cyclic input, so a regression cannot overflow the test host's stack.
            StringAssert.Contains(tracked.ToString(), "__tracker");
            Assert.IsTrue(Helpers(result, "MapNested_").Any(helper => helper.ToString().Contains("__tracker.Enter", StringComparison.Ordinal)), GeneratedSource(result));
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void SharedRecursiveHelperReportsTheInitiatingEntryMethodAndPath(bool reverseDeclarations, bool collideParameterName)
        {
            var source = WithMethods(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
public sealed class Source { public Node? Child { get; set; } = new Node(); }
public sealed class Target { public NodeDto? Child { get; set; } }
public sealed class Node { public int Value { get; set; } public Node? Next { get; set; } }
public sealed class NodeDto { public int Value { get; set; } public NodeDto? Next { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source { Child = new Node { Value = 5, Next = new Node { Value = 8 } } };
        if (Mapper.__FIRST__(source).Child?.Next?.Value != 8 || Mapper.__SECOND__(source).Child?.Next?.Value != 8) return false;
        source.Child!.Next = source.Child;
        return ReportsCycle(() => Mapper.__FIRST__(source), ""__FIRST__"") &&
            ReportsCycle(() => Mapper.__SECOND__(source), ""__SECOND__"");
    }
    private static bool ReportsCycle(Func<Target> map, string entry)
    {
        try { map(); return false; }
        catch (LiteMapperCycleException cycle)
        {
            return cycle.SourceType == typeof(Node) && cycle.DestinationType == typeof(NodeDto) &&
                cycle.MappingMethod == entry && cycle.MemberPath == ""Child.Next"";
        }
    }
}
", "ReferenceHandling = ReferenceHandling.ThrowOnCycle", "ReferenceHandling = ReferenceHandling.ThrowOnCycle", reverseNames: false, reverseDeclarations);

            if (collideParameterName)
            {
                source = source.Replace("(Source source)", "(Source __mappingMethod)");
            }
            var result = RunGenerator(source, "shared-recursive-" + reverseDeclarations + collideParameterName);
            AssertNoDiagnostics(result);
            var tracker = result.RunResult.GeneratedTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                .Single(type => type.Identifier.ValueText == "__LiteMapperCycleTracker");
            Assert.AreEqual(1, tracker.Members.OfType<FieldDeclarationSyntax>().Count(),
                "Entry metadata must be forwarded without growing the per-call tracker state.");
            Assert.AreEqual(1, Helpers(result, "MapNested_").Length, GeneratedSource(result));
            // Keep a missing-tracker regression from invoking a cyclic graph without protection.
            foreach (var entry in GeneratedMethods(result).Where(method => method.Modifiers.Any(SyntaxKind.PublicKeyword) && method.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                StringAssert.Contains(entry.ToString(), "__tracker");
            }
            StringAssert.Contains(Helpers(result, "MapNested_").Single().ToString(), "__tracker.Enter");
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        public void ClosedGenericTargetAnnotationsKeepDistinctNullBehavior()
        {
            var result = RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Box<T> { public T Value { get; set; } = default!; }
public sealed class BoxDto<T> { public T Value { get; set; } = default!; }
public sealed class Source
{
    public Box<string?> AOptional { get; set; } = new Box<string?>();
    public Box<string?> ZRequired { get; set; } = new Box<string?>();
}
public sealed class Target
{
    public BoxDto<string?> AOptional { get; set; } = null!;
    public BoxDto<string> ZRequired { get; set; } = null!;
}
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        source.ZRequired.Value = ""present"";
        var mapped = Mapper.Map(source);
        if (mapped.AOptional.Value != null || mapped.ZRequired.Value != ""present"") return false;
        source.ZRequired.Value = null;
        try { Mapper.Map(source); return false; }
        catch (InvalidOperationException exception) { return exception.Message.Contains(""Value""); }
    }
}
", "generic-target-annotations");

            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
            Assert.AreEqual(2, Helpers(result, "MapNested_").Length);
        }

        [TestMethod]
        public void ClosedGenericSourceAnnotationsDoNotHideLaterNullableErrors()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target AValid(NonNullSource source);
    public static partial Target ZInvalid(NullableSource source);
}
public sealed class Box<T> { public T Value { get; set; } = default!; }
public sealed class BoxDto<T> { public T Value { get; set; } = default!; }
public sealed class NonNullSource { public Box<string> Child { get; set; } = new Box<string>(); }
public sealed class NullableSource { public Box<string?> Child { get; set; } = new Box<string?>(); }
public sealed class Target { public BoxDto<string> Child { get; set; } = null!; }
", "generic-source-annotations");

            Assert.AreEqual(1, result.RunResult.Diagnostics.Count(diagnostic => diagnostic.Id == "LITEMAPPER2001"), DiagnosticsText(result));
            Assert.IsTrue(GeneratedMethods(result).Any(method => method.Identifier.ValueText == "AValid"));
            Assert.IsFalse(GeneratedMethods(result).Any(method => method.Identifier.ValueText == "ZInvalid"));
            AssertOnlyMissingPartialImplementation(result);
        }

        [TestMethod]
        public void CollectionOuterAnnotationsPreserveNullableTargetNulls()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source
{
    public List<int> APresent { get; set; } = new List<int> { 4 };
    public List<int>? ZMissing { get; set; }
}
public sealed class Target
{
    public List<int> APresent { get; set; } = null!;
    public List<int>? ZMissing { get; set; }
}
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var mapped = Mapper.Map(source);
        return mapped.APresent[0] == 4 && mapped.ZMissing == null && !ReferenceEquals(mapped.APresent, source.APresent);
    }
}
", "collection-outer-annotations");

            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void EquivalentInheritedAndExplicitPoliciesReusePrivateHelpers(bool reverseDeclarations)
        {
            var source = WithMethods(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public static partial class Mapper { __METHODS__ }
public sealed class Source
{
    public Child Child { get; set; } = new Child { Value = 6 };
    public List<Child> Items { get; set; } = new List<Child> { new Child { Value = 12 } };
}
public sealed class Target
{
    public ChildDto Child { get; set; } = null!;
    public List<ChildDto> Items { get; set; } = null!;
}
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public byte Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var first = Mapper.__FIRST__(source);
        var second = Mapper.__SECOND__(source);
        return first.Child.Value == 6 && second.Child.Value == 6 && first.Items[0].Value == 12 && second.Items[0].Value == 12 &&
            !ReferenceEquals(first.Items, second.Items) && !ReferenceEquals(first.Child, second.Child);
    }
}
", "", "NumericConversion = NumericConversion.Checked", reverseNames: false, reverseDeclarations);

            var result = RunGenerator(source, "equivalent-policy-" + reverseDeclarations);
            AssertNoDiagnostics(result);
            Assert.AreEqual(1, Helpers(result, "MapNested_").Length, GeneratedSource(result));
            Assert.AreEqual(1, Helpers(result, "MapCollection_").Length, GeneratedSource(result));
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        public void StaticAndStatelessInstanceEntriesKeepTheirNumericPolicies()
        {
            var result = RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    [MappingOptions(NumericConversion = NumericConversion.Checked)]
    public static partial Target AStatic(Source source);
    [MappingOptions(NumericConversion = NumericConversion.Unchecked)]
    public partial Target ZInstance(Source source);
}
public sealed class Source { public Child Child { get; set; } = new Child { Value = 300 }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public byte Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        if (new Mapper().ZInstance(new Source()).Child.Value != 44) return false;
        try { Mapper.AStatic(new Source()); return false; }
        catch (OverflowException) { return true; }
    }
}
", "static-instance-policy");

            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        public void StaticAndInstanceEntriesHaveDistinctHelperExecutionContexts()
        {
            var result = RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public sealed partial class Mapper
{
    public static partial Target AStatic(Source source);
    public partial Target ZInstance(Source source);
}
public sealed class Source { public Child Child { get; set; } = new Child { Value = 7 }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public byte Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var mapper = new Mapper();
        var source = new Source();
        if (mapper.ZInstance(source).Child.Value != 7 || Mapper.AStatic(source).Child.Value != 7) return false;
        source.Child.Value = 300;
        return Overflows(() => Mapper.AStatic(source)) && Overflows(() => mapper.ZInstance(source));
    }
    private static bool Overflows(Func<Target> map)
    {
        try { map(); return false; }
        catch (OverflowException) { return true; }
    }
}
", "static-instance-context");

            AssertNoDiagnostics(result);
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
            Assert.AreEqual(2, Helpers(result, "MapNested_").Length, GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void ConstructorCandidateResolutionRetainsItsEntryMethodContext(bool reverseNames, bool reverseDeclarations)
        {
            // Excluding the current generated method changes constructor viability even when the
            // selected helper contains no declared calls and both entries have identical options.
            var first = "public static partial B? " + FirstName(reverseNames) + "(A source);";
            var second = "public static partial WrapperDto " + SecondName(reverseNames) + "(Wrapper source);";
            var source = @"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper { __METHODS__ }
public sealed class A { public H? Child { get; set; } }
public sealed class B { public HDto? Child { get; set; } }
public sealed class H { public A Node { get; set; } = new A(); }
public sealed class HDto
{
    public int Chosen { get; }
    public HDto(B node) { Chosen = 1; }
    public HDto() { Chosen = 2; }
}
public sealed class Wrapper { public H? Child { get; set; } }
public sealed class WrapperDto { public HDto? Child { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var first = Mapper.__FIRST__(new A { Child = new H() });
        var second = Mapper.__SECOND__(new Wrapper { Child = new H() });
        return first?.Child?.Chosen == 1 && second.Child?.Chosen == 2;
    }
}
".Replace("__METHODS__", reverseDeclarations ? second + Environment.NewLine + first : first + Environment.NewLine + second)
                .Replace("__FIRST__", FirstName(reverseNames)).Replace("__SECOND__", SecondName(reverseNames));

            var result = RunGenerator(source, "constructor-context-" + reverseNames + reverseDeclarations);
            AssertNoDiagnostics(result, "LITEMAPPER6001");
            Assert.IsTrue(EmitAndRun(result), GeneratedSource(result));
        }

        [TestMethod]
        public void PolicyHelperAllocationIsStableAcrossDeclarationAndSyntaxTreeOrder()
        {
            const string models = @"
public sealed class Source { public Child Child { get; set; } = new Child { Value = 7 }; }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public byte Value { get; set; } }
public static class Probe { public static bool Run() => Mapper.AMap(new Source()).Child.Value == 7 && Mapper.ZMap(new Source()).Child.Value == 7; }
";
            const string mapper = @"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { __METHODS__ }
";
            var firstMapper = WithMethods(mapper, "NumericConversion = NumericConversion.Checked", "NumericConversion = NumericConversion.Unchecked", reverseNames: false, reverseDeclarations: false);
            var reversedMapper = WithMethods(mapper, "NumericConversion = NumericConversion.Checked", "NumericConversion = NumericConversion.Unchecked", reverseNames: false, reverseDeclarations: true);
            var first = RunGenerator(new[] { firstMapper, models }, "ordered");
            var reversed = RunGenerator(new[] { models, reversedMapper }, "reversed");
            AssertNoDiagnostics(first);
            AssertNoDiagnostics(reversed);
            Assert.AreEqual(2, Helpers(first, "MapNested_").Length);
            Assert.AreEqual(GeneratedSource(first), GeneratedSource(reversed));
            Assert.IsTrue(EmitAndRun(first));
            Assert.IsTrue(EmitAndRun(reversed));
        }

        private static string FirstName(bool reverseNames) => reverseNames ? "ZMap" : "AMap";

        private static string SecondName(bool reverseNames) => reverseNames ? "AMap" : "ZMap";

        private static string WithMethods(string source, string firstOptions, string secondOptions, bool reverseNames, bool reverseDeclarations)
        {
            var first = (firstOptions.Length == 0 ? "" : "[MappingOptions(" + firstOptions + ")] ") +
                "public static partial Target " + FirstName(reverseNames) + "(Source source);";
            var second = (secondOptions.Length == 0 ? "" : "[MappingOptions(" + secondOptions + ")] ") +
                "public static partial Target " + SecondName(reverseNames) + "(Source source);";
            return source.Replace("__METHODS__", reverseDeclarations ? second + Environment.NewLine + first : first + Environment.NewLine + second)
                .Replace("__FIRST__", FirstName(reverseNames)).Replace("__SECOND__", SecondName(reverseNames));
        }

        private static GeneratorResult RunGenerator(string source, string assemblyName) => RunGenerator(new[] { source }, assemblyName);

        private static GeneratorResult RunGenerator(string[] sources, string assemblyName)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "HelperSemanticIsolation_" + assemblyName,
                sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)),
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

        private static string GeneratedSource(GeneratorResult result) => string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));

        private static MethodDeclarationSyntax[] GeneratedMethods(GeneratorResult result) => result.RunResult.GeneratedTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();

        private static MethodDeclarationSyntax[] Helpers(GeneratorResult result, string prefix) => GeneratedMethods(result)
            .Where(method => method.Modifiers.Any(SyntaxKind.PrivateKeyword) && method.Identifier.ValueText.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        private static string DiagnosticsText(GeneratorResult result) => string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));

        private static void AssertNoDiagnostics(GeneratorResult result, params string[] allowedDiagnostics)
        {
            var unexpected = result.RunResult.Diagnostics.Where(diagnostic => !allowedDiagnostics.Contains(diagnostic.Id)).ToArray();
            Assert.AreEqual(0, unexpected.Length, string.Join(Environment.NewLine, unexpected.Select(diagnostic => diagnostic.ToString())));
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        private static void AssertOnlyMissingPartialImplementation(GeneratorResult result)
        {
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER9001"), DiagnosticsText(result));
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
