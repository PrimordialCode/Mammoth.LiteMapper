using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mammoth.LiteMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class DictionaryComparerConversionTests
    {
        [TestMethod]
        [DataRow("Dictionary", false)]
        [DataRow("IDictionary", false)]
        [DataRow("IReadOnlyDictionary", false)]
        [DataRow("Dictionary", true)]
        [DataRow("IDictionary", true)]
        [DataRow("IReadOnlyDictionary", true)]
        public void ValueWideningPreservesKeyComparerAndIndependentCopy(string target, bool empty)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial " + target + @"<string, long> Map(Dictionary<string, int> source);
}
public static class Probe {
    public static bool Run() {
        var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        " + (empty ? "" : "source.Add(\"A\", 7);") + @"
        var mapped = (Dictionary<string, long>)Mapper.Map(source);
        if (!ReferenceEquals(source.Comparer, mapped.Comparer) || ReferenceEquals(source, mapped) ||
            mapped.Count != source.Count) return false;
        " + (empty ? "" : "if (!mapped.ContainsKey(\"a\") || mapped[\"a\"] != 7L) return false;") + @"
        mapped.Add(""B"", 8L);
        source.Add(""C"", 9);
        return !source.ContainsKey(""b"") && !mapped.ContainsKey(""c"");
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Value conversion must not change key equality or alias mutable dictionaries.");
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.Dictionary<string, long>(source.Count, source.Comparer)");
            StringAssert.Contains(result.Source, "target.Add(item.Key, item.Value)");
        }

        [TestMethod]
        [DataRow("Dictionary", false)]
        [DataRow("IDictionary", false)]
        [DataRow("IReadOnlyDictionary", false)]
        [DataRow("Dictionary", true)]
        [DataRow("IReadOnlyDictionary", true)]
        public void StructuralValuesPreserveKeyComparer(string target, bool nullable)
        {
            var suffix = nullable ? "?" : "";
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial " + target + "<string, Target" + suffix + "> Map(Dictionary<string, Source" + suffix + @"> source);
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public long Value { get; set; } }
public static class Probe {
    public static bool Run() {
        var value = new Source { Value = 7 };
        var source = new Dictionary<string, Source" + suffix + @">(StringComparer.OrdinalIgnoreCase) { [""A""] = value };
        " + (nullable ? "source.Add(\"Null\", null);" : "") + @"
        var mapped = (Dictionary<string, Target" + suffix + @">)Mapper.Map(source);
        return ReferenceEquals(source.Comparer, mapped.Comparer) && !ReferenceEquals(source, mapped) &&
            mapped.ContainsKey(""a"") && mapped[""a""]?.Value == 7L && !ReferenceEquals(value, mapped[""a""])
            " + (nullable ? "&& mapped.ContainsKey(\"null\") && mapped[\"null\"] == null" : "") + @";
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Structural value mapping must preserve key equality, including nullable values.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ValueConvertersPreserveKeyComparerAndRunOnce(bool widening)
        {
            var valueType = widening ? "long" : "int";
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial Dictionary<string, " + valueType + @"> Map(Dictionary<string, int> source);
    [MappingConverter] private static " + valueType + @" ConvertValue(int value) { Calls++; return value + 10; }
}
public static class Probe {
    public static bool Run() {
        var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [""A""] = 7, [""B""] = 9 };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(source.Comparer, mapped.Comparer) && mapped.ContainsKey(""a"") &&
            mapped[""a""] == 17 && mapped[""b""] == 19 && Mapper.Calls == 2;
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Value converter selection is independent of the identity-compatible key comparer.");
        }

        [TestMethod]
        [DataRow("local", false)]
        [DataRow("default", false)]
        [DataRow("external-converter", false)]
        [DataRow("external-mapping", false)]
        [DataRow("local", true)]
        [DataRow("default", true)]
        [DataRow("external-converter", true)]
        [DataRow("external-mapping", true)]
        public void SameTypeKeyConvertersUseDefaultComparer(string kind, bool wideningValues)
        {
            var external = kind.StartsWith("external", StringComparison.Ordinal);
            var attribute = kind == "default" ? "[DefaultMapping]" : kind == "external-mapping" ? "" : "[MappingConverter]";
            var converter = attribute + @" public static string ConvertKey(string value) { Calls++; return value == ""first"" ? ""A"" : ""a""; }";
            var valueType = wideningValues ? "long" : "int";
            var owner = external ? "External" : "Mapper";
            var result = Run(Header + (external ? "[UseMapper(typeof(External))]" : "") + @"
[LiteMapper] public static partial class Mapper {
    public static partial Dictionary<string, " + valueType + @"> Map(Dictionary<string, int> source);
    " + (external ? "" : "public static int Calls; " + converter) + @"
}
" + (external ? "public static class External { public static int Calls; " + converter + " }" : "") + @"
public static class Probe {
    public static bool Run() {
        var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [""first""] = 7, [""second""] = 9 };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) &&
            mapped.Count == 2 && mapped[""A""] == 7 && mapped[""a""] == 9 && " + owner + @".Calls == 2;
    }
}");
            AssertValid(result);
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.IsTrue(Execute(result), "A selected key converter is not identity conversion merely because its input and output types match.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ConvertedKeyCollisionsThrowThroughAdd(bool sameType)
        {
            var keyType = sameType ? "string" : "int";
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial Dictionary<" + keyType + @", long> Map(Dictionary<string, int> source);
    [MappingConverter] private static " + keyType + @" ConvertKey(string value) { Calls++; return " + (sameType ? "\"same\"" : "1") + @"; }
}
public static class Probe {
    public static bool Run() {
        var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [""first""] = 7, [""second""] = 9 };
        try { Mapper.Map(source); return false; }
        catch (ArgumentException) { return Mapper.Calls == 2 && source.Count == 2; }
    }
}");
            AssertValid(result);
            StringAssert.Contains(result.Source, "target.Add(");
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.IsTrue(Execute(result), "Converted-key collisions must throw rather than silently overwrite or discard an entry.");
        }

        [TestMethod]
        public void DifferentKeyTypesUseDefaultComparer()
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial Dictionary<long, long> Map(Dictionary<int, int> source);
}
public sealed class KeyComparer : IEqualityComparer<int> {
    public bool Equals(int a, int b) => Math.Abs(a) == Math.Abs(b);
    public int GetHashCode(int value) => Math.Abs(value);
}
public static class Probe {
    public static bool Run() {
        var source = new Dictionary<int, int>(new KeyComparer()) { [1] = 7 };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<long>.Default) &&
            mapped[1] == 7 && !mapped.ContainsKey(-1);
    }
}");
            AssertValid(result);
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        [DataRow("IDictionary")]
        [DataRow("IReadOnlyDictionary")]
        public void InterfaceSourcesDoNotDiscoverRuntimeComparers(string sourceType)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial Dictionary<string, long> Map(" + sourceType + @"<string, int> source);
}
public static class Probe {
    public static bool Run() {
        var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [""A""] = 7 };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) && mapped[""A""] == 7 && !mapped.ContainsKey(""a"");
    }
}");
            AssertValid(result);
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MemberMappingAndUpdatePreserveComparerWithoutAliasing(bool update)
        {
            var result = Run(Header + @"
public sealed class Source {
    private readonly Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [""A""] = 7 };
    public int Reads;
    public Dictionary<string, int> Values { get { Reads++; return values; } }
}
public sealed class Target { public IReadOnlyDictionary<string, long> Values { get; set; } = new Dictionary<string, long>(); }
[LiteMapper] public static partial class Mapper {
    public static partial " + (update ? "void Apply(Source source, Target target);" : "Target Map(Source source);") + @"
}
public static class Probe {
    public static bool Run() {
        var source = new Source();
        var target = new Target();
        var original = target.Values;
        " + (update ? "Mapper.Apply(source, target);" : "target = Mapper.Map(source);") + @"
        var reads = source.Reads;
        var mapped = (Dictionary<string, long>)target.Values;
        return reads == 1 && !ReferenceEquals(original, target.Values) &&
            ReferenceEquals(source.Values.Comparer, mapped.Comparer) && mapped[""a""] == 7;
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Dictionary helper paths must preserve key comparers and replace writable target collections.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void NullCollectionPolicyRemainsIndependent(bool empty)
        {
            var result = Run(Header + @"
[LiteMapper(NullCollections = NullCollectionStrategy." + (empty ? "Empty" : "Preserve") + @")]
public static partial class Mapper {
    public static partial Dictionary<string, long>" + (empty ? "" : "?") + @" Map(Dictionary<string, int>? source);
}
public static class Probe {
    public static bool Run() {
        var mapped = Mapper.Map(null);
        return " + (empty ? "mapped.Count == 0 && ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default)" : "mapped == null") + @";
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void SameDriverKeyConversionChangeMatchesFreshDeterministicOutput()
        {
            var source = Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial Dictionary<string, long> Map(Dictionary<string, int> source);
    CONVERTER
}";
            var firstCompilation = CreateCompilation(source.Replace("CONVERTER", "", StringComparison.Ordinal));
            var first = Run(firstCompilation, CreateDriver());
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstCompilation.SyntaxTrees.Single(),
                CSharpSyntaxTree.ParseText(source.Replace("CONVERTER", "[MappingConverter] private static string Change(string key) => key.ToUpperInvariant();", StringComparison.Ordinal), ParseOptions, "Mapper.cs"));
            var second = Run(secondCompilation, first.Driver);
            var fresh = Run(secondCompilation, CreateDriver());
            AssertValid(first);
            AssertValid(second);
            AssertValid(fresh);
            StringAssert.Contains(first.Source, "source.Comparer");
            Assert.IsFalse(second.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.AreEqual(fresh.Source, second.Source);
            Assert.AreEqual(second.Source, Run(secondCompilation, second.Driver).Source);
        }

        private const string Header = "using System; using System.Collections.Generic; using Mammoth.LiteMapper;\n";
        private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);

        private static CSharpCompilation CreateCompilation(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Where(static path => path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Append(MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location));
            var compilation = CSharpCompilation.Create("DictionaryTests_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, ParseOptions, "Mapper.cs") }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));
            return compilation;
        }

        private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
            new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        private static Result Run(string source) => Run(CreateCompilation(source), CreateDriver());
        private static Result Run(CSharpCompilation compilation, GeneratorDriver driver)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            return new Result(driver, output, string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.ToString())));
        }
        private static void AssertValid(Result result)
        {
            var diagnostics = result.Driver.GetRunResult().Diagnostics.Concat(result.Compilation.GetDiagnostics())
                .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }
        private static bool Execute(Result result)
        {
            using var stream = new MemoryStream();
            var emit = result.Compilation.Emit(stream);
            Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return (bool)Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        private sealed record Result(GeneratorDriver Driver, Compilation Compilation, string Source);
    }
}
