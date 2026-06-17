using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class Milestone9CollectionMappingTests
    {
        [TestMethod]
        public void TopLevelArrayAndListMappingsGenerateSingleEnumerationCopies()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial int[] ToArray(IReadOnlyCollection<int> source);
    public static partial List<string> ToList(IReadOnlyCollection<string> source);
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "var target = new int[source.Count]");
            StringAssert.Contains(generated, "foreach (var item in source)");
            StringAssert.Contains(generated, "var target = new global::System.Collections.Generic.List<string>(source.Count)");
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var array = (int[])assembly.GetType("Mapper")!.GetMethod("ToArray")!.Invoke(null, new object[] { new[] { 1, 2, 3 } })!;
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, array);
            var list = (List<string>)assembly.GetType("Mapper")!.GetMethod("ToList")!.Invoke(null, new object[] { new[] { "a", "b" } })!;
            CollectionAssert.AreEqual(new[] { "a", "b" }, list);
        }

        [TestMethod]
        public void MemberCollectionsSetsDictionariesAndNestedElementsMap()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source
{
    public List<Item> Items { get; set; } = new List<Item>();
    public HashSet<int> Tags { get; set; } = new HashSet<int>();
    public Dictionary<string, Item> Lookup { get; set; } = new Dictionary<string, Item>();
}

public sealed class Target
{
    public ItemDto[] Items { get; set; } = null!;
    public ISet<int> Tags { get; set; } = null!;
    public IReadOnlyDictionary<string, ItemDto> Lookup { get; set; } = null!;
}

public sealed class Item { public int Id { get; set; } }
public sealed class ItemDto { public int Id { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "Items = MapCollection_List_Item_To_ItemDto_Array(source.Items)");
            StringAssert.Contains(generated, "Tags = MapCollection_HashSet_Int32_To_ISet_Int32(source.Tags)");
            StringAssert.Contains(generated, "Lookup = MapDictionary_Dictionary_String_Item_To_IReadOnlyDictionary_String_ItemDto(source.Lookup)");
            StringAssert.Contains(generated, "private static ItemDto MapNested_Item_To_ItemDto(Item source)");

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            var itemType = assembly.GetType("Item")!;
            var item = Activator.CreateInstance(itemType)!;
            itemType.GetProperty("Id")!.SetValue(item, 7);
            ((System.Collections.IList)source.GetType().GetProperty("Items")!.GetValue(source)!).Add(item);
            ((System.Collections.IDictionary)source.GetType().GetProperty("Lookup")!.GetValue(source)!).Add("x", item);

            var target = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;
            var items = (Array)target.GetType().GetProperty("Items")!.GetValue(target)!;
            Assert.AreEqual(1, items.Length);
            Assert.AreEqual(7, items.GetValue(0)!.GetType().GetProperty("Id")!.GetValue(items.GetValue(0)!));
            Assert.AreNotSame(source.GetType().GetProperty("Tags")!.GetValue(source), target.GetType().GetProperty("Tags")!.GetValue(target));
        }

        [TestMethod]
        public void NullCollectionsFollowEffectiveStrategy()
        {
            var error = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}
public sealed class Source { public List<int>? Values { get; set; } }
public sealed class Target { public List<int> Values { get; set; } = null!; }
");
            AssertDiagnostic(error.RunResult, "LITEMAPPER2002");

            var empty = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper(NullCollections = NullCollectionStrategy.Empty)]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}
public sealed class Source { public List<int>? Values { get; set; } }
public sealed class Target { public List<int> Values { get; set; } = null!; }
");
            AssertNoLiteMapperDiagnostics(empty.RunResult);
            StringAssert.Contains(SingleGeneratedSource(empty.RunResult), "if (source == null)");
        }

        [TestMethod]
        public void UnsupportedCollectionShapesReportDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial int[,] ToTarget(int[,] source); }
").RunResult, "LITEMAPPER4002");

            AssertDiagnostic(RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Queue<int> ToTarget(Queue<int> source); }
").RunResult, "LITEMAPPER4001");

            AssertDiagnostic(RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial MyList ToTarget(List<int> source); }
public sealed class MyList : List<int> { }
").RunResult, "LITEMAPPER4006");
        }

        [TestMethod]
        public void EnumerableArrayMappingUsesOneEnumerationWithoutCount()
        {
            var result = RunGenerator(@"
using System.Collections;
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial int[] ToArray(CountingEnumerable source);
}

public sealed class CountingEnumerable : IEnumerable<int>
{
    private readonly int[] _items = new[] { 4, 5 };
    public int Enumerations { get; private set; }
    public IEnumerator<int> GetEnumerator() { Enumerations++; return ((IEnumerable<int>)_items).GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            Assert.IsFalse(generated.Contains("source.Count", StringComparison.Ordinal), generated);
            StringAssert.Contains(generated, "target.ToArray()");

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("CountingEnumerable")!;
            var mapped = (int[])assembly.GetType("Mapper")!.GetMethod("ToArray")!.Invoke(null, new[] { source })!;
            CollectionAssert.AreEqual(new[] { 4, 5 }, mapped);
            Assert.AreEqual(1, source.GetType().GetProperty("Enumerations")!.GetValue(source));
        }

        [TestMethod]
        public void SetComparerIsPreservedAndDictionaryKeyCollisionThrows()
        {
            var result = RunGenerator(@"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial HashSet<string> ToSet(HashSet<string> source);
    public static partial Dictionary<int, string> ToDictionary(Dictionary<string, string> source);
    private static int ToInt(string source) => source.Length;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "source.Comparer");
            StringAssert.Contains(generated, "target.Add(ToInt(item.Key), item.Value)");

            var assembly = Emit(result.Compilation);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
            var mapped = (HashSet<string>)assembly.GetType("Mapper")!.GetMethod("ToSet")!.Invoke(null, new object[] { set })!;
            Assert.IsTrue(mapped.Contains("A"));
            Assert.AreNotSame(set, mapped);

            var dictionary = new Dictionary<string, string> { ["aa"] = "one", ["bb"] = "two" };
            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("ToDictionary")!.Invoke(null, new object[] { dictionary }));
            Assert.IsInstanceOfType<ArgumentException>(exception.InnerException);
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static void AssertNoRuntimeFeatures(string source)
        {
            Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("dynamic", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("Assembly", StringComparison.Ordinal), source);
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id), "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(static d => d.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11),
                optionsProvider: new TestAnalyzerConfigOptionsProvider(),
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static Compilation CreateCompilation(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });

            return CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11)) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString())));
            stream.Position = 0;
            return Assembly.Load(stream.ToArray());
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

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private static readonly AnalyzerConfigOptions Empty = new TestAnalyzerConfigOptions();
            public override AnalyzerConfigOptions GlobalOptions => Empty;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = string.Empty;
                return false;
            }
        }
    }
}
