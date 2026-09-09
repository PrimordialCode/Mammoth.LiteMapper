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
    public class ConverterResultConversionTests
    {
        [TestMethod]
        [DataRow(true, "Checked")]
        [DataRow(false, "Checked")]
        [DataRow(true, "Unchecked")]
        [DataRow(false, "Unchecked")]
        public void NumericPolicyConvertsSelectedResultAndPreservesOverflowSemantics(bool explicitSelection, string policy)
        {
            var result = Run(MemberSource(explicitSelection, "NumericConversion = NumericConversion." + policy,
                "int", "long", "long.Parse(source)", @"
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(new Source { Value = ""12"" }).Value != 12 || Mapper.Calls != 1) return false;
        " + (policy == "Checked" ? @"
        try { Mapper.Map(new Source { Value = ""2147483648"" }); return false; }
        catch (System.OverflowException) { return Mapper.Calls == 2; }" : @"
        return Mapper.Map(new Source { Value = ""2147483648"" }).Value == int.MinValue && Mapper.Calls == 2;") + @"
    }
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        public void NumericResultConversionAppliesAtRootElementAndDictionaryBoundaries()
        {
            var result = Run(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public static partial class Mapper
{
    public static int Calls;
    public static partial int Root(string source);
    public static partial int[] Elements(string[] source);
    public static partial Dictionary<int, int> Dictionary(Dictionary<string, string> source);
    [MappingConverter] private static long Convert(string source) { Calls++; return long.Parse(source); }
}
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Root(""12"") != 12 || Mapper.Calls != 1) return false;
        var elements = Mapper.Elements(new[] { ""13"", ""14"" });
        if (elements.Length != 2 || elements[0] != 13 || elements[1] != 14 || Mapper.Calls != 3) return false;
        var dictionary = Mapper.Dictionary(new Dictionary<string, string> { [""15""] = ""16"" });
        return dictionary.Count == 1 && dictionary[15] == 16 && Mapper.Calls == 5;
    }
} ");
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void EnabledExplicitOperatorConvertsConverterResultOnce(bool explicitSelection)
        {
            var result = Run(MemberSource(explicitSelection, "AllowExplicitOperators = true", "int", "ConvertedValue",
                "new ConvertedValue(int.Parse(source))", @"
public readonly struct ConvertedValue
{
    private readonly int value;
    public static int Conversions;
    public ConvertedValue(int value) { this.value = value; }
    public static explicit operator int(ConvertedValue source) { Conversions++; return source.value + 1; }
}
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { Value = ""12"" }).Value == 13 &&
        Mapper.Calls == 1 && ConvertedValue.Conversions == 1;
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        public void MethodOptionsOverrideTheConverterResultConversionPolicy()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public static partial class Mapper
{
    public static int Calls;
    [MappingOptions(NumericConversion = NumericConversion.Unchecked)]
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source source);
    private static long Convert(string source) { Calls++; return long.Parse(source); }
}
public class Source { public string Value { get; set; } = ""2147483648""; }
public class Target { public int Value { get; set; } }
public static class Probe { public static bool Run() => Mapper.Map(new Source()).Value == int.MinValue && Mapper.Calls == 1; }
");
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        public void NullableNarrowingResultUnderErrorReportsConverterNullabilityMismatch()
        {
            var result = Run(MemberSource(true, "NumericConversion = NumericConversion.Checked", "int", "long?", "null", string.Empty));
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2010"), Diagnostics(result));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void NullableNarrowingResultChecksNullBeforeCastingAndInvokesConverterOnce()
        {
            var result = Run(MemberSource(true,
                "NumericConversion = NumericConversion.Checked, NullableMismatch = NullableMismatchPolicy.Throw",
                "int", "long?", "source == \"missing\" ? null : long.Parse(source)", @"
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(new Source { Value = ""12"" }).Value != 12 || Mapper.Calls != 1) return false;
        try { Mapper.Map(new Source { Value = ""missing"" }); return false; }
        catch (System.InvalidOperationException ex) { if (!ex.Message.Contains(""Value"") || Mapper.Calls != 2) return false; }
        try { Mapper.Map(new Source { Value = ""2147483648"" }); return false; }
        catch (System.OverflowException) { return Mapper.Calls == 3; }
    }
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        public void NullableNarrowingResultPreservesNullForNullableTarget()
        {
            var result = Run(MemberSource(true, "NumericConversion = NumericConversion.Checked", "int?", "long?",
                "source == \"missing\" ? null : long.Parse(source)", @"
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { Value = ""12"" }).Value == 12 &&
        Mapper.Map(new Source { Value = ""missing"" }).Value == null && Mapper.Calls == 2;
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        [DataRow("long", "12L", "")]
        [DataRow("ConvertedValue", "new ConvertedValue()", "public readonly struct ConvertedValue { public static explicit operator int(ConvertedValue source) => 12; }")]
        public void ResultConversionsRemainDisabledByDefault(string resultType, string resultExpression, string extraSource)
        {
            var result = Run(MemberSource(true, string.Empty, "int", resultType, resultExpression, extraSource));
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
                "A selected converter must not silently enable narrowing or explicit operators. " + Diagnostics(result));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"), Diagnostics(result));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void ConverterExceptionPropagatesUnchangedBeforeResultConversion()
        {
            var result = Run(MemberSource(true, "NumericConversion = NumericConversion.Checked", "int", "long",
                "Probe.Fail()", @"
public static class Probe
{
    public static readonly System.Exception Expected = new System.Exception(""converter failed"");
    public static long Fail() => throw Expected;
    public static bool Run()
    {
        try { Mapper.Map(new Source()); return false; }
        catch (System.Exception ex) { return object.ReferenceEquals(Expected, ex) && Mapper.Calls == 1; }
    }
}"));
            Assert.AreEqual(true, Execute(result));
        }

        private static string MemberSource(bool explicitSelection, string options, string targetType,
            string resultType, string resultExpression, string probe)
        {
            return @"using Mammoth.LiteMapper;
[LiteMapper(" + options + @")] public static partial class Mapper
{
    public static int Calls;
    " + (explicitSelection ? "[MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]" : string.Empty) + @"
    public static partial Target Map(Source source);
    " + (explicitSelection ? string.Empty : "[MappingConverter]") + @"
    private static " + resultType + " Convert(string source) { Calls++; return " + resultExpression + @"; }
}
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public " + targetType + @" Value { get; set; } }
" + probe;
        }

        private static string Diagnostics((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            return string.Join(Environment.NewLine, result.RunResult.Diagnostics);
        }

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, Diagnostics(result));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("ConverterResultTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "The regression fixture must be valid before generation: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
