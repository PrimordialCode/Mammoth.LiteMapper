using System;
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
    public sealed class DeclarationContractEdgeTests
    {
        [TestMethod]
        [DataRow("mapper", "(NameMatching)1", true)]
        [DataRow("mapper", "(NameMatching)0", true)]
        [DataRow("mapper", "(NameMatching)999", false)]
        [DataRow("method", "(NameMatching)1", true)]
        [DataRow("method", "(NameMatching)0", true)]
        [DataRow("method", "(NameMatching)999", false)]
        [DataRow("assembly", "(NameMatching)1", true)]
        [DataRow("assembly", "(NameMatching)0", true)]
        [DataRow("assembly", "(NameMatching)999", false)]
        [DataRow("mapper", "(NameMatching)(1 + 0)", true)]
        public void EnumConfigurationUsesTheDefinedValueRatherThanItsConstantSyntax(string scope, string expression, bool valid)
        {
            var configuration = "NameMatching = " + expression;
            var result = Run("using Mammoth.LiteMapper; " +
                (scope == "assembly" ? "[assembly: LiteMapperDefaults(" + configuration + ")] " : string.Empty) +
                "[LiteMapper" + (scope == "mapper" ? "(" + configuration + ")" : string.Empty) + "] public static partial class Mapper { " +
                (scope == "method" ? "[MappingOptions(" + configuration + ")] " : string.Empty) +
                "public static partial Target Map(Source source); } " + Models +
                "public static class Probe { public static int Run() => Mapper.Map(new Source { Value = 3 }).Value; }");
            if (valid)
            {
                Assert.AreEqual(3, Execute(result));
            }
            else
            {
                Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == (scope == "method" ? "LITEMAPPER0009" : "LITEMAPPER0008")),
                    string.Join(Environment.NewLine, result.RunResult.Diagnostics));
                Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ValidIgnoreCaseCastOverridesAnExactMatchingParent(bool methodOverride)
        {
            var result = Run("using Mammoth.LiteMapper; [assembly: LiteMapperDefaults(NameMatching = NameMatching.Exact)] " +
                "[LiteMapper(NameMatching = " + (methodOverride ? "NameMatching.Exact" : "(NameMatching)3") + ")] public static partial class Mapper { " +
                (methodOverride ? "[MappingOptions(NameMatching = (NameMatching)3)] " : string.Empty) +
                "public static partial Target Map(Source source); } " +
                "public class Source { public int value { get; set; } } public class Target { public int Value { get; set; } } " +
                "public static class Probe { public static int Run() => Mapper.Map(new Source { value = 3 }).Value; }");
            Assert.AreEqual(3, Execute(result));
        }

        [TestMethod]
        [DataRow("class")]
        [DataRow("struct")]
        [DataRow("record")]
        [DataRow("record struct")]
        [DataRow("interface")]
        public void NestedMapperPreservesTheContainingDeclarationKind(string kind)
        {
            var result = Run("using Mammoth.LiteMapper; public partial " + kind + " Outer { " +
                "[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); } } " + Models +
                "public static class Probe { public static int Run() => Outer.Mapper.Map(new Source { Value = 3 }).Value; }");
            Assert.AreEqual(3, Execute(result));
        }

        [TestMethod]
        public void EveryLevelOfAMixedContainingChainIsPreserved()
        {
            var result = Run("using Mammoth.LiteMapper; public partial record Outer { public partial interface Inner { " +
                "[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); } } } " + Models +
                "public static class Probe { public static int Run() => Outer.Inner.Mapper.Map(new Source { Value = 3 }).Value; }");
            Assert.AreEqual(3, Execute(result));
        }

        [TestMethod]
        public void GenericContainingTypeStillRejectsAnOtherwiseValidMapper()
        {
            var result = Run("using Mammoth.LiteMapper; public partial class Outer<T> { " +
                "[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); } } " + Models);
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER0003"));
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        [DataRow("record")]
        [DataRow("struct")]
        [DataRow("interface")]
        public void SupportedContainingKindsDoNotBecomeSupportedMapperKinds(string kind)
        {
            var result = Run("using Mammoth.LiteMapper; [LiteMapper] public partial " + kind + " Mapper { }", allowInvalidAttributeTarget: true);
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER0002"));
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        private const string Models = "public class Source { public int Value { get; set; } } public class Target { public int Value { get; set; } } ";

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source, bool allowInvalidAttributeTarget = false)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("DeclarationContractEdgeTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795" &&
                !(allowInvalidAttributeTarget && d.Id == "CS0592")).ToArray();
            Assert.AreEqual(0, inputErrors.Length, "The fixture must be valid apart from the deliberately invalid attribute target or missing partial implementation. " +
                string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
