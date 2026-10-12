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
    public sealed class SetComparerConversionTests
    {
        [TestMethod]
        [DataRow("local", "HashSet")]
        [DataRow("local", "ISet")]
        [DataRow("local", "IReadOnlySet")]
        [DataRow("default", "HashSet")]
        [DataRow("default", "ISet")]
        [DataRow("default", "IReadOnlySet")]
        [DataRow("external-converter", "HashSet")]
        [DataRow("external-converter", "ISet")]
        [DataRow("external-converter", "IReadOnlySet")]
        [DataRow("external-mapping", "HashSet")]
        [DataRow("external-mapping", "ISet")]
        [DataRow("external-mapping", "IReadOnlySet")]
        public void SameTypeElementConvertersUseDefaultComparer(string kind, string target)
        {
            var external = kind.StartsWith("external", StringComparison.Ordinal);
            var attribute = kind == "default" ? "[DefaultMapping]" : kind == "external-mapping" ? "" : "[MappingConverter]";
            var converter = attribute + @" public static string ConvertElement(string value) { Calls++; return value == ""A"" ? ""x"" : ""X""; }";
            var owner = external ? "External" : "Mapper";
            var result = Run(Header + (external ? "[UseMapper(typeof(External))]" : "") + @"
[LiteMapper] public static partial class Mapper {
    public static partial " + target + @"<string> Map(HashSet<string> source);
    " + (external ? "" : "public static int Calls; " + converter) + @"
}
" + (external ? "public static class External { public static int Calls; " + converter + " }" : "") + @"
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"", ""B"" };
        var mapped = (HashSet<string>)Mapper.Map(source);
        if (!ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) ||
            ReferenceEquals(source, mapped) || mapped.Count != 2 || !mapped.Contains(""x"") ||
            !mapped.Contains(""X"") || " + owner + @".Calls != 2) return false;
        mapped.Add(""C"");
        source.Add(""D"");
        return source.Count == 3 && source.Contains(""a"") && source.Contains(""b"") &&
            !source.Contains(""C"") && !mapped.Contains(""D"");
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Selected same-type conversion must preserve values distinct under the destination default comparer.");
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            StringAssert.Contains(result.Source, "target.Add(");
        }

        [TestMethod]
        [DataRow("HashSet")]
        [DataRow("ISet")]
        [DataRow("IReadOnlySet")]
        public void ConvertedDuplicatesCollapseUsingDefaultEquality(string target)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial " + target + @"<string> Map(HashSet<string> source);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return ""same""; }
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"", ""B"" };
        var mapped = (HashSet<string>)Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) && mapped.Count == 1 &&
            mapped.Contains(""same"") && !mapped.Contains(""SAME"") && Mapper.Calls == 2 && source.Count == 2;
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Set Add may collapse genuine duplicates after every source element is converted once.");
        }

        [TestMethod]
        [DataRow("HashSet", false)]
        [DataRow("ISet", false)]
        [DataRow("IReadOnlySet", false)]
        [DataRow("HashSet", true)]
        [DataRow("ISet", true)]
        [DataRow("IReadOnlySet", true)]
        public void LanguageIdentityPreservesComparerAndIgnoresUnmarkedHelpers(string target, bool empty)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial " + target + @"<string> Map(HashSet<string> source);
    private static string ConvertElement(string value) { Calls++; return ""changed""; }
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        " + (empty ? "" : "source.Add(\"A\");") + @"
        var mapped = (HashSet<string>)Mapper.Map(source);
        if (!ReferenceEquals(source.Comparer, mapped.Comparer) || ReferenceEquals(source, mapped) ||
            mapped.Count != source.Count || Mapper.Calls != 0) return false;
        " + (empty ? "" : "if (!mapped.Contains(\"a\")) return false;") + @"
        mapped.Add(""B"");
        source.Add(""C"");
        return mapped.Contains(""b"") && !source.Contains(""b"") && !mapped.Contains(""c"");
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Only the selected language identity path may reuse the compatible source comparer.");
            StringAssert.Contains(result.Source, "source.Comparer");
            Assert.IsFalse(result.Source.Contains("ConvertElement(", StringComparison.Ordinal));
        }

        [TestMethod]
        [DataRow("HashSet")]
        [DataRow("ISet")]
        [DataRow("IReadOnlySet")]
        public void EmptyConvertedSetsStillUseDefaultComparer(string target)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial " + target + @"<string> Map(HashSet<string> source);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return value; }
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapped = (HashSet<string>)Mapper.Map(source);
        return mapped.Count == 0 && !ReferenceEquals(source, mapped) && Mapper.Calls == 0 &&
            ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default);
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Comparer selection follows the selected conversion even when the source is empty.");
        }

        [TestMethod]
        public void HandwrittenIdentityBodyDoesNotBecomeLanguageIdentity()
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial HashSet<string> Map(HashSet<string> source);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return value; }
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"" };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) && mapped.Contains(""A"") &&
            !mapped.Contains(""a"") && Mapper.Calls == 1;
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "The generator must not infer comparer compatibility by inspecting a handwritten converter body.");
        }

        [TestMethod]
        [DataRow("ISet")]
        [DataRow("IReadOnlySet")]
        public void InterfaceSourcesDoNotDiscoverRuntimeComparers(string sourceType)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial HashSet<string> Map(" + sourceType + @"<string> source);
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"" };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) &&
            !ReferenceEquals(source, mapped) && mapped.Contains(""A"") && !mapped.Contains(""a"");
    }
}");
            AssertValid(result);
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void DifferentElementTypesUseDefaultComparer()
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial HashSet<long> Map(HashSet<int> source);
}
public sealed class AbsoluteComparer : IEqualityComparer<int> {
    public bool Equals(int a, int b) => Math.Abs(a) == Math.Abs(b);
    public int GetHashCode(int value) => Math.Abs(value);
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<int>(new AbsoluteComparer()) { 1 };
        var mapped = Mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<long>.Default) &&
            mapped.Count == 1 && mapped.Contains(1L) && !mapped.Contains(-1L);
    }
}");
            AssertValid(result);
            Assert.IsFalse(result.Source.Contains("source.Comparer", StringComparison.Ordinal));
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void MemberAndNestedUpdateHelpersUseConvertedEquality(bool nested, bool update)
        {
            var result = Run(Header + @"
public sealed class Source {
    public readonly HashSet<string> Items = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"", ""B"" };
    public int Reads;
    public HashSet<string> Values { get { Reads++; return Items; } }
}
public sealed class Target { public IReadOnlySet<string> Values { get; set; } = new HashSet<string> { ""old"" }; }
public sealed class OuterSource { public Source Child { get; set; } = new Source(); }
public sealed class OuterTarget { public Target Child { get; set; } = new Target(); }
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial " + (update ? "void Apply(" + (nested ? "OuterSource source, OuterTarget target" : "Source source, Target target") + ");" :
                (nested ? "OuterTarget Map(OuterSource source);" : "Target Map(Source source);")) + @"
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return value == ""A"" ? ""x"" : ""X""; }
}
public static class Probe {
    public static bool Run() {
        var source = new " + (nested ? "OuterSource" : "Source") + @"();
        var target = new " + (nested ? "OuterTarget" : "Target") + @"();
        var original = target." + (nested ? "Child." : "") + @"Values;
        " + (update ? "Mapper.Apply(source, target);" : "target = Mapper.Map(source);") + @"
        var child = " + (nested ? "source.Child" : "source") + @";
        var mapped = (HashSet<string>)target." + (nested ? "Child." : "") + @"Values;
        return child.Reads == 1 && Mapper.Calls == 2 && !ReferenceEquals(original, mapped) &&
            !ReferenceEquals(child.Items, mapped) && ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) &&
            mapped.Count == 2 && mapped.Contains(""x"") && mapped.Contains(""X"") &&
            !mapped.Contains(""old"") && child.Items.SetEquals(new[] { ""A"", ""B"" });
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Private collection helpers must preserve converted values and replace writable collections with independent copies.");
        }

        [TestMethod]
        [DataRow("HashSet", false)]
        [DataRow("ISet", false)]
        [DataRow("IReadOnlySet", false)]
        [DataRow("HashSet", true)]
        [DataRow("ISet", true)]
        [DataRow("IReadOnlySet", true)]
        public void NullCollectionPoliciesDoNotInvokeElementConverters(string target, bool empty)
        {
            var result = Run(Header + @"
[LiteMapper(NullCollections = NullCollectionStrategy." + (empty ? "Empty" : "Preserve") + @")]
public static partial class Mapper {
    public static int Calls;
    public static partial " + target + "<string>" + (empty ? "" : "?") + @" Map(HashSet<string>? source);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return value; }
}
public static class Probe {
    public static bool Run() {
        var mapped = Mapper.Map(null);
        return Mapper.Calls == 0 && " + (empty ? "mapped is HashSet<string> set && set.Count == 0 && ReferenceEquals(set.Comparer, EqualityComparer<string>.Default)" : "mapped == null") + @";
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void PatchSkipsNullBeforeApplyingConvertedSetReplacement()
        {
            var result = Run(Header + @"
public sealed class Source { public HashSet<string>? Values { get; set; } }
public sealed class Target { public ISet<string> Values { get; set; } = new HashSet<string> { ""old"" }; }
[LiteMapper(IgnoreNullSourceMembers = true, NullCollections = NullCollectionStrategy.Empty)]
public static partial class Mapper {
    public static int Calls;
    public static partial void Apply(Source source, Target target);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return value == ""A"" ? ""x"" : ""X""; }
}
public static class Probe {
    public static bool Run() {
        var source = new Source();
        var target = new Target();
        var original = target.Values;
        Mapper.Apply(source, target);
        if (!ReferenceEquals(original, target.Values) || !target.Values.Contains(""old"") || Mapper.Calls != 0) return false;
        source.Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"", ""B"" };
        Mapper.Apply(source, target);
        var mapped = (HashSet<string>)target.Values;
        return !ReferenceEquals(original, mapped) && !ReferenceEquals(source.Values, mapped) &&
            ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) && mapped.Count == 2 &&
            mapped.Contains(""x"") && mapped.Contains(""X"") && Mapper.Calls == 2;
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Patch null skipping remains independent from comparer selection for a later non-null replacement.");
        }

        [TestMethod]
        [DataRow("HashSet")]
        [DataRow("ISet")]
        [DataRow("IReadOnlySet")]
        public void OneShotSourceIsEnumeratedAndConvertedOnce(string target)
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static int Calls;
    public static partial " + target + @"<string> Map(IEnumerable<string> source);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; return value == ""B"" ? ""X"" : ""x""; }
}
public static class Probe {
    private static int enumerations;
    private static IEnumerable<string> Items() {
        enumerations++;
        if (enumerations != 1) throw new InvalidOperationException(""Second enumeration"");
        yield return ""A"";
        yield return ""B"";
        yield return ""C"";
    }
    public static bool Run() {
        var mapped = (HashSet<string>)Mapper.Map(Items());
        return enumerations == 1 && Mapper.Calls == 3 && mapped.Count == 2 && mapped.Contains(""x"") &&
            mapped.Contains(""X"") && ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default);
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Count probing and duplicate collapse must not enumerate or convert an element twice.");
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<string>()");
        }

        [TestMethod]
        public void InstanceElementConverterKeepsItsReceiverAndDefaultComparer()
        {
            var result = Run(Header + @"
[LiteMapper] public sealed partial class Mapper {
    private readonly string prefix;
    public int Calls;
    public Mapper(string prefix) { this.prefix = prefix; }
    public partial HashSet<string> Map(HashSet<string> source);
    [MappingConverter] private string ConvertElement(string value) { Calls++; return prefix + (value == ""A"" ? ""x"" : ""X""); }
}
public static class Probe {
    public static bool Run() {
        var mapper = new Mapper(""value-"");
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"", ""B"" };
        var mapped = mapper.Map(source);
        return ReferenceEquals(mapped.Comparer, EqualityComparer<string>.Default) && mapped.Count == 2 &&
            mapped.Contains(""value-x"") && mapped.Contains(""value-X"") && mapper.Calls == 2;
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void ElementConverterExceptionsPropagateUnchanged()
        {
            var result = Run(Header + @"
[LiteMapper] public static partial class Mapper {
    public static readonly InvalidOperationException Failure = new InvalidOperationException(""converter failure"");
    public static int Calls;
    public static partial HashSet<string> Map(HashSet<string> source);
    [MappingConverter] private static string ConvertElement(string value) { Calls++; throw Failure; }
}
public static class Probe {
    public static bool Run() {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"", ""B"" };
        try { Mapper.Map(source); return false; }
        catch (InvalidOperationException exception) {
            return ReferenceEquals(exception, Mapper.Failure) && Mapper.Calls == 1 &&
                source.Count == 2 && source.Contains(""a"") && source.Contains(""b"");
        }
    }
}");
            AssertValid(result);
            Assert.IsTrue(Execute(result), "Comparer planning must not change converter exception identity or mutate the source set.");
        }

        [TestMethod]
        public void SameDriverConverterAdditionAndRemovalMatchFreshDeterministicOutput()
        {
            var source = Header + @"
[LiteMapper] public static partial class Mapper {
    public static partial HashSet<string> Map(HashSet<string> source);
    CONVERTER
}";
            var firstCompilation = CreateCompilation(source.Replace("CONVERTER", "", StringComparison.Ordinal));
            var first = Run(firstCompilation, CreateDriver());
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstCompilation.SyntaxTrees.Single(),
                CSharpSyntaxTree.ParseText(source.Replace("CONVERTER", "[MappingConverter] private static string Change(string value) => value.ToUpperInvariant();", StringComparison.Ordinal), ParseOptions, "Mapper.cs"));
            var second = Run(secondCompilation, first.Driver);
            var fresh = Run(secondCompilation, CreateDriver());
            var repeated = Run(secondCompilation, second.Driver);
            var restored = Run(firstCompilation, repeated.Driver);
            AssertValid(first);
            AssertValid(second);
            AssertValid(fresh);
            AssertValid(repeated);
            AssertValid(restored);
            StringAssert.Contains(first.Source, "source.Comparer");
            Assert.IsFalse(second.Source.Contains("source.Comparer", StringComparison.Ordinal));
            StringAssert.Contains(second.Source, "Change(item)");
            Assert.AreEqual(fresh.Source, second.Source);
            Assert.AreEqual(second.Source, repeated.Source);
            Assert.AreEqual(first.Source, restored.Source);
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
            var compilation = CSharpCompilation.Create("SetTests_" + Guid.NewGuid().ToString("N"),
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
