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
    public class NumericOperatorConversionTests
    {
        [TestMethod]
        public void NumericPoliciesApplyAtRootMemberAndDictionaryBoundaries()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public static partial class Mapper
{
    public static partial byte Checked(int source);
}
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public static partial class CollectionMapper
{
    public static partial Target Members(Source source);
    public static partial Dictionary<byte, byte> Dictionary(Dictionary<int, int> source);
}
[LiteMapper(NumericConversion = NumericConversion.Checked)]
public static partial class UncheckedMapper
{
    [MappingOptions(NumericConversion = NumericConversion.Unchecked)]
    public static partial byte Map(int source);
}
public class Source { public int Value { get; set; } }
public class Target { public byte Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var mapped = CollectionMapper.Dictionary(new Dictionary<int, int> { [2] = 3 });
        return Mapper.Checked(255) == 255 && UncheckedMapper.Map(256) == 0 &&
            CollectionMapper.Members(new Source { Value = 4 }).Value == 4 && mapped[2] == 3;
    }
}
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("Checked")!.Invoke(null, new object[] { 256 }));
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
        }

        [TestMethod]
        public void ExplicitOperatorsHonorMapperAndMethodOptionsWhileImplicitOperatorsRemainAutomatic()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(AllowExplicitOperators = true)]
public static partial class Mapper
{
    public static partial int Explicit(Value source);
    public static partial long Implicit(ImplicitValue source);
}
[LiteMapper]
public static partial class CollectionMapper
{
    [MappingOptions(AllowExplicitOperators = OptionState.Enabled)]
    public static partial int[] Elements(Value[] source);
}
public readonly struct Value { public static explicit operator int(Value source) => 7; }
public readonly struct ImplicitValue { public static implicit operator long(ImplicitValue source) => 9; }
public static class Probe { public static bool Run() => Mapper.Explicit(new Value()) == 7 && Mapper.Implicit(new ImplicitValue()) == 9 && CollectionMapper.Elements(new[] { new Value() })[0] == 7; }
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        [TestMethod]
        [DataRow("", "", "LITEMAPPER2007")]
        [DataRow("NumericConversion = NumericConversion.Checked", "[MappingOptions(NumericConversion = NumericConversion.ImplicitOnly)]", "LITEMAPPER2007")]
        public void DisabledNarrowingHasCanonicalDiagnostic(string mapperOptions, string methodOptions, string diagnostic)
        {
            var result = RunGenerator("using Mammoth.LiteMapper; [LiteMapper(" + mapperOptions + ")] public static partial class Mapper { " + methodOptions + " public static partial byte Map(int source); }");
            AssertDiagnostic(result.RunResult, diagnostic);
        }

        [TestMethod]
        [DataRow("", "")]
        [DataRow("AllowExplicitOperators = true", "[MappingOptions(AllowExplicitOperators = OptionState.Disabled)]")]
        public void DisabledExplicitOperatorHasCanonicalDiagnostic(string mapperOptions, string methodOptions)
        {
            var result = RunGenerator("using Mammoth.LiteMapper; [LiteMapper(" + mapperOptions + ")] public static partial class Mapper { " + methodOptions + @" public static partial int Map(Value source); }
public readonly struct Value { public static explicit operator int(Value source) => 7; }");
            AssertDiagnostic(result.RunResult, "LITEMAPPER2006");
        }

        [TestMethod]
        public void EqualUserDefinedConversionsAreDiagnosedBeforeEmittingInvalidCast()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(AllowExplicitOperators = true)] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public static explicit operator Target(Source source) => new Target(); }
public class Target { public static explicit operator Target(Source source) => new Target(); }
");
            AssertDiagnostic(result.RunResult, "LITEMAPPER2005");
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void NullableNumericLiftingPreservesNullAndImplicitOperatorsNeedNoOptIn()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)] public static partial class Mapper
{
    public static partial byte? Nullable(int? source);
    public static partial long Implicit(Value source);
    public static partial object Box(int source);
}
public readonly struct Value { public static implicit operator long(Value source) => 19; }
public static class Probe { public static bool Run() => Mapper.Nullable(null) == null && Mapper.Nullable(12) == 12 && Mapper.Implicit(new Value()) == 19 && (int)Mapper.Box(5) == 5; }
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        [TestMethod]
        public void DisabledMemberAndElementConversionsDoNotAddMissingConversionErrors()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target Members(Source source);
    public static partial byte[] Elements(int[] source);
}
public class Source { public int Value { get; set; } }
public class Target { public byte Value { get; set; } }
");
            Assert.AreEqual(2, result.RunResult.Diagnostics.Count(d => d.Id == "LITEMAPPER2007"));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2004"), "A disabled conversion exists and must not also be reported missing.");
        }

        [TestMethod]
        [DataRow("byte Map(int? source)", "LITEMAPPER2001")]
        [DataRow("Target Map(Source source)", "LITEMAPPER2001")]
        [DataRow("byte[] Map(int?[] source)", "LITEMAPPER2003")]
        public void NullableNumericErrorPolicyRejectsUnwrapping(string signature, string diagnostic)
        {
            var result = RunGenerator("using Mammoth.LiteMapper; [LiteMapper(NumericConversion = NumericConversion.Checked)] public static partial class Mapper { public static partial " + signature + @"; }
public class Source { public int? Value { get; set; } }
public class Target { public byte Value { get; set; } }");
            AssertDiagnostic(result.RunResult, diagnostic);
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void NullableNumericThrowChecksBeforeCastingAndEvaluatesMembersOnce()
        {
            var result = RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked, NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper { public static partial byte Map(int? source); }
[LiteMapper(NumericConversion = NumericConversion.Checked, NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class OtherMapper
{
    public static partial Target Members(Source source);
    public static partial byte[] Elements(int?[] source);
}
public class Source { private readonly int? value; public Source(int? value) { this.value = value; } public int Reads; public int? Value { get { Reads++; return value; } } }
public class Target { public byte Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        try { Mapper.Map(null); return false; } catch (ArgumentNullException) { }
        var missing = new Source(null);
        try { OtherMapper.Members(missing); return false; } catch (InvalidOperationException ex) { if (!ex.Message.Contains(""Value"")) return false; }
        try { OtherMapper.Elements(new int?[] { null }); return false; } catch (InvalidOperationException ex) { if (!ex.Message.Contains(""item"")) return false; }
        var present = new Source(12);
        return Mapper.Map(12) == 12 && OtherMapper.Members(present).Value == 12 && present.Reads == 1 && missing.Reads == 1 && OtherMapper.Elements(new int?[] { 12 })[0] == 12;
    }
}
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            Assert.AreEqual(true, Emit(result.Compilation).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id), string.Join(Environment.NewLine, result.Diagnostics));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(0, result.Diagnostics.Length, string.Join(Environment.NewLine, result.Diagnostics));
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) RunGenerator(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("NumericTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            return Assembly.Load(stream.ToArray());
        }
    }
}
