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
    public sealed class CollectionContractEdgeTests
    {
        [TestMethod]
        public void IReadOnlySetTargetUsesIndependentHashSetCopyAndPreservesComparer()
        {
            var result = RunGenerator(@"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial IReadOnlySet<string> Map(HashSet<string> source);
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var mapper = assembly.GetType("Mapper")!;
            var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha" };
            var mapped = (IReadOnlySet<string>)mapper.GetMethod("Map")!.Invoke(null, new object[] { source })!;

            Assert.AreEqual(typeof(HashSet<string>), mapped.GetType(), "Section 15.3 requires HashSet<T> for IReadOnlySet<T> targets.");
            Assert.AreNotSame(source, mapped, "Section 15.7 requires mutable collections to be copied.");
            Assert.IsTrue(mapped.Contains("ALPHA"), "The identity-compatible case-insensitive comparer must remain effective.");
            source.Add("beta");
            Assert.IsFalse(mapped.Contains("beta"), "The mapped set must be detached from later source mutations.");
        }

        [TestMethod]
        public void IDictionaryTargetUsesIndependentDictionaryCopyAndPreservesCaseInsensitiveComparer()
        {
            var result = RunGenerator(@"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial IDictionary<string, int> Map(Dictionary<string, int> source);
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var mapper = assembly.GetType("Mapper")!;
            var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["alpha"] = 7 };
            var mapped = (IDictionary<string, int>)mapper.GetMethod("Map")!.Invoke(null, new object[] { source })!;

            Assert.AreEqual(typeof(Dictionary<string, int>), mapped.GetType(), "Section 15.3 requires Dictionary<TKey,TValue> for IDictionary<TKey,TValue> targets.");
            Assert.AreNotSame(source, mapped, "Section 15.7 requires mutable collections to be copied.");
            Assert.IsTrue(mapped.ContainsKey("ALPHA"), "Section 15.10 requires preserving an identity-compatible case-insensitive comparer.");
            Assert.AreEqual(7, mapped["ALPHA"]);
            source["beta"] = 8;
            Assert.IsFalse(mapped.ContainsKey("beta"), "The mapped dictionary must be detached from later source mutations.");
        }

        [TestMethod]
        public void PreserveAllowsNullableCollectionTargetAndReturnsNull()
        {
            var result = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullCollections = NullCollectionStrategy.Preserve)]
public static partial class Mapper
{
    public static partial List<int>? Map(List<int>? source);
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var mapped = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new object?[] { null });
            Assert.IsNull(mapped, "Section 6.3 permits Preserve when both collection types are nullable.");
        }

        [TestMethod]
        public void PreserveRejectsNonNullableCollectionTarget()
        {
            var result = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullCollections = NullCollectionStrategy.Preserve)]
public static partial class Mapper
{
    public static partial List<int> Map(List<int>? source);
}
");

            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2002"),
                "Sections 6.3 and 20.2 require LITEMAPPER2002 when Preserve cannot satisfy a nonnullable target. Actual diagnostics: " +
                string.Join(", ", result.RunResult.Diagnostics.Select(static d => d.Id)));
        }

        [TestMethod]
        public void ExistingTargetCollectionIsReplacedWithAnIndependentMappedCollection()
        {
            var result = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
public class Source { public List<int> Items { get; set; } = new List<int>(); }
public class Target { public List<int> Items { get; set; } = new List<int>(); }
[LiteMapper] public static partial class Mapper { public static partial void Apply(Source source, Target target); }
public static class Probe {
    public static int Run() {
        var source = new Source { Items = new List<int> { 1, 2 } };
        var target = new Target { Items = new List<int> { 5 } };
        var original = target.Items;
        Mapper.Apply(source, target);
        return object.ReferenceEquals(original, target.Items) ? -1 : target.Items.Count * 10 + target.Items[0];
    }
}");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            Assert.AreEqual(21, Emit(result.Compilation).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null),
                "Section 15.12 requires replacement with a detached mapped collection.");
        }

        [TestMethod]
        public void PatchModeSkipsCollectionWhenAConfiguredSourcePathSegmentIsNull()
        {
            var result = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
public class Payload { public List<int>? Items { get; set; } }
public class Source { public Payload? Data { get; set; } }
public class Target { public List<int> Items { get; set; } = new List<int>(); }
[LiteMapper(IgnoreNullSourceMembers = true)] public static partial class Mapper {
    [MapProperty(Source = ""Data.Items"", Target = nameof(Target.Items))]
    public static partial void Apply(Source source, Target target);
}
public static class Probe {
    public static int Run() {
        var target = new Target { Items = new List<int> { 5 } };
        Mapper.Apply(new Source { Data = null }, target);
        return target.Items.Count == 1 ? target.Items[0] : -1;
    }
}");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            Assert.AreEqual(5, Emit(result.Compilation).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null),
                "Sections 15.11 and 16.3 require a null intermediate path to preserve the target collection.");
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
            return Assembly.Load(stream.ToArray());
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(d => d.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
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
