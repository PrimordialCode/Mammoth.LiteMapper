using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class RootSourceConverterPriorityTests
    {
        [TestMethod]
        [DataRow("create", "missing")]
        [DataRow("create", "exact")]
        [DataRow("create", "ambiguous")]
        [DataRow("constructor", "missing")]
        [DataRow("constructor", "exact")]
        [DataRow("constructor", "ambiguous")]
        [DataRow("update", "missing")]
        [DataRow("update", "exact")]
        [DataRow("update", "ambiguous")]
        public void ExplicitRootConverterPrecedesAutomaticSourceMatching(string shape, string sourceMembers)
        {
            var source = @"
using System;
using Mammoth.LiteMapper;
[LiteMapper(NameMatching = NameMatching.IgnoreCase)]
public static partial class Mapper
{
    public static Source? Seen;
    public static int Calls;
    [MapProperty(Target = nameof(Target.FullName), Use = nameof(BuildName))]
    __DECLARATION__
    private static string BuildName(Source source)
    {
        Seen = source;
        Calls++;
        return source.First + "" "" + source.Last;
    }
}
public sealed class Source
{
    public string First { get; set; } = ""Ada"";
    public string Last { get; set; } = ""Lovelace"";
    __MEMBERS__
}
public sealed class Target { __TARGET__ }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        __INVOKE__
        return target.FullName == ""Ada Lovelace"" && Mapper.Calls == 1 && ReferenceEquals(Mapper.Seen, source);
    }
}
".Replace("__DECLARATION__", shape == "update" ? "public static partial void Map(Source source, Target target);" : "public static partial Target Map(Source source);")
                .Replace("__TARGET__", shape == "constructor" ? "public string FullName { get; } public Target(string fullName) { FullName = fullName; }" : "public string FullName { get; set; } = string.Empty;")
                .Replace("__INVOKE__", shape == "update" ? "var target = new Target(); Mapper.Map(source, target);" : "var target = Mapper.Map(source);")
                .Replace("__MEMBERS__", sourceMembers == "missing" ? "" : sourceMembers == "exact"
                    ? "public string FullName => throw new Exception();"
                    : "public string FULLNAME => throw new Exception(); public string fullname => throw new Exception();");

            AssertValidAndRun(RunGenerator(source));
        }

        [TestMethod]
        [DataRow("string", "string.Empty", "\"Ada Lovelace\"")]
        [DataRow("string?", "null", "null")]
        [DataRow("int", "0", "42")]
        [DataRow("int?", "null", "42")]
        public void MissingSourceNameDoesNotSkipConfiguredTarget(string type, string initial, string expected)
        {
            AssertValidAndRun(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper
{
    public static int Calls;
    [MapProperty(Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source source);
    private static __TYPE__ Convert(Source source) { Calls++; return __EXPECTED__; }
}
public sealed class Source { }
public sealed class Target { public __TYPE__ Value { get; set; } = __INITIAL__; }
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source()).Value == __EXPECTED__ && Mapper.Calls == 1;
}
".Replace("__TYPE__", type).Replace("__INITIAL__", initial).Replace("__EXPECTED__", expected)));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void PatchModeRootConverterIgnoresAnUnrelatedNullableMember(bool nullableRoot)
        {
            AssertValidAndRun(RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper(IgnoreNullSourceMembers = true, NullableMismatch = NullableMismatchPolicy.Throw)]
public sealed partial class Mapper
{
    private readonly string prefix;
    public Mapper(string prefix) { this.prefix = prefix; }
    public int Calls;
    [MapProperty(Target = nameof(Target.FullName), Use = nameof(BuildName))]
    public partial Target Map(__SOURCE__ source, Target target);
    private string BuildName(Source source) { Calls++; return prefix + source.First; }
}
public sealed class Source
{
    public string First { get; set; } = ""Ada"";
    public string? FullName => throw new Exception(""Unrelated getter must not be evaluated."");
}
public sealed class Target { public string FullName { get; set; } = ""existing""; }
public static class Probe
{
    public static bool Run()
    {
        var mapper = new Mapper(""name:"");
        var target = new Target();
        if (!ReferenceEquals(mapper.Map(new Source(), target), target) || target.FullName != ""name:Ada"" || mapper.Calls != 1) return false;
        __NULL_CHECK__
        return true;
    }
}
".Replace("__SOURCE__", nullableRoot ? "Source?" : "Source")
                .Replace("__NULL_CHECK__", nullableRoot ? @"
        try { mapper.Map(null, target); return false; }
        catch (ArgumentNullException error) { if (error.ParamName != ""source"" || mapper.Calls != 1 || target.FullName != ""name:Ada"") return false; }
" : "")));
        }

        [TestMethod]
        [DataRow("private static string Convert(int value) => value.ToString();")]
        [DataRow("private static string Convert<T>(Source source) => string.Empty;")]
        public void InvalidRootConverterStillReportsItsSignature(string converter)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Target = nameof(Target.FullName), Use = nameof(Convert))]
    public static partial Target Map(Source source);
    __CONVERTER__
}
public sealed class Source { }
public sealed class Target { public string FullName { get; set; } = string.Empty; }
".Replace("__CONVERTER__", converter));
            CollectionAssert.AreEqual(new[] { "LITEMAPPER2009" }, result.RunResult.Diagnostics.Select(d => d.Id).ToArray());
            Assert.IsFalse(result.RunResult.GeneratedTrees.Any(tree => tree.ToString().Contains("partial Target Map(")));
        }

        [TestMethod]
        public void ExplicitSourcePathStillSuppliesTheMemberToTheExternalConverter()
        {
            AssertValidAndRun(RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Name), Target = nameof(Target.FullName), Use = nameof(Converters.BuildName), ConverterType = typeof(Converters))]
    public static partial Target Map(Source source);
}
public static class Converters
{
    public static readonly Exception Failure = new Exception(""converter"");
    public static int Calls;
    public static string BuildName(string name) { Calls++; if (name == ""fail"") throw Failure; return ""Name: "" + name; }
}
public sealed class Source { public string Name { get; set; } = ""Ada""; }
public sealed class Target { public string FullName { get; set; } = string.Empty; }
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(new Source()).FullName != ""Name: Ada"" || Converters.Calls != 1) return false;
        try { Mapper.Map(new Source { Name = ""fail"" }); return false; }
        catch (Exception error) { return ReferenceEquals(error, Converters.Failure) && Converters.Calls == 2; }
    }
}
"));
        }

        private static void AssertValidAndRun(GeneratorRun result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var warningsAndErrors = result.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, warningsAndErrors.Length, string.Join(Environment.NewLine, warningsAndErrors.Select(d => d.ToString())));
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null), SingleGeneratedSource(result.RunResult));
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "RootSourceConverterPriorityTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                References(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static MetadataReference[] References()
        {
            return AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) })
                .ToArray();
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success,
                string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
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
    }
}
