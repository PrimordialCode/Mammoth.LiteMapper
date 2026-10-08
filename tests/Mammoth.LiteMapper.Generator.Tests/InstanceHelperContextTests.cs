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
    public sealed class InstanceHelperContextTests
    {
        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void NestedHelpersUseTheCallingInstancesConverter(bool memberConvention, bool constructorTarget)
        {
            var source = @"
using System;
using System.Threading.Tasks;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial Target Map(Source source);
    __CONVERTER__
}
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
__CHILD_TARGET__
public static class Probe
{
    public static bool Run()
    {
        var first = new Mapper(""first:"");
        var second = new Mapper(""second:"");
        var source = new Source { Child = new Child { Value = 7 } };
        if (first.Map(source).Child.Value != ""first:7"" || second.Map(source).Child.Value != ""second:7"") return false;
        Parallel.For(0, 64, index =>
        {
            var mapper = index % 2 == 0 ? first : second;
            var expected = (index % 2 == 0 ? ""first:"" : ""second:"") + index;
            var mapped = mapper.Map(new Source { Child = new Child { Value = index } });
            if (mapped.Child.Value != expected) throw new InvalidOperationException(""Mapper instance state leaked."");
        });
        return true;
    }
}
".Replace("__CONVERTER__", memberConvention
                ? "private string MapValue(int value) => prefix + value;"
                : "[MappingConverter] private string Decorate(int value) => prefix + value;")
                .Replace("__CHILD_TARGET__", constructorTarget
                    ? "public sealed class ChildDto { public ChildDto(string value) { Value = value; } public string Value { get; } }"
                    : "public sealed class ChildDto { public string Value { get; set; } = string.Empty; }");

            var result = RunGenerator(source, "nested-" + memberConvention + constructorTarget);
            AssertValidAndRun(result);
            AssertHelperStaticness(result, "MapNested_", isStatic: false);
        }

        [TestMethod]
        [DataRow("array")]
        [DataRow("list")]
        [DataRow("dictionary")]
        public void CollectionHelpersUseInstanceElementKeyAndValueConverters(string shape)
        {
            var sourceType = shape == "array" ? "int[]" : shape == "list" ? "List<int>" : "Dictionary<int, int>";
            var targetType = shape == "array" ? "string[]" : shape == "list" ? "List<string>" : "Dictionary<string, string>";
            var values = shape == "array" ? "new[] { 3, 7 }" : shape == "list" ? "new List<int> { 3, 7 }" : "new Dictionary<int, int> { [3] = 7 }";
            var check = shape == "dictionary"
                ? "mapped.Values.Count == 1 && mapped.Values[prefix + 3] == prefix + 7"
                : "mapped.Values[0] == prefix + 3 && mapped.Values[1] == prefix + 7";
            var source = @"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial Target Map(Source source);
    [MappingConverter] private string Decorate(int value) => prefix + value;
}
public sealed class Source { public __SOURCE_TYPE__ Values { get; set; } = __VALUES__; }
public sealed class Target { public __TARGET_TYPE__ Values { get; set; } = null!; }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        foreach (var prefix in new[] { ""first:"", ""second:"" })
        {
            var mapped = new Mapper(prefix).Map(source);
            if (!(__CHECK__)) return false;
        }
        return true;
    }
}
".Replace("__SOURCE_TYPE__", sourceType).Replace("__TARGET_TYPE__", targetType)
                .Replace("__VALUES__", values).Replace("__CHECK__", check);

            var result = RunGenerator(source, "collection-" + shape);
            AssertValidAndRun(result);
            AssertHelperStaticness(result, shape == "dictionary" ? "MapDictionary_" : "MapCollection_", isStatic: false);
        }

        [TestMethod]
        public void TransitiveNestedAndCollectionHelpersPreserveInstanceContext()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial Target Map(Source source);
    [MappingConverter] private string Decorate(int value) => prefix + value;
}
public sealed class Source { public Branch Branch { get; set; } = new Branch(); }
public sealed class Target { public BranchDto Branch { get; set; } = null!; }
public sealed class Branch { public List<Leaf> Children { get; set; } = new List<Leaf> { new Leaf() }; }
public sealed class BranchDto { public List<LeafDto> Children { get; set; } = null!; }
public sealed class Leaf { public List<int> Values { get; set; } = new List<int> { 5, 9 }; }
public sealed class LeafDto { public List<string> Values { get; set; } = null!; }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var first = new Mapper(""first:"").Map(source).Branch.Children[0].Values;
        var second = new Mapper(""second:"").Map(source).Branch.Children[0].Values;
        return first[0] == ""first:5"" && first[1] == ""first:9"" &&
            second[0] == ""second:5"" && second[1] == ""second:9"";
    }
}
", "transitive");

            AssertValidAndRun(result);
            AssertHelperStaticness(result, "MapNested_", isStatic: false);
            AssertHelperStaticness(result, "MapCollection_", isStatic: false);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void NestedAndCollectionHelpersCanCallDeclaredInstanceMappings(bool handwrittenDefault)
        {
            var source = @"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial Target Map(Source source);
    __DECLARED_MAPPING__
    private string Decorate(int value) => prefix + value;
}
public sealed class Source { public Branch Branch { get; set; } = new Branch(); }
public sealed class Target { public BranchDto Branch { get; set; } = null!; }
public sealed class Branch
{
    public Child Child { get; set; } = new Child { Value = 3 };
    public List<Child> Children { get; set; } = new List<Child> { new Child { Value = 7 } };
}
public sealed class BranchDto
{
    public ChildDto Child { get; set; } = null!;
    public List<ChildDto> Children { get; set; } = null!;
}
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public string Value { get; set; } = string.Empty; }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        foreach (var prefix in new[] { ""first:"", ""second:"" })
        {
            var mapped = new Mapper(prefix).Map(source).Branch;
            if (mapped.Child.Value != prefix + 3 || mapped.Children[0].Value != prefix + 7) return false;
        }
        return true;
    }
}
".Replace("__DECLARED_MAPPING__", handwrittenDefault
                ? "[DefaultMapping] private ChildDto MapChild(Child source) => new ChildDto { Value = Decorate(source.Value) };"
                : "[MapProperty(Source = nameof(Child.Value), Target = nameof(ChildDto.Value), Use = nameof(Decorate))] private partial ChildDto MapChild(Child source);");

            var result = RunGenerator(source, "declared-" + handwrittenDefault);
            AssertValidAndRun(result);
            AssertHelperStaticness(result, "MapNested_", isStatic: false);
            AssertHelperStaticness(result, "MapCollection_", isStatic: false);
            Assert.IsTrue(Helpers(result, "MapNested_").Any(helper => helper.ToString().Contains("MapChild(", StringComparison.Ordinal)), GeneratedSource(result));
            Assert.IsTrue(Helpers(result, "MapCollection_").Any(helper => helper.ToString().Contains("MapChild(", StringComparison.Ordinal)), GeneratedSource(result));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RecursiveHelpersRetainInstanceStateAndPerCallTracking(bool throwOnCycle)
        {
            var source = @"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.__HANDLING__)]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial NodeDto Map(Node source);
    [MappingConverter] private string Decorate(int value) => prefix + value;
}
public sealed class Node
{
    public int Value { get; set; }
    public Node? Next { get; set; }
    public List<Node> Children { get; set; } = new List<Node>();
}
public sealed class NodeDto
{
    public string Value { get; set; } = string.Empty;
    public NodeDto? Next { get; set; }
    public List<NodeDto> Children { get; set; } = null!;
}
public static class Probe
{
    public static bool Run()
    {
        var source = new Node { Value = 3, Next = new Node { Value = 5 }, Children = new List<Node> { new Node { Value = 7 } } };
        foreach (var prefix in new[] { ""first:"", ""second:"" })
        {
            var mapper = new Mapper(prefix);
            var mapped = mapper.Map(source);
            if (mapped.Value != prefix + 3 || mapped.Next?.Value != prefix + 5 || mapped.Children[0].Value != prefix + 7) return false;
            __CYCLE_CHECK__
        }
        return true;
    }
}
".Replace("__HANDLING__", throwOnCycle ? "ThrowOnCycle" : "None")
                .Replace("__CYCLE_CHECK__", throwOnCycle ? @"
            source.Next = source;
            try { mapper.Map(source); return false; }
            catch (LiteMapperCycleException exception)
            {
                if (exception.MappingMethod != ""Map"" || exception.MemberPath != ""Next"") return false;
            }
            finally { source.Next = new Node { Value = 5 }; }
            if (mapper.Map(source).Next?.Value != prefix + 5) return false;
" : "");

            var result = RunGenerator(source, "recursive-" + throwOnCycle);
            AssertValidAndRun(result, throwOnCycle ? Array.Empty<string>() : new[] { "LITEMAPPER6001" });
            AssertHelperStaticness(result, "MapNested_", isStatic: false);
            AssertHelperStaticness(result, "MapCollection_", isStatic: false);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void HelpersWithoutInstanceDependenciesRemainStatic(bool staticMapper)
        {
            var source = @"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public __STATIC__ partial class Mapper
{
    public __STATIC__ partial Target Map(Source source);
    [MappingConverter] private static string Decorate(int value) => ""static:"" + value;
    __UNUSED_CONVERTER__
}
public sealed class Source
{
    public Child Child { get; set; } = new Child { Value = 3 };
    public List<Child> Children { get; set; } = new List<Child> { new Child { Value = 7 } };
}
public sealed class Target
{
    public ChildDto Child { get; set; } = null!;
    public List<ChildDto> Children { get; set; } = null!;
}
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public string Value { get; set; } = string.Empty; }
public static class Probe
{
    public static bool Run()
    {
        var mapped = __RECEIVER__.Map(new Source());
        return mapped.Child.Value == ""static:3"" && mapped.Children[0].Value == ""static:7"";
    }
}
".Replace("__STATIC__", staticMapper ? "static" : "")
                .Replace("__UNUSED_CONVERTER__", staticMapper ? "" : "[MappingConverter] private string Unused(Guid value) => value.ToString();")
                .Replace("__RECEIVER__", staticMapper ? "Mapper" : "new Mapper()")
                .Replace("using System.Collections.Generic;", "using System;\nusing System.Collections.Generic;");

            var result = RunGenerator(source, "static-control-" + staticMapper);
            AssertValidAndRun(result);
            AssertHelperStaticness(result, "MapNested_", isStatic: true);
            AssertHelperStaticness(result, "MapCollection_", isStatic: true);
        }

        [TestMethod]
        public void SiblingHelpersUseInstanceContextOnlyWhenRequired()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial Target Map(Source source);
    [MappingConverter] private string Decorate(int value) => prefix + value;
}
public sealed class Source
{
    public StatefulChild Stateful { get; set; } = new StatefulChild { Value = 3 };
    public PlainChild Plain { get; set; } = new PlainChild { Value = 7 };
}
public sealed class Target
{
    public StatefulChildDto Stateful { get; set; } = null!;
    public PlainChildDto Plain { get; set; } = null!;
}
public sealed class StatefulChild { public int Value { get; set; } }
public sealed class StatefulChildDto { public string Value { get; set; } = string.Empty; }
public sealed class PlainChild { public int Value { get; set; } }
public sealed class PlainChildDto { public int Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var first = new Mapper(""first:"").Map(source);
        var second = new Mapper(""second:"").Map(source);
        return first.Stateful.Value == ""first:3"" && second.Stateful.Value == ""second:3"" &&
            first.Plain.Value == 7 && second.Plain.Value == 7;
    }
}
", "mixed-sibling-contexts");

            AssertValidAndRun(result);
            var helpers = Helpers(result, "MapNested_");
            Assert.AreEqual(2, helpers.Length, GeneratedSource(result));
            var stateful = helpers.Single(helper => helper.ParameterList.Parameters[0].Type!.ToString() == "StatefulChild");
            var plain = helpers.Single(helper => helper.ParameterList.Parameters[0].Type!.ToString() == "PlainChild");
            Assert.IsFalse(stateful.Modifiers.Any(SyntaxKind.StaticKeyword), GeneratedSource(result));
            Assert.IsTrue(plain.Modifiers.Any(SyntaxKind.StaticKeyword), GeneratedSource(result));
        }

        [TestMethod]
        public void EquivalentInstanceEntriesShareInstanceDependentHelpers()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial Target AMap(Source source);
    public partial Target ZMap(Source source);
    [MappingConverter] private string Decorate(int value) => prefix + value;
}
public sealed class Source
{
    public Child Child { get; set; } = new Child { Value = 3 };
    public List<Child> Children { get; set; } = new List<Child> { new Child { Value = 7 } };
}
public sealed class Target
{
    public ChildDto Child { get; set; } = null!;
    public List<ChildDto> Children { get; set; } = null!;
}
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public string Value { get; set; } = string.Empty; }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        foreach (var prefix in new[] { ""first:"", ""second:"" })
        {
            var mapper = new Mapper(prefix);
            var first = mapper.AMap(source);
            var second = mapper.ZMap(source);
            if (first.Child.Value != prefix + 3 || first.Children[0].Value != prefix + 7 ||
                second.Child.Value != prefix + 3 || second.Children[0].Value != prefix + 7 ||
                ReferenceEquals(first.Child, second.Child) || ReferenceEquals(first.Children, second.Children)) return false;
        }
        return true;
    }
}
", "equivalent-instance-entries");

            AssertValidAndRun(result);
            Assert.AreEqual(1, Helpers(result, "MapNested_").Length, GeneratedSource(result));
            Assert.AreEqual(1, Helpers(result, "MapCollection_").Length, GeneratedSource(result));
            AssertHelperStaticness(result, "MapNested_", isStatic: false);
            AssertHelperStaticness(result, "MapCollection_", isStatic: false);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void StaticEntriesDoNotSelectInstanceDeclaredMappings(bool handwrittenDefault)
        {
            var source = @"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    private readonly int offset;
    public Mapper(int offset) { this.offset = offset; }
    public static partial Target AStatic(Source source);
    public partial Target ZInstance(Source source);
    __DECLARED_MAPPING__
    private int AddOffset(int value) => offset + value;
}
public sealed class Source { public Branch Branch { get; set; } = new Branch(); }
public sealed class Target { public BranchDto Branch { get; set; } = null!; }
public sealed class Branch
{
    public Child Child { get; set; } = new Child { Value = 3 };
    public List<Child> Children { get; set; } = new List<Child> { new Child { Value = 7 } };
}
public sealed class BranchDto
{
    public ChildDto Child { get; set; } = null!;
    public List<ChildDto> Children { get; set; } = null!;
}
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public int Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        var direct = Mapper.AStatic(source).Branch;
        var first = new Mapper(10).ZInstance(source).Branch;
        var second = new Mapper(20).ZInstance(source).Branch;
        return direct.Child.Value == 3 && direct.Children[0].Value == 7 &&
            first.Child.Value == 13 && first.Children[0].Value == 17 &&
            second.Child.Value == 23 && second.Children[0].Value == 27;
    }
}
".Replace("__DECLARED_MAPPING__", handwrittenDefault
                ? "[DefaultMapping] private ChildDto MapChild(Child source) => new ChildDto { Value = AddOffset(source.Value) };"
                : "[MapProperty(Source = nameof(Child.Value), Target = nameof(ChildDto.Value), Use = nameof(AddOffset))] private partial ChildDto MapChild(Child source);");

            var result = RunGenerator(source, "static-declared-" + handwrittenDefault);
            AssertValidAndRun(result);
            var helpers = Helpers(result, "MapNested_").Concat(Helpers(result, "MapCollection_")).ToArray();
            Assert.IsTrue(helpers.Any(helper => helper.Modifiers.Any(SyntaxKind.StaticKeyword)), GeneratedSource(result));
            Assert.IsTrue(helpers.Any(helper => !helper.Modifiers.Any(SyntaxKind.StaticKeyword)), GeneratedSource(result));
        }

        [TestMethod]
        public void StaticEntriesIgnoreInstanceDefaultsWhenSelectingAStaticDefault()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    public static partial Target Map(Source source);
    [DefaultMapping] private static ChildDto StaticChild(Child source) => new ChildDto { Value = source.Value + 10 };
    [DefaultMapping] private ChildDto InstanceChild(Child source) => new ChildDto { Value = source.Value + 20 };
}
public sealed class Source { public Child Child { get; set; } = new Child { Value = 3 }; }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public int Value { get; set; } }
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source()).Child.Value == 13;
}
", "static-available-default");

            AssertValidAndRun(result);
            StringAssert.Contains(GeneratedSource(result), "StaticChild(");
            Assert.IsFalse(GeneratedSource(result).Contains("InstanceChild(", StringComparison.Ordinal), GeneratedSource(result));
        }

        [TestMethod]
        public void StaticEntriesIgnoreUnusableInstanceDefaultsAndUseStructuralFallback()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public sealed partial class Mapper
{
    public static partial Target Map(Source source);
    [DefaultMapping] private ChildDto InstanceChild<T>(Child source) => new ChildDto { Value = source.Value + 20 };
}
public sealed class Source { public Child Child { get; set; } = new Child { Value = 3 }; }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class Child { public int Value { get; set; } }
public sealed class ChildDto { public int Value { get; set; } }
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source()).Child.Value == 3;
}
", "static-unusable-instance-default");

            AssertValidAndRun(result);
            AssertHelperStaticness(result, "MapNested_", isStatic: true);
            Assert.IsFalse(GeneratedSource(result).Contains("InstanceChild", StringComparison.Ordinal), GeneratedSource(result));
        }

        private static GeneratorResult RunGenerator(string source, string name)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "InstanceHelperContext_" + name,
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorResult(driver.GetRunResult(), updatedCompilation);
        }

        private static void AssertValidAndRun(GeneratorResult result, params string[] allowedDiagnostics)
        {
            var unexpected = result.RunResult.Diagnostics.Where(diagnostic => !allowedDiagnostics.Contains(diagnostic.Id)).ToArray();
            Assert.AreEqual(0, unexpected.Length, string.Join(Environment.NewLine, unexpected.Select(diagnostic => diagnostic.ToString())));
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));

            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.IsTrue((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!, GeneratedSource(result));
        }

        private static MethodDeclarationSyntax[] Helpers(GeneratorResult result, string prefix) => result.RunResult.GeneratedTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(method => method.Modifiers.Any(SyntaxKind.PrivateKeyword) && method.Identifier.ValueText.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        private static void AssertHelperStaticness(GeneratorResult result, string prefix, bool isStatic)
        {
            var helpers = Helpers(result, prefix);
            Assert.IsTrue(helpers.Length > 0, "The fixture must exercise a generated helper.\n" + GeneratedSource(result));
            Assert.IsTrue(helpers.All(helper => helper.Modifiers.Any(SyntaxKind.StaticKeyword) == isStatic), GeneratedSource(result));
        }

        private static string GeneratedSource(GeneratorResult result) => string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));

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
