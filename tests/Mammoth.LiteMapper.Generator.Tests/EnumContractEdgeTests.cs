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
    public sealed class EnumContractEdgeTests
    {
        [TestMethod]
        [DataRow("root", "int", "4", "8", "16")]
        [DataRow("member", "int", "4", "8", "16")]
        [DataRow("element", "int", "4", "8", "16")]
        [DataRow("root", "sbyte", "sbyte.MinValue", "1", "-126")]
        public void NamedCompositeMustAgreeWithAtomicMappingAndPreserveIndependentMethods(string boundary, string targetType, string read, string write, string both)
        {
            var declaration = boundary == "root" ? "To Map(From source)"
                : boundary == "member" ? "Target Map(Source source)"
                : "To?[] Map(From?[] source)";
            var result = Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial " + declaration + @";
    public static partial int Healthy(int source);
}
[Flags] public enum From { None = 0, Read = 1, Write = 2, Both = Read | Write }
[Flags] public enum To : " + targetType + " { None = 0, Read = " + read + ", Write = " + write + ", Both = " + both + @" }
public class Source { public From? Value { get; set; } }
public class Target { public To? Value { get; set; } }
");
            Assert.IsTrue(result.Result.Diagnostics.Any(d => d.Id == "LITEMAPPER7002"), string.Join(Environment.NewLine, result.Result.Diagnostics));
            Assert.IsFalse(result.Result.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"));
            var mapper = result.Compilation.GetTypeByMetadataName("Mapper")!;
            var invalid = mapper.GetMembers("Map").OfType<IMethodSymbol>().Single(m => m.PartialDefinitionPart == null);
            var healthy = mapper.GetMembers("Healthy").OfType<IMethodSymbol>().Single(m => m.PartialDefinitionPart == null);
            Assert.IsNull(invalid.PartialImplementationPart, "The inconsistent composite must not receive an implementation.");
            Assert.IsNotNull(healthy.PartialImplementationPart, "A bad enum mapping must not suppress an independent mapping.");
            Assert.IsFalse(result.Compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795"),
                string.Join(Environment.NewLine, result.Compilation.GetDiagnostics()));
        }

        [TestMethod]
        [DataRow("int", "4", "8", "12")]
        [DataRow("sbyte", "sbyte.MinValue", "1", "-127")]
        [DataRow("long", "long.MinValue", "1", "long.MinValue + 1")]
        public void ConsistentNamedCompositeMapsTheSameAsItsAtomicExpression(string targetType, string read, string write, string both)
        {
            AssertProbe(Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial To? Map(From? source); }
[Flags] public enum From { None = 0, Read = 1, Write = 2, Both = Read | Write }
[Flags] public enum To : " + targetType + " { None = 0, Read = " + read + ", Write = " + write + ", Both = " + both + @" }
public static class Probe
{
    public static bool Run() => Mapper.Map(null) == null && Mapper.Map(From.Both) == To.Both &&
        Mapper.Map(From.Read | From.Write) == (To.Read | To.Write);
}"));
        }

        [TestMethod]
        public void DeclaredCompositeNeedsOnlyItsMappedAtomicFlags()
        {
            AssertProbe(Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial To Map(From source); }
[Flags] public enum From { None = 0, Read = 1, Write = 2, Both = Read | Write }
[Flags] public enum To { None = 0, Read = 4, Write = 8 }
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(From.Both) != (To.Read | To.Write)) return false;
        try { Mapper.Map((From)4); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}"));
        }

        [TestMethod]
        [DataRow("EnumMapping = EnumMappingStrategy.ByValue")]
        [DataRow("UnmatchedEnumValues = UnmatchedEnumValuePolicy.ByValue")]
        public void UncheckedEnumPolicyPermitsDeclaredOverflowAndWrapsTheValue(string strategy)
        {
            AssertProbe(Run(@"
using Mammoth.LiteMapper;
[LiteMapper(" + strategy + @", EnumNumericConversion = EnumNumericConversion.Unchecked)]
public static partial class Mapper { public static partial To Map(From source); }
public enum From : long { None = 0, Large = 300 }
public enum To : byte { None = 0 }
public static class Probe { public static bool Run() => (byte)Mapper.Map(From.Large) == 44 && Mapper.Map(From.None) == To.None; }
"));
        }

        [TestMethod]
        public void CheckedDeclaredEnumOverflowRemainsADiagnostic()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(EnumMapping = EnumMappingStrategy.ByValue, EnumNumericConversion = EnumNumericConversion.Checked)]
public static partial class Mapper { public static partial To Map(From source); }
public enum From : long { Large = 300 }
public enum To : byte { None = 0 }
");
            AssertInvalid(result, "LITEMAPPER7005");
        }

        [TestMethod]
        [DataRow("sbyte", "sbyte.MinValue")]
        [DataRow("short", "short.MinValue")]
        [DataRow("int", "int.MinValue")]
        [DataRow("long", "long.MinValue")]
        public void SignedHighBitFlagsAreMappedWithoutNumericOverflow(string underlyingType, string highBit)
        {
            AssertProbe(Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial To Map(From source); }
[Flags] public enum From : " + underlyingType + " { None = 0, Low = 1, High = " + highBit + @" }
[Flags] public enum To { None = 0, Low = 2, High = 4 }
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(From.High) != To.High || Mapper.Map(From.High | From.Low) != (To.High | To.Low)) return false;
        try { Mapper.Map(From.High | (From)2); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}"));
        }

        [TestMethod]
        [DataRow("", "LITEMAPPER7001")]
        [DataRow("UnmatchedEnumValues = UnmatchedEnumValuePolicy.ByValue", "LITEMAPPER7002")]
        public void MissingAliasDoesNotSilentlyBorrowAnotherAliasesTarget(string options, string diagnostic)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(" + options + @")] public static partial class Mapper { public static partial To Map(From source); }
