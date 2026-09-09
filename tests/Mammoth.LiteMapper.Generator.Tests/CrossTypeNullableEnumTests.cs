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
    public sealed class CrossTypeNullableEnumTests
    {
        [TestMethod]
        [DataRow("root", true)]
        [DataRow("member", true)]
        [DataRow("element", true)]
        [DataRow("root", false)]
        [DataRow("member", false)]
        [DataRow("element", false)]
        public void NullableTargetsPreserveNullAndUseEnumNamesAfterLifting(string boundary, bool nullableSource)
        {
            var check = boundary == "root"
                ? "Mapper.Map(From.Ready) == To.Ready && Mapper.Map(From.None) == To.None"
                : boundary == "member"
                    ? "Mapper.Map(new Source { Value = From.Ready }).Value == To.Ready && Mapper.Map(new Source { Value = From.None }).Value == To.None"
                    : "Mapper.Map(new " + (nullableSource ? "From?" : "From") + "[] { From.None, From.Ready }) is var values && values.Length == 2 && values[0] == To.None && values[1] == To.Ready";
            if (nullableSource)
            {
                check += boundary == "root" ? " && Mapper.Map(null) == null"
                    : boundary == "member" ? " && Mapper.Map(new Source()).Value == null"
                    : " && Mapper.Map(new From?[] { null, From.Ready }) is var nullableValues && nullableValues.Length == 2 && nullableValues[0] == null && nullableValues[1] == To.Ready";
            }

            AssertProbe(Run(BoundarySource(boundary, nullableSource, true, string.Empty, @"
public static class Probe
{
    public static bool Run()
    {
        if (!(" + check + @")) return false;
        try { " + Call(boundary, "(From)99", nullableSource) + @"; return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}")));
        }

        [TestMethod]
        [DataRow("root")]
        [DataRow("member")]
        [DataRow("element")]
        public void ThrowPolicyChecksNullableEnumBeforeByNameMapping(string boundary)
        {
            var success = Call(boundary, "From.Ready", true) + (boundary == "root" ? string.Empty : boundary == "member" ? ".Value" : "[0]");
            var exceptionType = boundary == "root" ? "ArgumentNullException" : "InvalidOperationException";
            var pathCheck = boundary == "root" ? "ex.ParamName == \"source\"" : "ex.Message.Contains(\"" + (boundary == "member" ? "Value" : "item") + "\")";
            AssertProbe(Run(BoundarySource(boundary, true, false, "NullableMismatch = NullableMismatchPolicy.Throw", @"
public static class Probe
{
    public static bool Run()
    {
        if (" + success + @" != To.Ready) return false;
        try { " + Call(boundary, "null", true) + @"; return false; }
        catch (" + exceptionType + @" ex) { if (!(" + pathCheck + @")) return false; }
        try { " + Call(boundary, "(From)99", true) + @"; return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}")));
        }

        [TestMethod]
        [DataRow("root", "LITEMAPPER2001")]
        [DataRow("member", "LITEMAPPER2001")]
        [DataRow("element", "LITEMAPPER2003")]
        public void ErrorPolicyRejectsNullableEnumMismatchBeforeEmittingImplementation(string boundary, string expectedDiagnostic)
        {
            var result = Run(BoundarySource(boundary, true, false, string.Empty, string.Empty));
            Assert.IsTrue(result.Result.Diagnostics.Any(d => d.Id == expectedDiagnostic), string.Join(Environment.NewLine, result.Result.Diagnostics));
            Assert.AreEqual(0, result.Result.GeneratedTrees.Length, "A nullable enum mismatch must not generate a partial implementation.");
            Assert.IsFalse(result.Result.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"));
        }

        [TestMethod]
        [DataRow("To")]
        [DataRow("To?")]
        public void PatchSkipsNullAndConvertsPresentEnumByName(string targetType)
        {
            AssertProbe(Run(Preamble + @"
[LiteMapper(IgnoreNullSourceMembers = true)] public static partial class Mapper
{
    public static partial Target Apply(Source source, Target destination);
}
public class Source { public From? Value { get; set; } }
public class Target { public " + targetType + @" Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var target = new Target { Value = To.Ready };
        if (!object.ReferenceEquals(target, Mapper.Apply(new Source(), target)) || target.Value != To.Ready) return false;
        Mapper.Apply(new Source { Value = From.None }, target);
        if (target.Value != To.None) return false;
        Mapper.Apply(new Source { Value = From.Ready }, target);
        return target.Value == To.Ready;
    }
}"));
        }

        [TestMethod]
        public void NullableEnumSourcePathIsEvaluatedOnceBeforeConversion()
        {
            AssertProbe(Run(Preamble + @"
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class Mapper
{
    [MapProperty(Source = ""Data.Value"", Target = nameof(Target.Value))]
    public static partial Target Map(Source source);
}
public class Source
{
    private readonly Data data;
    public int Reads;
    public Source(From? value) { data = new Data(value); }
    public Data Data { get { Reads++; return data; } }
    public int ValueReads() => data.Reads;
}
public class Data
{
    private readonly From? value;
    public int Reads;
    public Data(From? value) { this.value = value; }
    public From? Value { get { Reads++; return value; } }
}
public class Target { public To Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var present = new Source(From.Ready);
        if (Mapper.Map(present).Value != To.Ready || present.Reads != 1 || present.ValueReads() != 1) return false;
        var missing = new Source(null);
        try { Mapper.Map(missing); return false; }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains(""Data.Value"") && missing.Reads == 1 && missing.ValueReads() == 1;
        }
    }
}"));
        }

        [TestMethod]
        public void NullableEnumTemporaryDoesNotCollideWithSourceParameter()
        {
            AssertProbe(Run(Preamble + @"
[LiteMapper] public static partial class Mapper { public static partial To? Map(From? __nullableEnumValue); }
public static class Probe { public static bool Run() => Mapper.Map(From.Ready) == To.Ready && Mapper.Map(null) == null; }
"));
        }

        private const string Preamble = "using System; using Mammoth.LiteMapper; public enum From { None = 0, Ready = 1 } public enum To { None = 0, Ready = 9 } ";

        private static string BoundarySource(string boundary, bool nullableSource, bool nullableTarget, string options, string probe)
        {
            var sourceType = nullableSource ? "From?" : "From";
            var targetType = nullableTarget ? "To?" : "To";
            var declaration = boundary == "root" ? targetType + " Map(" + sourceType + " source)"
                : boundary == "element" ? targetType + "[] Map(" + sourceType + "[] source)"
                : "Target Map(Source source)";
            return Preamble + "[LiteMapper(" + options + ")] public static partial class Mapper { public static partial " + declaration + "; } " +
                "public class Source { public " + sourceType + " Value { get; set; } } public class Target { public " + targetType + " Value { get; set; } } " + probe;
        }

        private static string Call(string boundary, string value, bool nullableSource)
        {
            return boundary == "root" ? "Mapper.Map(" + value + ")"
                : boundary == "member" ? "Mapper.Map(new Source { Value = " + value + " })"
                : "Mapper.Map(new " + (nullableSource ? "From?" : "From") + "[] { " + value + " })";
        }

        private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("CrossTypeNullableEnumTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }

        private static void AssertProbe((GeneratorDriverRunResult Result, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.Result.Diagnostics.Length, string.Join(Environment.NewLine, result.Result.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }
    }
}
