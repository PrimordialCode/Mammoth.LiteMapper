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
    public class ConversionPrecedenceTests
    {
        [TestMethod]
        [DataRow("", "", 13)]
        [DataRow("[MappingConverter] private static ValueTarget Marked(ValueSource source) => new() { Value = 33 };", "", 33)]
        [DataRow("private static ValueTarget MapValue(ValueSource source) => new() { Value = 43 };", "", 43)]
        [DataRow("private static ValueTarget Selected(ValueSource source) => new() { Value = 53 };", "[MapProperty(Target = \"Value\", Source = \"Value\", Use = \"Selected\")]", 53)]
        [DataRow("", "[MapProperty(Target = \"Value\", Source = \"Value\", Use = \"Convert\", ConverterType = typeof(External))]", 23)]
        public void LocalDefaultPrecedesExternalConverterButRespectsEarlierStages(string earlier, string configuration, int expected)
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[LiteMapper, UseMapper(typeof(External))]
public static partial class Mapper
{
    " + configuration + @" public static partial EnvelopeTarget Map(EnvelopeSource source);
    [DefaultMapping] private static ValueTarget Default(ValueSource source) => new() { Value = source.Value + 10 };
    " + earlier + @"
}
public static class External { [MappingConverter] public static ValueTarget Convert(ValueSource source) => new() { Value = source.Value + 20 }; }
public class ValueSource { public int Value { get; set; } }
public class ValueTarget { public int Value { get; set; } }
public class EnvelopeSource { public ValueSource Value { get; set; } = new(); }
public class EnvelopeTarget { public ValueTarget Value { get; set; } = new(); }
public static class Probe { public static bool Run() => Mapper.Map(new() { Value = new() { Value = 3 } }).Value.Value == " + expected + @"; }
");
        }

        [TestMethod]
        public void IndependentRootDeclarationsDoNotDelegateToEachOther()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target First(Source source);
    public static partial Target Second(Source source);
}
public class Source { public int Value { get; set; } }
public class Target { public int Value { get; set; } }
");
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var generated = string.Join(Environment.NewLine, result.RunResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
            Assert.IsFalse(generated.Contains("return First(source)") || generated.Contains("return Second(source)"), generated);
        }

        [TestMethod]
        public void ExplicitAssignableSourcePathPrecedesMarkedConverter()
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Target = ""Value"", Source = ""Value"")] public static partial Target Map(Source source);
    [MappingConverter] private static int Convert(int source) => source + 10;
}
public class Source { public int Value { get; set; } }
public class Target { public int Value { get; set; } }
public static class Probe { public static bool Run() => Mapper.Map(new() { Value = 3 }).Value == 3; }
");
        }

        [TestMethod]
        [DataRow("", "[DefaultMapping]", 23)]
        [DataRow("[MappingConverter]", "[DefaultMapping]", 13)]
        public void ExternalDefaultSelectsWithinMappingStage(string firstAttribute, string secondAttribute, int expected)
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[LiteMapper, UseMapper(typeof(External))] public static partial class Mapper { public static partial int Map(int source); }
public static class External
{
    " + firstAttribute + @" public static int First(int source) => source + 10;
    " + secondAttribute + @" public static int Second(int source) => source + 20;
}
public static class Probe { public static bool Run() => Mapper.Map(3) == " + expected + @"; }
");
        }

        [TestMethod]
        public void ClassRegistrationPrecedesOtherwiseEqualAssemblyRegistration()
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[assembly: UseMapper(typeof(AAssembly))]
[LiteMapper, UseMapper(typeof(ZLocal))] public static partial class Mapper { public static partial int Map(int source); }
public static class AAssembly { [MappingConverter] public static int Convert(int source) => source + 20; }
public static class ZLocal { [MappingConverter] public static int Convert(int source) => source + 10; }
public static class Probe { public static bool Run() => Mapper.Map(3) == 13; }
");
        }

        [TestMethod]
        public void EqualExternalConvertersAcrossContainersAreAmbiguous()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper, UseMapper(typeof(A)), UseMapper(typeof(B))] public static partial class Mapper { public static partial int Map(int source); }
public static class A { [MappingConverter] public static int Convert(int source) => 1; }
public static class B { [MappingConverter] public static int Convert(int source) => 2; }
");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2011"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
        }

        [TestMethod]
        public void RootCollectionConverterPrecedesAutomaticCopy()
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial int[] Map(int[] source);
    [MappingConverter] private static int[] Convert(int[] source) => new[] { 13 };
}
public static class Probe { public static bool Run() => Mapper.Map(new[] { 3 })[0] == 13; }
");
        }

        [TestMethod]
        public void EnumElementsUseEnumPolicyBeforeUnresolvedDiagnostic()
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target[] Map(Source[] source); }
public enum Source { Item = 3 }
public enum Target { Item = 13 }
public static class Probe { public static bool Run() => Mapper.Map(new[] { Source.Item })[0] == Target.Item; }
");
        }

        [TestMethod]
        public void JaggedCollectionsCopyInnerCollectionsInsteadOfUsingIdentity()
        {
            AssertProbe(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial int[][] Map(int[][] source); }
public static class Probe
{
    public static bool Run()
    {
        var source = new[] { new[] { 3 } };
        var target = Mapper.Map(source);
        return !object.ReferenceEquals(source, target) && !object.ReferenceEquals(source[0], target[0]) && target[0][0] == 3;
    }
}
");
        }

        private static void AssertProbe(string source)
        {
            var result = RunGenerator(source);
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        [TestMethod]
        [DataRow("PairTarget Map(PairSource source)")]
        [DataRow("Target Map(Source source)")]
        [DataRow("PairTarget[] Map(PairSource[] source)")]
        public void DefaultsAcrossLocalAndExternalStagesRemainDuplicate(string signature)
        {
            var result = RunGenerator(@"using Mammoth.LiteMapper;
[LiteMapper, UseMapper(typeof(External))] public static partial class Mapper {
public static partial " + signature + @";
public static partial int Healthy(int source);
[DefaultMapping] private static PairTarget Local(PairSource source) => new();
}
public static class External { [DefaultMapping] public static PairTarget Other(PairSource source) => new(); }
public class PairSource { public int Value { get; set; } }
public class PairTarget { public int Value { get; set; } }
public class Source { public PairSource Value { get; set; } = new(); }
public class Target { public PairTarget Value { get; set; } = new(); }");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER3002"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var mapper = result.Compilation.GetTypeByMetadataName("Mapper")!;
            Assert.IsNull(mapper.GetMembers("Map").OfType<IMethodSymbol>().Single().PartialImplementationPart);
            Assert.IsNotNull(mapper.GetMembers("Healthy").OfType<IMethodSymbol>().Single().PartialImplementationPart);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) RunGenerator(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("PrecedenceTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