public enum From { Known = 1, Missing = 1 }
public enum To { Known = 9 }
");
            AssertInvalid(result, diagnostic);
        }

        [TestMethod]
        public void MatchingSourceAliasesAndTargetAliasesRemainValid()
        {
            AssertProbe(Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial To Map(From source); }
public enum From { Known = 1, Alias = 1 }
public enum To { Known = 9, Alias = 9, TargetOnlyAlias = 9 }
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(From.Known) != To.Known || Mapper.Map(From.Alias) != To.Alias) return false;
        try { Mapper.Map((From)2); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}"));
        }

        [TestMethod]
        [DataRow(300, "byte", "44")]
        [DataRow(255, "sbyte", "-1")]
        public void UncheckedAliasComparisonUsesTheConvertedTargetValue(int sourceValue, string targetType, string targetValue)
        {
            AssertProbe(Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmatchedEnumValues = UnmatchedEnumValuePolicy.ByValue, EnumNumericConversion = EnumNumericConversion.Unchecked)]
public static partial class Mapper { public static partial To Map(From source); }
public enum From : long { Known = " + sourceValue + ", Missing = " + sourceValue + @" }
public enum To : " + targetType + " { Known = " + targetValue + @" }
public static class Probe { public static bool Run() => Mapper.Map(From.Known) == To.Known && Mapper.Map(From.Missing) == To.Known; }
"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FlagsTemporaryCannotCollideWithTheMappingParameter(bool nullable)
        {
            var suffix = nullable ? "?" : string.Empty;
            AssertProbe(Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial To" + suffix + " Map(From" + suffix + @" enumValue); }
[Flags] public enum From { None = 0, Read = 1, Write = 2 }
[Flags] public enum To { None = 0, Read = 4, Write = 8 }
public static class Probe
{
    public static bool Run() => Mapper.Map(From.Read | From.Write) == (To.Read | To.Write)" + (nullable ? " && Mapper.Map(null) == null" : string.Empty) + @";
}"));
        }

        [TestMethod]
        public void UnknownEnumSourcePathEvaluatesTheGetterOnce()
        {
            AssertProbe(Run(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = ""Data.Value"", Target = nameof(Target.Value))]
    public static partial Target Map(Source source);
}
public enum From { None = 0, Ready = 1 }
public enum To { None = 0, Ready = 9 }
public class Source
{
    private readonly Data data = new Data();
    public int Reads;
    public Data Data { get { Reads++; return data; } }
    public int ValueReads() => data.Reads;
}
public class Data
{
    public int Reads;
    public From Value { get { Reads++; return (From)99; } }
}
public class Target { public To Value { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        try { Mapper.Map(source); return false; }
        catch (ArgumentOutOfRangeException ex)
        {
            return source.Reads == 1 && source.ValueReads() == 1 && ex.ActualValue is From actual && actual == (From)99;
        }
    }
}"));
        }

        private static void AssertInvalid((GeneratorDriverRunResult Result, Compilation Compilation) result, string diagnostic)
        {
            Assert.IsTrue(result.Result.Diagnostics.Any(d => d.Id == diagnostic), string.Join(Environment.NewLine, result.Result.Diagnostics));
            Assert.IsFalse(result.Result.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"));
            Assert.AreEqual(0, result.Result.GeneratedTrees.Length, "The invalid enum mapping must have no implementation.");
        }

        private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("EnumContractEdgeTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
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
