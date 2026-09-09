using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class MemberConfigurationDiagnosticTests
    {
        [TestMethod]
        [DataRow("Missing", "Missing")]
        [DataRow("Child.Missing.Value", "Missing")]
        public void InvalidSourcePathIdentifiesTheFailingSegment(string path, string segment)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = """ + path + @""", Target = nameof(Target.Value))]
    public static partial Target Map(Source source);
}
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            var diagnostic = AssertCanonicalDiagnostic(result, "LITEMAPPER1006");
            StringAssert.Contains(diagnostic.GetMessage(), segment,
                "A source-path diagnostic must identify the segment the consumer must correct.");
        }

        [TestMethod]
        [DataRow("[MapProperty(Source = nameof(Source.Value), Target = \"Nested.Value\")]", "LITEMAPPER1007")]
        [DataRow("[MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value))][MapProperty(Source = nameof(Source.Other), Target = nameof(Target.Value))]", "LITEMAPPER1008")]
        [DataRow("[UseTargetDefault(nameof(Target.Value))]", "LITEMAPPER1014")]
        [DataRow("[IgnoreSource(\"Missing\")]", "LITEMAPPER1015")]
        [DataRow("[IgnoreTarget(\"Missing\")]", "LITEMAPPER1015")]
        public void InvalidMemberConfigurationUsesItsCanonicalDiagnostic(string attributes, string expectedId)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    " + attributes + @"
    public static partial Target Map(Source source);
}
public sealed class Source { public int Value { get; set; } public int Other { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertCanonicalDiagnostic(result, expectedId);
        }

        [TestMethod]
        [DataRow("private static void Convert(int value) { }")]
        [DataRow("private static string Convert<T>(int value) => value.ToString();")]
        public void ExplicitConverterWithUnsupportedSignatureReportsInvalidConverterSignature(string converter)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source source);
    " + converter + @"
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public string Value { get; set; } = string.Empty; }
");

            AssertCanonicalDiagnostic(result, "LITEMAPPER2009");
        }

        [TestMethod]
        public void EqualPrecedenceConvertersReportAmbiguousConverter()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target Map(Source source);
    [MappingConverter] private static string One(int value) => value.ToString();
    [MappingConverter] private static string Two(int value) => value.ToString();
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public string Value { get; set; } = string.Empty; }
");

            AssertCanonicalDiagnostic(result, "LITEMAPPER2011");
        }

        [TestMethod]
        public void ExplicitlyNamedRegisteredConverterDoesNotRequireConverterType()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[UseMapper(typeof(ExternalConversions))]
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(ExternalConversions.Convert))]
    public static partial Target Map(Source source);
}
public static class ExternalConversions
{
    public static string Convert(int value) => value.ToString();
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public string Value { get; set; } = string.Empty; }
");

            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                "Specification 5.7 permits explicit Use selection through registered resolution without ConverterType: " +
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            StringAssert.Contains(result.RunResult.GeneratedTrees.Single().GetText().ToString(),
                "ExternalConversions.Convert(source.Value)",
                "The explicit registered method must be invoked in the generated implementation.");
        }

        [TestMethod]
        [DataRow("List<int>", "public List<int> Items { get; } = new List<int>();", "LITEMAPPER4004")]
        [DataRow("List<int>", "public List<int> Items { get; init; } = new List<int>();", "LITEMAPPER5004")]
        [DataRow("int", "public int Items { get; }", "LITEMAPPER1009")]
        public void UnwritableUpdateMemberReportsTheSpecificReason(string sourceMemberType, string targetMember, string expectedId)
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial void Map(Source source, Target target);
}
public sealed class Source { public " + sourceMemberType + @" Items { get; set; } = default!; }
public sealed class Target { " + targetMember + @" }
");

            AssertCanonicalDiagnostic(result, expectedId);
        }

        [TestMethod]
        [DataRow("int[]")]
        [DataRow("IList<int>")]
        public void TopLevelArrayUpdateReportsItsSpecificCollectionDiagnostic(string sourceType)
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial void Map(" + sourceType + @" source, int[] target);
}
");

            AssertCanonicalDiagnostic(result, "LITEMAPPER4005");
        }

        private static Diagnostic AssertCanonicalDiagnostic(GeneratorRun result, string expectedId)
        {
            var matches = result.RunResult.Diagnostics.Where(d => d.Id == expectedId).ToArray();
            Assert.IsTrue(matches.Length > 0,
                "Specification 20.2 reserves a stable diagnostic for this error. Expected " + expectedId +
                "; actual: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.AreEqual(1, matches.Length,
                "A failed selection must report its canonical error once, not once per unsuccessful lookup: " +
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var diagnostic = matches[0];
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.IsTrue(diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable),
                "Fatal member/converter/collection errors must not be suppressible into an invalid implementation.");
            Assert.IsTrue(diagnostic.Location.IsInSource, "The consumer must be able to locate the invalid declaration.");
            var method = result.Compilation.GetTypeByMetadataName("Mapper")!
                .GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNull(method.PartialImplementationPart, "The invalid mapping must not receive an implementation.");
            return diagnostic;
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                "The diagnostic fixture must be valid apart from the pending generated implementation: " +
                string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
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
