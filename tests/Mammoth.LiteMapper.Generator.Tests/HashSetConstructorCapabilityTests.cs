using System;
using System.Collections.Generic;
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
    public sealed class HashSetConstructorCapabilityTests
    {
        [TestMethod]
        [DataRow("", false)]
        [DataRow("public HashSet(int capacity) { }", true)]
        [DataRow("internal HashSet(int capacity) { }", true)]
        [DataRow("private HashSet(int capacity) { }", false)]
        [DataRow("public HashSet(long capacity) { }", false)]
        [DataRow("public HashSet(ref int capacity) { }", false)]
        [DataRow("public HashSet(in int capacity) { }", false)]
        [DataRow("public HashSet(params int[] capacity) { }", false)]
        [DataRow("public HashSet(int capacity, int extra = 0) { }", false)]
        [DataRow("public HashSet(int capacity, IEqualityComparer<T> comparer) { }", false)]
        public void CapacityRequiresItsOwnAccessibleExactSignature(string constructors, bool available)
        {
            var result = Run(Mapper("int[]", "HashSet<int>"), Framework(constructors));
            AssertValid(result, synthetic: true);
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<int>(" + (available ? "source.Length" : "") + ")");
        }

        [TestMethod]
        [DataRow("", false)]
        [DataRow("public HashSet(int capacity) { }", false)]
        [DataRow("public HashSet(int capacity, IEqualityComparer<T> comparer) { }", true)]
        [DataRow("public HashSet(int capacity) { } public HashSet(int capacity, IEqualityComparer<T> comparer) { }", true)]
        [DataRow("internal HashSet(int capacity, IEqualityComparer<T> comparer) { }", true)]
        [DataRow("private HashSet(int capacity, IEqualityComparer<T> comparer) { }", false)]
        [DataRow("public HashSet(long capacity, IEqualityComparer<T> comparer) { }", false)]
        [DataRow("public HashSet(int capacity, IEqualityComparer<string> comparer) { }", false)]
        [DataRow("public HashSet(int capacity, ref IEqualityComparer<T> comparer) { }", false)]
        [DataRow("public HashSet(int capacity, in IEqualityComparer<T> comparer) { }", false)]
        [DataRow("public HashSet(IEqualityComparer<T> comparer, int capacity) { }", false)]
        public void CapacityAndComparerAreDetectedIndependently(string constructors, bool available)
        {
            var result = Run(Mapper("HashSet<int>", "HashSet<int>"), Framework(constructors));
            AssertValid(result, synthetic: true);
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<int>(" +
                (available ? "source.Count, " : "") + "source.Comparer)");
        }

        [TestMethod]
        [DataRow("int[]", "HashSet<long>", "new[] { 1, 2, 1 }", "source.Length")]
        [DataRow("int[]", "ISet<long>", "new[] { 1, 2, 1 }", "source.Length")]
        [DataRow("int[]", "IReadOnlySet<long>", "new[] { 1, 2, 1 }", "source.Length")]
        [DataRow("List<int>", "HashSet<long>", "new List<int> { 1, 2, 1 }", "source.Count")]
        [DataRow("List<int>", "ISet<long>", "new List<int> { 1, 2, 1 }", "source.Count")]
        [DataRow("List<int>", "IReadOnlySet<long>", "new List<int> { 1, 2, 1 }", "source.Count")]
        [DataRow("ICollection<int>", "HashSet<long>", "new List<int> { 1, 2, 1 }", "source.Count")]
        [DataRow("IReadOnlyCollection<int>", "HashSet<long>", "new List<int> { 1, 2, 1 }", "source.Count")]
        [DataRow("IEnumerable<int>", "HashSet<long>", "new List<int> { 1, 2, 1 }", "")]
        public void ModernSetsRetainContentsAndIndependentIdentity(string sourceType, string targetType, string input, string count)
        {
            var result = Run(Mapper(sourceType, targetType) + @"
public static class Probe
{
    public static bool Run()
    {
        " + sourceType + " source = " + input + @";
        var target = Mapper.Map(source);
        return target is HashSet<long> set && set.SetEquals(new long[] { 1, 2 }) &&
            !object.ReferenceEquals(source, target) && object.ReferenceEquals(set.Comparer, EqualityComparer<long>.Default);
    }
}");
            AssertValid(result);
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<long>(" + count + ")");
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        [DataRow("HashSet<string>")]
        [DataRow("ISet<string>")]
        [DataRow("IReadOnlySet<string>")]
        public void CompatibleComparerAndCapacityArePreserved(string targetType)
        {
            var result = Run(Mapper("HashSet<string>", targetType) + @"
public static class Probe
{
    public static bool Run()
    {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ""A"" };
        var target = (HashSet<string>)Mapper.Map(source);
        target.Add(""B"");
        return target.Contains(""a"") && target.Count == 2 && source.Count == 1 &&
            !object.ReferenceEquals(source, target) && object.ReferenceEquals(source.Comparer, target.Comparer);
    }
}");
            AssertValid(result);
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<string>(source.Count, source.Comparer)");
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void ConvertedSetElementsUseDefaultComparer()
        {
            var result = Run(Mapper("HashSet<int>", "HashSet<long>") + @"
public static class Probe
{
    public static bool Run()
    {
        var source = new HashSet<int> { 1, 2 };
        var target = Mapper.Map(source);
        return target.SetEquals(new long[] { 1, 2 }) && !object.ReferenceEquals(source, target) &&
            object.ReferenceEquals(target.Comparer, EqualityComparer<long>.Default);
    }
}");
            AssertValid(result);
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<long>(source.Count)");
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void UnknownCountSourceIsEnumeratedOnceWithoutCapacityProbe()
        {
            var result = Run(Mapper("IEnumerable<int>", "HashSet<int>") + @"
public static class Probe
{
    private static int enumerations;
    private static IEnumerable<int> Items()
    {
        enumerations++;
        if (enumerations > 1) throw new InvalidOperationException();
        yield return 1;
        yield return 1;
        yield return 2;
    }
    public static bool Run() => Mapper.Map(Items()).SetEquals(new[] { 1, 2 }) && enumerations == 1;
}");
            AssertValid(result);
            StringAssert.Contains(result.Source, "new global::System.Collections.Generic.HashSet<int>()");
            Assert.IsTrue(Execute(result));
        }

        [TestMethod]
        public void CapabilityChangesInvalidateSameDriverOutput()
        {
            var source = Mapper("int[]", "HashSet<int>");
            var firstCompilation = CreateCompilation(source, Framework(""));
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstCompilation.SyntaxTrees.Last(),
                CSharpSyntaxTree.ParseText(Framework("public HashSet(int capacity) { }"), ParseOptions, "Framework.cs"));
            var first = Run(firstCompilation, CreateDriver());
            var second = Run(secondCompilation, first.Driver);
            var fresh = Run(secondCompilation, CreateDriver());
            AssertValid(first, synthetic: true);
            AssertValid(second, synthetic: true);
            AssertValid(fresh, synthetic: true);
            StringAssert.Contains(first.Source, "new global::System.Collections.Generic.HashSet<int>()");
            StringAssert.Contains(second.Source, "new global::System.Collections.Generic.HashSet<int>(source.Length)");
            Assert.AreEqual(fresh.Source, second.Source);
            Assert.AreEqual(second.Source, Run(secondCompilation, second.Driver).Source);
        }

        private static string Mapper(string source, string target) => @"
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper { public static partial " + target + " Map(" + source + " source); }\n";

        // A source-owned framework stand-in deliberately produces only CS0436 conflict warnings.
        // Its signatures vary independently from the host framework and its name stays canonical.
        private static string Framework(string constructors) => @"
namespace System.Collections.Generic
{
    public sealed class HashSet<T> : List<T>
    {
        public HashSet() { }
        public HashSet(IEqualityComparer<T> comparer) { Comparer = comparer; }
        public IEqualityComparer<T> Comparer { get; } = EqualityComparer<T>.Default;
        " + constructors + @"
    }
}";

        private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);

        private static CSharpCompilation CreateCompilation(string source, string? framework = null)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Where(static path => path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Append(MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location));
            var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, ParseOptions, "Mapper.cs") };
            if (framework != null) trees.Add(CSharpSyntaxTree.ParseText(framework, ParseOptions, "Framework.cs"));
            var compilation = CSharpCompilation.Create("HashSetTests_" + Guid.NewGuid().ToString("N"), trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));
            return compilation;
        }

        private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
            new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        private static Result Run(string source, string? framework = null) => Run(CreateCompilation(source, framework), CreateDriver());
        private static Result Run(CSharpCompilation compilation, GeneratorDriver driver)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            return new Result(driver, output, string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(static tree => tree.ToString())));
        }
        private static void AssertValid(Result result, bool synthetic = false)
        {
            var diagnostics = result.Driver.GetRunResult().Diagnostics.Concat(result.Compilation.GetDiagnostics())
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && !(synthetic && diagnostic.Id == "CS0436")).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
            using var stream = new MemoryStream();
            var emit = result.Compilation.Emit(stream);
            Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
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
