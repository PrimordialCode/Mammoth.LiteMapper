using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class UnmappedDiagnosticSeverityTests
    {
        [TestMethod]
        [DataRow(true, "Ignore", -1)]
        [DataRow(true, "Info", (int)DiagnosticSeverity.Info)]
        [DataRow(true, "Warning", (int)DiagnosticSeverity.Warning)]
        [DataRow(true, "Error", (int)DiagnosticSeverity.Error)]
        [DataRow(false, "Ignore", -1)]
        [DataRow(false, "Info", (int)DiagnosticSeverity.Info)]
        [DataRow(false, "Warning", (int)DiagnosticSeverity.Warning)]
        [DataRow(false, "Error", (int)DiagnosticSeverity.Error)]
        public void ConfiguredUnmappedPolicyReportsExactSeverity(bool target, string policy, int expectedSeverity)
        {
            var result = RunGenerator(Source(target, policy));
            AssertSeverity(result.RunResult, target ? "LITEMAPPER1001" : "LITEMAPPER1003", expectedSeverity);
            AssertValidImplementation(result.Compilation);
        }

        [TestMethod]
        [DataRow(true, "Error", ReportDiagnostic.Warn, (int)DiagnosticSeverity.Warning)]
        [DataRow(false, "Error", ReportDiagnostic.Warn, (int)DiagnosticSeverity.Warning)]
        [DataRow(true, "Error", ReportDiagnostic.Suppress, -1)]
        [DataRow(false, "Error", ReportDiagnostic.Suppress, -1)]
        [DataRow(true, "Warning", ReportDiagnostic.Info, (int)DiagnosticSeverity.Info)]
        [DataRow(false, "Warning", ReportDiagnostic.Info, (int)DiagnosticSeverity.Info)]
        [DataRow(true, "Warning", ReportDiagnostic.Error, (int)DiagnosticSeverity.Error)]
        [DataRow(false, "Warning", ReportDiagnostic.Error, (int)DiagnosticSeverity.Error)]
        public void EditorConfigSeverityControlsConfigurableUnmappedDiagnostic(bool target, string policy, ReportDiagnostic configuredSeverity, int expectedSeverity)
        {
            var id = target ? "LITEMAPPER1001" : "LITEMAPPER1003";
            var result = RunGenerator(Source(target, policy), new EditorConfigSeverityProvider(id, configuredSeverity));
            AssertSeverity(result.RunResult, id, expectedSeverity);
            AssertValidImplementation(result.Compilation);
        }

        [TestMethod]
        public void EditorConfigCannotSuppressMandatoryTargetFailure()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { }
public sealed class Target { public required string RequiredValue { get; set; } }
", new EditorConfigSeverityProvider("LITEMAPPER1002", ReportDiagnostic.Suppress));

            AssertSeverity(result.RunResult, "LITEMAPPER1002", (int)DiagnosticSeverity.Error);
            var diagnostic = result.RunResult.Diagnostics.Single(d => d.Id == "LITEMAPPER1002");
            CollectionAssert.Contains(diagnostic.Descriptor.CustomTags.ToArray(), WellKnownDiagnosticTags.NotConfigurable);
            Assert.IsNull(result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map")
                .OfType<IMethodSymbol>().Single().PartialImplementationPart);
        }

        [TestMethod]
        [DataRow("public string Extra { get; set; }", false, true)]
        [DataRow("public string? Extra { get; set; }", false, false)]
        [DataRow("public string Extra { get; set; } = string.Empty;", false, true)]
        [DataRow("public string Extra { get; set; } = string.Empty;", true, false)]
        public void IgnoredReferenceTargetsRespectMandatoryNullabilityAndApprovedDefaults(string memberDeclaration, bool approveDefault, bool mandatory)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper
{
    " + (approveDefault ? "[UseTargetDefault(nameof(Target.Extra))]" : string.Empty) + @"
    public static partial Target Map(Source source);
}
public sealed class Source { }
public sealed class Target { " + memberDeclaration + @" }
", new EditorConfigSeverityProvider("LITEMAPPER1002", ReportDiagnostic.Suppress));

            if (mandatory)
            {
                AssertSeverity(result.RunResult, "LITEMAPPER1002", (int)DiagnosticSeverity.Error);
                var diagnostic = result.RunResult.Diagnostics.Single(d => d.Id == "LITEMAPPER1002");
                CollectionAssert.Contains(diagnostic.Descriptor.CustomTags.ToArray(), WellKnownDiagnosticTags.NotConfigurable);
                Assert.IsNull(result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map")
                    .OfType<IMethodSymbol>().Single().PartialImplementationPart,
                    "Sections 6.4 and 10.7 require explicit satisfaction of a non-null target, even with Ignore or an unapproved initializer.");
            }
            else
            {
                Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
                AssertValidImplementation(result.Compilation);
            }
        }

        private static string Source(bool target, string policy)
        {
            var option = target ? "UnmappedTargetMembers" : "UnmappedSourceMembers";
            return @"
using Mammoth.LiteMapper;
[LiteMapper(" + option + " = UnmappedMemberPolicy." + policy + @")]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public int Value { get; set; } " + (target ? string.Empty : "public int Extra { get; set; }") + @" }
public sealed class Target { public int Value { get; set; } " + (target ? "public int Extra { get; set; }" : string.Empty) + @" }
";
        }

        private static void AssertSeverity(GeneratorDriverRunResult result, string id, int expectedSeverity)
        {
            var diagnostics = result.Diagnostics.Where(d => d.Id == id && !d.IsSuppressed).ToArray();
            Assert.AreEqual(expectedSeverity < 0 ? 0 : 1, diagnostics.Length, string.Join(Environment.NewLine, result.Diagnostics));
            if (expectedSeverity >= 0)
            {
                Assert.AreEqual((DiagnosticSeverity)expectedSeverity, diagnostics[0].Severity, "Configured unmapped severity must be honored exactly.");
            }
        }

        private static void AssertValidImplementation(Compilation compilation)
        {
            var method = compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNotNull(method.PartialImplementationPart, "An ordinary configurable diagnostic must not suppress valid code and prevent later severity overrides.");
            var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(static d => d.ToString())));
        }

        private static GeneratorRun RunGenerator(string source, SyntaxTreeOptionsProvider? severityProvider = null)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSyntaxTreeOptionsProvider(severityProvider);
            var compilation = CSharpCompilation.Create("Tests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs") }, references, options);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private sealed class EditorConfigSeverityProvider : SyntaxTreeOptionsProvider
        {
            private readonly string _id;
            private readonly ReportDiagnostic _severity;

            public EditorConfigSeverityProvider(string id, ReportDiagnostic severity)
            {
                _id = id;
                _severity = severity;
            }

            public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.NotGenerated;

            public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
            {
                severity = _severity;
                return tree.FilePath == "Consumer.cs" && diagnosticId == _id;
            }

            public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
            {
                severity = ReportDiagnostic.Default;
                return false;
            }
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
