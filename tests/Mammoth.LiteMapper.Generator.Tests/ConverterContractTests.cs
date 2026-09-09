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
    public class ConverterContractTests
    {
        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void NullableConverterResultCannotSilentlySatisfyNonNullTarget(bool explicitSelection)
        {
            var result = Run(ConverterSource(explicitSelection, "Error", "string", "string?", "null", string.Empty));
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2010"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void ThrowPolicyChecksConverterResultOnceAndReportsTargetPath(bool explicitSelection)
        {
            var result = Run(ConverterSource(explicitSelection, "Throw", "string", "string?", "null", @"
public static class Probe
{
    public static bool Run()
    {
        try { Mapper.Map(new Source()); return false; }
        catch (System.InvalidOperationException ex) { return ex.Message.Contains(""Value"") && Mapper.Calls == 1; }
    }
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        [DataRow("string", "string", "\"converted\"", false)]
        [DataRow("string?", "string?", "null", true)]
        public void CompatibleConverterNullabilityRemainsUsable(string targetType, string resultType, string resultExpression, bool expectNull)
        {
            var result = Run(ConverterSource(true, "Error", targetType, resultType, resultExpression, @"
public static class Probe { public static bool Run() => Mapper.Map(new Source()).Value " + (expectNull ? "== null" : "== \"converted\"") + " && Mapper.Calls == 1; }"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        [DataRow(true, "int")]
        [DataRow(false, "int")]
        [DataRow(true, "long")]
        [DataRow(false, "long")]
        public void NullableValueConverterResultsReportTheConverterNullabilityDiagnostic(bool explicitSelection, string targetType)
        {
            var result = Run(ValueConverterSource(explicitSelection, "Error", targetType, string.Empty));
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2010"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        [DataRow(true, "int")]
        [DataRow(false, "int")]
        [DataRow(true, "long")]
        [DataRow(false, "long")]
        public void NullableValueConverterThrowChecksBeforeImplicitWideningAndEvaluatesOnce(bool explicitSelection, string targetType)
        {
            var result = Run(ValueConverterSource(explicitSelection, "Throw", targetType, @"
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(new Source { Value = ""present"" }).Value != 12 || Mapper.Calls != 1) return false;
        try { Mapper.Map(new Source { Value = ""missing"" }); return false; }
        catch (System.InvalidOperationException ex) { return ex.Message.Contains(""Value"") && Mapper.Calls == 2; }
    }
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        public void NullableValueConverterResultCanLiftToNullableTargetWithoutLosingNull()
        {
            var result = Run(ValueConverterSource(true, "Error", "long?", @"
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { Value = ""present"" }).Value == 12 &&
        Mapper.Map(new Source { Value = ""missing"" }).Value == null && Mapper.Calls == 2;
}"));
            Assert.AreEqual(true, Execute(result));
        }

        [TestMethod]
        public void NullableValueConverterResultWorksAtRootAndCollectionElementBoundaries()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    public static int Calls;
    public static partial long Root(string source);
    public static partial long[] Elements(string[] source);
    [MappingConverter] private static int? Convert(string source) { Calls++; return source == ""missing"" ? null : 12; }
}
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Root(""present"") != 12 || Mapper.Elements(new[] { ""present"" })[0] != 12 || Mapper.Calls != 2) return false;
        try { Mapper.Root(""missing""); return false; }
        catch (System.InvalidOperationException) { if (Mapper.Calls != 3) return false; }
        try { Mapper.Elements(new[] { ""missing"" }); return false; }
        catch (System.InvalidOperationException ex) { return ex.Message.Contains(""item"") && Mapper.Calls == 4; }
    }
}");
            Assert.AreEqual(true, Execute(result));
        }

        private static string ValueConverterSource(bool explicitSelection, string policy, string targetType, string probe)
        {
            return @"using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy." + policy + @")] public static partial class Mapper
{
    public static int Calls;
    " + (explicitSelection ? "[MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]" : string.Empty) + @"
    public static partial Target Map(Source source);
    " + (explicitSelection ? string.Empty : "[MappingConverter]") + @"
    private static int? Convert(string source) { Calls++; return source == ""missing"" ? null : 12; }
}
public class Source { public string Value { get; set; } = ""present""; }
public class Target { public " + targetType + @" Value { get; set; } }
" + probe;
        }

        [TestMethod]
        public void InaccessibleDefaultForRequestedPairIsNotReplacedByAFallback()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper, UseMapper(typeof(External))] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public int Value { get; set; } }
public static class External
{
    [DefaultMapping] private static int Preferred(string source) => 7;
    public static int Other(string source) => 9;
}");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER3003"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void IncompatibleDefaultForRequestedPairIsDiagnosed()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target Map(Source source);
    [DefaultMapping] private static int Preferred(ref string source) => 7;
}
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public int Value { get; set; } }");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER3003"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void UnrelatedInaccessibleDefaultDoesNotPoisonAUsablePair()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper, UseMapper(typeof(External))] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public int Value { get; set; } }
public static class External
{
    [DefaultMapping] private static int Unrelated(System.DateTime source) => 7;
    public static int Convert(string source) => 9;
}
public static class Probe { public static int Run() => Mapper.Map(new Source()).Value; }");
            Assert.AreEqual(9, Execute(result));
        }

        private static string ConverterSource(bool explicitSelection, string policy, string targetType, string resultType, string resultExpression, string probe)
        {
            return @"using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy." + policy + @")] public static partial class Mapper
{
    public static int Calls;
    " + (explicitSelection ? "[MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]" : string.Empty) + @"
    public static partial Target Map(Source source);
    " + (explicitSelection ? string.Empty : "[MappingConverter]") + @"
    private static " + resultType + " Convert(string source) { Calls++; return " + resultExpression + @"; }
}
public class Source { public string Value { get; set; } = ""source""; }
public class Target { public " + targetType + @" Value { get; set; } = ""initial""; }
" + probe;
        }

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("ConverterTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
