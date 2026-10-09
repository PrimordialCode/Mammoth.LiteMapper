using System;
using System.Collections.Generic;
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
    public sealed class NullableStructuralElementTests
    {
        [TestMethod]
        [DataRow("array", "Error")]
        [DataRow("list", "Error")]
        [DataRow("enumerable", "Error")]
        [DataRow("readonly", "Error")]
        [DataRow("dictionary", "Error")]
        [DataRow("jagged", "Error")]
        [DataRow("nested_list", "Error")]
        [DataRow("array", "Throw")]
        public void NullableTargetsPreserveEveryElementAndItsOrder(string shape, string policy)
        {
            var result = Generate(Fixture(shape, policy, nullableTarget: true));
            AssertRuns(result);
        }

        [TestMethod]
        [DataRow("array")]
        [DataRow("list")]
        [DataRow("enumerable")]
        [DataRow("readonly")]
        [DataRow("dictionary")]
        [DataRow("jagged")]
        [DataRow("nested_list")]
        public void ThrowRejectsNullBeforeCallingTheStructuralHelper(string shape)
        {
            var result = Generate(Fixture(shape, "Throw", nullableTarget: false));
            AssertRuns(result);
        }

        [TestMethod]
        [DataRow("array")]
        [DataRow("list")]
        [DataRow("enumerable")]
        [DataRow("readonly")]
        [DataRow("dictionary")]
        [DataRow("jagged")]
        [DataRow("nested_list")]
        public void ErrorRejectsNullableElementsAndOmitsTheirImplementation(string shape)
        {
            var result = Generate(Fixture(shape, "Error", nullableTarget: false));
            Assert.IsTrue(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER2003"),
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER9001"));
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "An invalid element contract must not emit a partial implementation or silently filter nulls.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MemberAndPatchCollectionMappingStillPreserveNullElements(bool update)
        {
            var source = @"
using System;
using Mammoth.LiteMapper;
[LiteMapper(OPTIONS)]
public static partial class Mapper { public static partial METHOD; }
public sealed class Root { public Source?[]? Items { get; set; } }
public sealed class RootDto { public Target?[]? Items { get; set; } }
" + ElementTypes + @"
public static class Probe
{
    public static bool Run()
    {
        var root = new Root { Items = new Source?[] { null, new Source(7), null, new Source(9) } };
        var destination = new RootDto();
        var result = CALL;
        if (result.Items == null || result.Items.Length != 4 || result.Items[0] != null ||
            result.Items[1]?.Value != 7 || result.Items[2] != null || result.Items[3]?.Value != 9) return false;
        var previous = result.Items;
        root.Items = null;
        var nullResult = CALL;
        return EXPECTED;
    }
}
";
            source = source.Replace("OPTIONS", update ? "IgnoreNullSourceMembers = true" : "")
                .Replace("METHOD", update ? "RootDto Map(Root source, RootDto destination)" : "RootDto Map(Root source)")
                .Replace("CALL", update ? "Mapper.Map(root, destination)" : "Mapper.Map(root)")
                .Replace("EXPECTED", update ? "ReferenceEquals(previous, nullResult.Items)" : "nullResult.Items == null");
            AssertRuns(Generate(source));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void NullableNestedCollectionsPreserveBothNullContainersAndNullObjects(bool nullLeaf)
        {
            var result = Generate(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { public static partial Target?[]?[] Map(Source?[]?[] source); }
" + ElementTypes + @"
public static class Probe
{
    public static bool Run()
    {
        var source = new Source?[]?[] { null, new Source?[] { LEAF, new Source(7) }, new Source?[0] };
        var mapped = Mapper.Map(source);
        return mapped.Length == 3 && mapped[0] == null && mapped[1]?.Length == 2 &&
            mapped[1]?[0]?.Value == EXPECTED && mapped[1]?[1]?.Value == 7 && mapped[2]?.Length == 0;
    }
}
".Replace("LEAF", nullLeaf ? "null" : "new Source(3)").Replace("EXPECTED", nullLeaf ? "null" : "3"));
            AssertRuns(result);
        }

        [TestMethod]
        public void EmptyCollectionStrategyDoesNotReplaceOrDropNullStructuralElements()
        {
            var source = Fixture("array", "Error", nullableTarget: true)
                .Replace("NullableMismatch = NullableMismatchPolicy.Error", "NullCollections = NullCollectionStrategy.Empty");
            AssertRuns(Generate(source));
        }

        [TestMethod]
        public void NullableElementsForwardCycleContextAndSkipNullBeforeTracking()
        {
            var result = Generate(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper { public static partial Target?[] Map(Source?[] source); }
public sealed class Source { public Source? Next { get; set; } public int Value { get; set; } }
public sealed class Target { public Target? Next { get; set; } public int Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var node = new Source { Value = 7 };
        var source = new Source?[] { null, node, node };
        var mapped = Mapper.Map(source);
        if (mapped.Length != 3 || mapped[0] != null || mapped[1]?.Value != 7 ||
            mapped[2]?.Value != 7 || ReferenceEquals(mapped[1], mapped[2])) return false;
        node.Next = node;
        try { Mapper.Map(source); return false; }
        catch (LiteMapperCycleException error)
        {
            if (error.MappingMethod != ""Map"" || error.MemberPath != ""Next"" ||
                error.SourceType != typeof(Source) || error.DestinationType != typeof(Target)) return false;
        }
        node.Next = null;
        return Mapper.Map(source)[1]?.Value == 7;
    }
}
");
            AssertRuns(result);
            var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees);
            Assert.AreEqual(1, CSharpSyntaxTree.ParseText(generated).GetRoot().DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>().Count(node => node.Type.ToString() == "__LiteMapperCycleTracker"));
        }

        [TestMethod]
        public void HigherPrecedenceNullableConverterReceivesNullWithoutStructuralInterception()
        {
            AssertRuns(Generate(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static int Calls;
    public static partial Target[] Map(Source?[] source);
    [MappingConverter] private static Target Convert(Source? source)
    {
        Calls++;
        return new Target { Value = source == null ? -1 : source.Value + 10 };
    }
}
" + ElementTypes + @"
public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new Source?[] { null, new Source(7) });
        return mapped.Length == 2 && mapped[0].Value == -1 && mapped[1].Value == 17 && Mapper.Calls == 2;
    }
}
"));
        }

        [TestMethod]
        public void InstanceConvertersStillRunOnlyForPresentStructuralElements()
        {
            AssertRuns(Generate(@"
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly int offset;
    public int Calls;
    public Mapper(int offset) { this.offset = offset; }
    public partial Target?[] Map(Source?[] source);
    [MappingConverter] private int Convert(int source) { Calls++; return source + offset; }
}
" + ElementTypes + @"
public static class Probe
{
    public static bool Run()
    {
        var mapper = new Mapper(10);
        var other = new Mapper(20);
        var source = new Source?[] { null, new Source(7) };
        var first = mapper.Map(source);
        var second = other.Map(source);
        return first[0] == null && first[1]?.Value == 17 && second[0] == null && second[1]?.Value == 27 &&
            mapper.Calls == 1 && other.Calls == 1;
    }
}
"));
        }

        [TestMethod]
        public void GeneratedNullBoundariesAreWarningFreeAndDeterministic()
        {
            var source = Fixture("dictionary", "Error", nullableTarget: true);
            var first = Generate(source);
            var second = Generate(source);
            AssertClean(first);
            Assert.AreEqual(string.Join(Environment.NewLine, first.RunResult.GeneratedTrees),
                string.Join(Environment.NewLine, second.RunResult.GeneratedTrees));
            Assert.IsFalse(string.Join(Environment.NewLine, first.RunResult.GeneratedTrees).Contains("default!", StringComparison.Ordinal));
        }

        private static string Fixture(string shape, string policy, bool nullableTarget)
        {
            var (sourceType, targetType, initialize, values) = shape switch
            {
                "array" => ("Source?[]", "Target?[]", "elements", "mapped"),
                "list" => ("List<Source?>", "List<Target?>", "new List<Source?>(elements)", "mapped"),
                "enumerable" => ("IEnumerable<Source?>", "Target?[]", "Enumerate(elements)", "mapped"),
                "readonly" => ("IReadOnlyList<Source?>", "IReadOnlyList<Target?>", "elements", "mapped"),
                "dictionary" => ("Dictionary<string, Source?>", "Dictionary<string, Target?>", "new Dictionary<string, Source?> { [\"first\"] = elements[0], [\"second\"] = elements[1], [\"third\"] = elements[2], [\"fourth\"] = elements[3] }", "new[] { mapped[\"first\"], mapped[\"second\"], mapped[\"third\"], mapped[\"fourth\"] }"),
                "jagged" => ("Source?[][]", "Target?[][]", "new[] { elements, new Source?[0] }", "mapped[0]"),
                "nested_list" => ("List<List<Source?>>", "List<List<Target?>>", "new List<List<Source?>> { new List<Source?>(elements), new List<Source?>() }", "mapped[0]"),
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };
            if (!nullableTarget) targetType = targetType.Replace("Target?", "Target");
            var source = @"
using System;
using System.Collections.Generic;
using System.Linq;
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.POLICY)]
public static partial class Mapper { public static partial TARGET Map(SOURCE source); }
ELEMENT_TYPES
public static class Probe
{
    private static int enumerations;
    private static int yields;
    private static IEnumerable<Source?> Enumerate(Source?[] elements)
    {
        if (++enumerations != 1) throw new InvalidOperationException(""Repeated enumeration."");
        foreach (var element in elements) { yields++; yield return element; }
    }
    public static bool Run()
    {
        var elements = new Source?[] { null, new Source(7), null, new Source(9) };
        var source = INITIAL;
        BODY
    }
}
".Replace("POLICY", policy).Replace("TARGET", targetType).Replace("SOURCE", sourceType)
                .Replace("ELEMENT_TYPES", ElementTypes).Replace("INITIAL", initialize);
            var preserve = @"
        var mapped = Mapper.Map(source);
        var values = VALUES;
        if (values.Count() != 4 || values.ElementAt(0) != null || values.ElementAt(1)?.Value != 7 ||
            values.ElementAt(2) != null || values.ElementAt(3)?.Value != 9 || Source.Reads != 2 || Target.Created != 2) return false;
        EXTRA
        return true;".Replace("VALUES", values).Replace("EXTRA", shape == "enumerable" ? "if (enumerations != 1 || yields != 4) return false;" :
                    shape == "jagged" ? "if (mapped.Length != 2 || mapped[1].Length != 0) return false;" :
                    shape == "nested_list" ? "if (mapped.Count != 2 || mapped[1].Count != 0) return false;" : "");
            var reject = @"
        try { Mapper.Map(source); return false; }
        catch (InvalidOperationException error)
        {
            return error.GetType() == typeof(InvalidOperationException) && error.Message.Contains(""PATH"") &&
                Source.Reads == 0 && Target.Created == 0;
        }".Replace("PATH", shape == "dictionary" ? "item.Value" : "item");
            return source.Replace("BODY", nullableTarget ? preserve : reject);
        }

        private const string ElementTypes = @"
public sealed class Source
{
    private readonly int value;
    public static int Reads;
    public Source(int value) { this.value = value; }
    public int Value { get { Reads++; return value; } }
}
public sealed class Target
{
    public static int Created;
    public Target() { Created++; }
    public int Value { get; set; }
}
";

        private static void AssertRuns((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.IsFalse(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning),
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            AssertClean(result);
        }

        private static void AssertClean((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            var problems = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, problems.Length, string.Join(Environment.NewLine, problems.Select(diagnostic => diagnostic.ToString())));
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Generate(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var compilation = CSharpCompilation.Create("NullableStructuralElementTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid fixture: " + string.Join(Environment.NewLine, inputErrors.Select(diagnostic => diagnostic.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
