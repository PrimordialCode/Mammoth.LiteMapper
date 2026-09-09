using System;
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
    public class Milestone11EnumMappingTests
    {
        [TestMethod]
        public void ByNameEnumMappingGeneratesSwitchAndRejectsUnknownRuntimeValues()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial TargetColor ToTarget(SourceColor source);
}

public enum SourceColor { None = 0, Red = 1, Blue = 2 }
public enum TargetColor { None = 0, Red = 10, Blue = 20 }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "source switch");
            StringAssert.Contains(generated, "SourceColor.Red => TargetColor.Red");
            StringAssert.Contains(generated, "throw new global::System.ArgumentOutOfRangeException(nameof(source), source, \"Unmapped enum value.\")");
            Assert.AreEqual(
                Normalize(File.ReadAllText(Repository.Path("tests/Mammoth.LiteMapper.Generator.Tests/Snapshots/Milestone11EnumMapping.Mapper.g.cs"))),
                Normalize(generated));
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var sourceType = assembly.GetType("SourceColor")!;
            var targetType = assembly.GetType("TargetColor")!;
            var mapped = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.Parse(sourceType, "Blue") });
            Assert.AreEqual(Enum.Parse(targetType, "Blue"), mapped);
            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.ToObject(sourceType, 99) }));
            Assert.IsInstanceOfType<ArgumentOutOfRangeException>(exception.InnerException);
        }

        [TestMethod]
        public void ByValueEnumMappingUsesCheckedNumericConversion()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper(EnumMapping = EnumMappingStrategy.ByValue)]
public static partial class Mapper
{
    public static partial TargetSmall ToTarget(SourceLarge source);
}

public enum SourceLarge : long { Small = 1 }
public enum TargetSmall : byte { Small = 1 }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "checked((TargetSmall)");

            var assembly = Emit(result.Compilation);
            var sourceType = assembly.GetType("SourceLarge")!;
            var mapped = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.Parse(sourceType, "Small") });
            Assert.AreEqual((byte)1, Convert.ToByte(mapped));
            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.ToObject(sourceType, 300) }));
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
        }

        [TestMethod]
        public void FlagsAliasesAndUnmatchedRuntimeValuesFollowByNameRules()
        {
            var result = RunGenerator(@"
using System;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial TargetFlags ToTarget(SourceFlags source);
}

[Flags]
public enum SourceFlags { None = 0, Read = 1, Write = 2, ReadWrite = Read | Write, AlsoRead = Read }
[Flags]
public enum TargetFlags { None = 0, Read = 4, Write = 8, ReadWrite = Read | Write, AlsoRead = Read }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var sourceType = assembly.GetType("SourceFlags")!;
            var targetType = assembly.GetType("TargetFlags")!;
            var mapped = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.Parse(sourceType, "ReadWrite") });
            Assert.AreEqual(Enum.Parse(targetType, "ReadWrite"), mapped);
            var alias = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.Parse(sourceType, "AlsoRead") });
            Assert.AreEqual(Enum.Parse(targetType, "AlsoRead"), alias);
            var exception = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { Enum.ToObject(sourceType, 4) }));
            Assert.IsInstanceOfType<ArgumentOutOfRangeException>(exception.InnerException);
        }

        [TestMethod]
        public void FlagsRuntimeCompositeWithoutDeclaredAliasIsDecomposed()
        {
            var result = RunGenerator(@"
using System;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial TargetFlags ToTarget(SourceFlags source);
}

[Flags]
public enum SourceFlags { None = 0, Read = 1, Write = 2 }
[Flags]
public enum TargetFlags { None = 0, Read = 4, Write = 8 }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var sourceType = assembly.GetType("SourceFlags")!;
            var targetType = assembly.GetType("TargetFlags")!;
            var source = Enum.ToObject(sourceType, 3);
            var mapped = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source });
            Assert.AreEqual(Enum.ToObject(targetType, 12), mapped);
        }

        [TestMethod]
        public void UnmatchedEnumPolicyThrowAndByValueAreHonored()
        {
            var throwPolicy = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmatchedEnumValues = UnmatchedEnumValuePolicy.Throw)] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public enum Source { None = 0, Known = 1, Missing = 2 }
public enum Target { None = 0, Known = 1 }
");
            AssertNoLiteMapperDiagnostics(throwPolicy.RunResult);

            var byValue = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmatchedEnumValues = UnmatchedEnumValuePolicy.ByValue)] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public enum Source { None = 0, Known = 1, Missing = 2 }
public enum Target { None = 0, Known = 1, Other = 2 }
");
            AssertNoLiteMapperDiagnostics(byValue.RunResult);
            StringAssert.Contains(SingleGeneratedSource(byValue.RunResult), "Source.Missing => checked((Target)Source.Missing)");
        }

        [TestMethod]
        public void InvalidEnumMappingsReportMilestoneDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public enum Source { None = 0, Known = 1, Missing = 2 }
public enum Target { None = 0, Known = 1 }
").RunResult, "LITEMAPPER7001");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public enum Source { Zero = 0, Known = 1 }
public enum Target { None = 0, Known = 1 }
").RunResult, "LITEMAPPER7003");

            AssertDiagnostic(RunGenerator(@"
using System;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target ToTarget(Source source); }
[Flags] public enum Source { None = 0, Read = 1, Write = 2 }
[Flags] public enum Target { None = 0, Read = 1 }
").RunResult, "LITEMAPPER7004");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public enum Source { One = 1, AlsoOne = 1 }
public enum Target { One = 1, AlsoOne = 2 }
").RunResult, "LITEMAPPER7002");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(EnumMapping = EnumMappingStrategy.ByValue)] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public enum Source : long { TooLarge = 300 }
public enum Target : byte { One = 1 }
").RunResult, "LITEMAPPER7005");
        }

        [TestMethod]
        public void CustomConverterPrecedesEnumStrategy()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
    [MappingConverter]
    private static Target Convert(Source source) => Target.Special;
}

public enum Source { None = 0 }
public enum Target { Special = 5 }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "return Convert(source);");
            Assert.IsFalse(generated.Contains("source switch", StringComparison.Ordinal), generated);
        }

        protected static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        protected static void AssertNoRuntimeFeatures(string source)
        {
            Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("dynamic", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("Assembly", StringComparison.Ordinal), source);
        }

        protected static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").TrimEnd();
        }

        protected static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id), "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        protected static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(static d => d.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
        }

        protected static GeneratorRun RunGenerator(string source)
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

        protected static Compilation CreateCompilation(string source)
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

        protected static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString())));
            stream.Position = 0;
            return Assembly.Load(stream.ToArray());
        }

        protected sealed class GeneratorRun
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
