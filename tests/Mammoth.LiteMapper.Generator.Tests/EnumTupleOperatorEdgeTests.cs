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
    public sealed class EnumTupleOperatorEdgeTests
    {
        [TestMethod]
        public void ThrowPolicyRejectsDeclaredAndUnknownEnumRuntimeValues()
        {
            var result = Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper(UnmatchedEnumValues = UnmatchedEnumValuePolicy.Throw)]
public static partial class Mapper { public static partial TargetModel Map(SourceModel source); }
public enum SourceColor { Known = 1, Missing = 2 }
public enum TargetColor { Known = 10 }
public sealed class SourceModel { public SourceColor Value { get; set; } }
public sealed class TargetModel { public TargetColor Value { get; set; } }
public static class Probe
{
    public static int Run()
    {
        var result = Mapper.Map(new SourceModel { Value = SourceColor.Known }).Value == TargetColor.Known ? 1 : 0;
        if (Throws(SourceColor.Missing)) result |= 2;
        if (Throws((SourceColor)99)) result |= 4;
        return result;
    }
    private static bool Throws(SourceColor value)
    {
        try { Mapper.Map(new SourceModel { Value = value }); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}
");
            AssertNoDiagnostics(result.RunResult);
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(7, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null),
                "Bits 1/2/4 represent the mapped value, declared-unmatched throw, and unknown-value throw required by Specification 13.1.");
        }

        [TestMethod]
        public void TupleMappingUsesOrdinalPositionsWhenElementNamesDiffer()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial (long destinationFirst, string destinationSecond) Map((int sourceFirst, string sourceSecond) source);
}
public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map((sourceFirst: 7, sourceSecond: ""seven""));
        return mapped.destinationFirst == 7L && mapped.destinationSecond == ""seven"";
    }
}
");
            AssertNoDiagnostics(result.RunResult);
            AssertProbe(result.Compilation, "Tuple element names must not change ordinal mapping semantics (Specification 18.5).");
        }

        [TestMethod]
        public void MarkedConverterPrecedesImplicitOperator()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial int Map(Value source);
    [MappingConverter] private static int Convert(Value source) => 42;
}
public readonly struct Value
{
    public static implicit operator int(Value source) => 7;
}
public static class Probe { public static bool Run() => Mapper.Map(new Value()) == 42; }
");
            AssertNoDiagnostics(result.RunResult);
            AssertProbe(result.Compilation, "Explicitly marked custom conversion must outrank implicit language conversion (Specification 11.1 stages 3 and 8).");
        }

        [TestMethod]
        public void TupleToObjectAndObjectToTupleAreRejected()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target FromTuple((int Value, string Text) source);
    public static partial (int Value, string Text) ToTuple(Source source);
}
public sealed class Source { public int Value { get; set; } public string Text { get; set; } = """"; }
public sealed class Target { public int Value { get; set; } public string Text { get; set; } = """"; }
");
            Assert.AreEqual(2, result.RunResult.Diagnostics.Count(d => d.Id == "LITEMAPPER2004"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var mapper = result.Compilation.GetTypeByMetadataName("Mapper")!;
            Assert.IsNull(mapper.GetMembers("FromTuple").OfType<IMethodSymbol>().Single().PartialImplementationPart, "Tuple-to-object structural mapping is outside 1.0 support (Specification 18.5).");
            Assert.IsNull(mapper.GetMembers("ToTuple").OfType<IMethodSymbol>().Single().PartialImplementationPart, "Object-to-tuple structural mapping is outside 1.0 support (Specification 18.5).");
        }

        private static void AssertNoDiagnostics(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(0, result.Diagnostics.Length, string.Join(Environment.NewLine, result.Diagnostics));
        }

        private static void AssertProbe(Compilation compilation, string reason)
        {
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, reason + Environment.NewLine + string.Join(Environment.NewLine, emitted.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null), reason);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "EnumTupleOperatorEdgeTests",
                new[] { CSharpSyntaxTree.ParseText(source, options) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Fixture input must be valid C#: " + string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
