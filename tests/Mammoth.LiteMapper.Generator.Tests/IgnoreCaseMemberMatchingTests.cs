using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class IgnoreCaseMemberMatchingTests
    {
        [TestMethod]
        [DataRow("create", "properties")]
        [DataRow("create", "fields")]
        [DataRow("create", "mixed")]
        [DataRow("create", "inherited")]
        [DataRow("create", "interface")]
        [DataRow("update", "properties")]
        [DataRow("update", "fields")]
        [DataRow("update", "mixed")]
        [DataRow("update", "inherited")]
        [DataRow("update", "interface")]
        [DataRow("nested", "properties")]
        [DataRow("nested", "fields")]
        [DataRow("nested", "mixed")]
        [DataRow("nested", "inherited")]
        [DataRow("nested-update", "properties")]
        [DataRow("nested-update", "fields")]
        [DataRow("nested-update", "mixed")]
        [DataRow("nested-update", "inherited")]
        public void IgnoreCaseRejectsExactCandidateAmongCaseInsensitiveMatches(string mapping, string members)
        {
            AssertAmbiguous(Run(Fixture(mapping, members, "IgnoreCase")));
        }

        [TestMethod]
        [DataRow("create", "Exact")]
        [DataRow("create", "ExactThenIgnoreCase")]
        [DataRow("constructor", "Exact")]
        [DataRow("constructor", "ExactThenIgnoreCase")]
        [DataRow("update", "Exact")]
        [DataRow("update", "ExactThenIgnoreCase")]
        [DataRow("nested", "Exact")]
        [DataRow("nested", "ExactThenIgnoreCase")]
        [DataRow("nested-update", "Exact")]
        [DataRow("nested-update", "ExactThenIgnoreCase")]
        public void ExactPoliciesStillSelectTheExactMember(string mapping, string policy)
        {
            AssertValidAndRun(Run(Fixture(mapping, "inherited", policy, probe: true)));
        }

        [TestMethod]
        [DataRow("Exact", "Value")]
        [DataRow("ExactThenIgnoreCase", "value")]
        [DataRow("IgnoreCase", "value")]
        public void UniqueMatchesRemainUsable(string policy, string member)
        {
            var source = Fixture("create", "properties", policy, probe: true)
                .Replace("public int Value { get; set; } = 7; public int value { get; set; } = 99;", "public int " + member + " { get; set; } = 7;");
            AssertValidAndRun(Run(source));
        }

        [TestMethod]
        [DataRow("create", "ExactThenIgnoreCase")]
        [DataRow("nested", "ExactThenIgnoreCase")]
        [DataRow("create", "IgnoreCase")]
        public void CaseInsensitiveFallbackStillRejectsMultipleCandidates(string mapping, string policy)
        {
            AssertAmbiguous(Run(Fixture(mapping, "properties", policy).Replace("public int Value { get; set; } = 7;", "public int VALUE { get; set; } = 7;")));
        }

        [TestMethod]
        [DataRow("create")]
        [DataRow("update")]
        public void ExplicitSourceSelectionDisambiguatesIgnoreCase(string mapping)
        {
            var source = Fixture(mapping, "properties", "IgnoreCase", probe: true)
                .Replace("public static partial " + (mapping == "update" ? "void" : "Target") + " Invalid", "[MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value))] public static partial " + (mapping == "update" ? "void" : "Target") + " Invalid");
            AssertValidAndRun(Run(source));
        }

        [TestMethod]
        public void HiddenMembersAreResolvedBeforeIgnoreCaseMatching()
        {
            var result = Run(Fixture("create", "inherited", "IgnoreCase", probe: true)
                .Replace("public int value { get; set; } = 99;", "public int Value { get; set; } = 99;")
                .Replace("public class Source : Base { public int Value", "public class Source : Base { public new int Value"));
            CollectionAssert.AreEqual(new[] { "LITEMAPPER1005" }, result.RunResult.Diagnostics.Select(d => d.Id).ToArray());
            AssertValidAndRun(result, allowHiddenWarning: true);
        }

        [TestMethod]
        [DataRow("assembly")]
        [DataRow("method")]
        public void InheritedConfigurationStillEnforcesIgnoreCaseAmbiguity(string scope)
        {
            var source = Fixture("create", "properties", "IgnoreCase");
            source = scope == "assembly"
                ? source.Replace("[LiteMapper(NameMatching = NameMatching.IgnoreCase)]", "[assembly: LiteMapperDefaults(NameMatching = NameMatching.IgnoreCase)] [LiteMapper]")
                : source.Replace("[LiteMapper(NameMatching = NameMatching.IgnoreCase)]", "[LiteMapper(NameMatching = NameMatching.Exact)]")
                    .Replace("public static partial Target Invalid", "[MappingOptions(NameMatching = NameMatching.IgnoreCase)] public static partial Target Invalid");
            AssertAmbiguous(Run(source));
        }

        [TestMethod]
        public void CandidateDeclarationOrderDoesNotChangeTheDiagnostic()
        {
            var source = Fixture("nested", "properties", "IgnoreCase");
            var first = Run(source);
            var second = Run(source.Replace("public int Value { get; set; } = 7; public int value { get; set; } = 99;", "public int value { get; set; } = 99; public int Value { get; set; } = 7;"));
            AssertAmbiguous(first);
            AssertAmbiguous(second);
            CollectionAssert.AreEqual(first.RunResult.Diagnostics.Select(d => d.ToString()).ToArray(), second.RunResult.Diagnostics.Select(d => d.ToString()).ToArray());
            CollectionAssert.AreEqual(first.RunResult.GeneratedTrees.Select(t => t.ToString()).ToArray(), second.RunResult.GeneratedTrees.Select(t => t.ToString()).ToArray());
        }

        private static string Fixture(string mapping, string members, string policy, bool probe = false)
        {
            var nested = mapping.StartsWith("nested", StringComparison.Ordinal);
            var update = mapping.EndsWith("update", StringComparison.Ordinal);
            var sourceType = members == "interface" ? "ISource" : "Source";
            var models = members switch
            {
                "fields" => "public class Source { public int Value = 7; public int value = 99; }",
                "mixed" => "public class Source { public int Value { get; set; } = 7; public int value = 99; }",
                "inherited" => "public class Base { public int value { get; set; } = 99; } public class Source : Base { public int Value { get; set; } = 7; }",
                "interface" => "public interface IBase { int value { get; } } public interface ISource : IBase { int Value { get; } } public class Source : ISource { public int Value => 7; public int value => 99; }",
                _ => "public class Source { public int Value { get; set; } = 7; public int value { get; set; } = 99; }",
            };
            var input = nested ? "RootSource" : sourceType;
            var output = nested ? "RootTarget" : "Target";
            var declaration = update ? "void Invalid(" + input + " source, " + output + " target);" : output + " Invalid(" + input + " source);";
            return @"using Mammoth.LiteMapper;
[LiteMapper(NameMatching = NameMatching.__POLICY__)]
public static partial class Mapper
{
    public static partial __DECLARATION__
    public static partial HealthyTarget Healthy(HealthySource source);
}
__MODELS__
public class Target { __TARGET__ }
public class RootSource { public __SOURCE__ Child { get; set; } = new Source(); }
public class RootTarget { public Target Child { get; set; } = new Target(); }
public class HealthySource { public int Other { get; set; } }
public class HealthyTarget { public int Other { get; set; } }
__PROBE__"
                .Replace("__POLICY__", policy).Replace("__DECLARATION__", declaration)
                .Replace("__MODELS__", models).Replace("__SOURCE__", sourceType)
                .Replace("__TARGET__", mapping == "constructor" ? "public Target(int Value) { this.Value = Value; } public int Value { get; }" : "public int Value { get; set; }")
                .Replace("new Target();", mapping == "constructor" ? "new Target(0);" : "new Target();")
                .Replace("__PROBE__", probe ? "public static class Probe { public static bool Run() { var source = new " + (nested ? "RootSource" : "Source") + "(); " +
                    (update ? "var target = new " + output + "(); Mapper.Invalid(source, target);" : "var target = Mapper.Invalid(source);") +
                    " return target." + (nested ? "Child." : "") + "Value == 7; } }" : "");
        }

        private static void AssertAmbiguous(GeneratorRun result)
        {
            var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "LITEMAPPER1004").ToArray();
            Assert.AreEqual(1, diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var diagnostic = diagnostics[0];
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.IsTrue(diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable));
            StringAssert.Contains(diagnostic.GetMessage(), "Value");
            Assert.AreEqual("Value", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"));
            var methods = result.RunResult.GeneratedTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
            Assert.IsFalse(methods.Any(m => m.Identifier.ValueText == "Invalid"), "The ambiguous mapping must not receive an implementation.");
            Assert.IsTrue(methods.Any(m => m.Identifier.ValueText == "Healthy"), "Independent mappings must still generate.");
            var unexpectedErrors = result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, unexpectedErrors.Length, string.Join(Environment.NewLine, unexpectedErrors.Select(d => d.ToString())));
        }

        private static void AssertValidAndRun(GeneratorRun result, bool allowHiddenWarning = false)
        {
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => !allowHiddenWarning || d.Id != "LITEMAPPER1005"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var warningsAndErrors = result.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, warningsAndErrors.Length, string.Join(Environment.NewLine, warningsAndErrors.Select(d => d.ToString())));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static GeneratorRun Run(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create("IgnoreCaseMemberMatchingTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
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
