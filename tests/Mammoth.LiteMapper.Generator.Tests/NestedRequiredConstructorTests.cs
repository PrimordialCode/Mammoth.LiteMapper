using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class NestedRequiredConstructorTests
    {
        [TestMethod]
        [DataRow("Nested")]
        [DataRow("CollectionElement")]
        [DataRow("ExistingTargetReplacement")]
        public void ConstructorBoundRequiredMemberWithoutLanguageMetadataIsFatal(string shape)
        {
            var result = Run(BuildInvalidSource(shape),
                new EditorConfigSeverityProvider("LITEMAPPER1002", ReportDiagnostic.Suppress));

            var diagnostic = result.RunResult.Diagnostics.Single(candidate => candidate.Id == "LITEMAPPER1002");
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity, DiagnosticsText(result));
            CollectionAssert.Contains(diagnostic.Descriptor.CustomTags.ToArray(), WellKnownDiagnosticTags.NotConfigurable);
            Assert.IsFalse(diagnostic.IsSuppressed, "UnmappedTargetMembers.Ignore and editorconfig suppression must not bypass a required-member obligation.");

            var mapper = result.Compilation.GetTypeByMetadataName("Mapper")!;
            var invalidMethodName = shape == "ExistingTargetReplacement" ? "Apply" : "Map";
            var invalidMethod = mapper.GetMembers(invalidMethodName).OfType<IMethodSymbol>().Single();
            Assert.IsNull(invalidMethod.PartialImplementationPart,
                "The invalid mapping must be omitted instead of emitting a constructor call that causes CS9035.");
            var healthyMethod = mapper.GetMembers("Healthy").OfType<IMethodSymbol>().Single();
            Assert.IsNotNull(healthyMethod.PartialImplementationPart,
                "A method-specific required-member failure must not suppress an independent valid sibling.");

            var unexpectedErrors = result.Compilation.GetDiagnostics()
                .Where(candidate => candidate.Severity == DiagnosticSeverity.Error && candidate.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, unexpectedErrors.Length,
                "Generated source must not leak CS9035 or another compiler error: " +
                string.Join(Environment.NewLine, unexpectedErrors.Select(static candidate => candidate.ToString())));
        }

        [TestMethod]
        public void SetsRequiredMembersConstructorPreservesConstructorValueWithoutDuplicateAssignment()
        {
            var result = Run(@"
using System.Diagnostics.CodeAnalysis;
using Mammoth.LiteMapper;

[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}

public sealed class Source
{
    public ChildSource Child { get; set; } = new ChildSource();
}

public sealed class ChildSource
{
    public string Name { get; set; } = ""source"";
}

public sealed class Target
{
    public ChildTarget Child { get; set; } = null!;
}

public sealed class ChildTarget
{
    private string name = string.Empty;

    [SetsRequiredMembers]
    public ChildTarget(string name)
    {
        Name = ""constructor:"" + name;
    }

    public static int Assignments { get; set; }

    public required string Name
    {
        get => name;
        set
        {
            Assignments++;
            name = value;
        }
    }
}

public static class Probe
{
    public static bool Run()
    {
        ChildTarget.Assignments = 0;
        var mapped = Mapper.Map(new Source());
        return mapped.Child.Name == ""constructor:source"" && ChildTarget.Assignments == 1;
    }
}
", null);

            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, DiagnosticsText(result));
            var errors = result.Compilation.GetDiagnostics()
                .Where(static candidate => candidate.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.AreEqual(0, errors.Length,
                string.Join(Environment.NewLine, errors.Select(static candidate => candidate.ToString())));
            Assert.IsTrue(EmitAndRun(result),
                "A SetsRequiredMembers constructor binding must retain its value and execute the setter only once.");
        }

        private static string BuildInvalidSource(string shape)
        {
            string mapping;
            string models;
            switch (shape)
            {
                case "Nested":
                    mapping = "public static partial Target Map(Source source);";
                    models = @"
public sealed class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public sealed class Target { public ChildTarget Child { get; set; } = null!; }";
                    break;
                case "CollectionElement":
                    mapping = "public static partial List<ChildTarget> Map(List<ChildSource> source);";
                    models = string.Empty;
                    break;
                case "ExistingTargetReplacement":
                    mapping = "public static partial void Apply(Source source, Target target);";
                    models = @"
public sealed class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public sealed class Target { public ChildTarget Child { get; set; } = null!; }";
                    break;
                default:
                    Assert.Fail("Unknown mapping shape: " + shape);
                    throw new InvalidOperationException();
            }

            return @"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper
{
    " + mapping + @"
    public static partial HealthyTarget Healthy(HealthySource source);
}
" + models + @"
public sealed class ChildSource { public string Name { get; set; } = ""source""; }
public sealed class ChildTarget
{
    public ChildTarget(string name) { Name = name; }
    public required string Name { get; set; }
}
public sealed class HealthySource { public int Value { get; set; } }
public sealed class HealthyTarget { public int Value { get; set; } }
";
        }

        private static bool EmitAndRun(GeneratorResult result)
        {
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success,
                string.Join(Environment.NewLine, emitted.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            return (bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }

        private static string DiagnosticsText(GeneratorResult result) =>
            string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(static diagnostic => diagnostic.ToString()));

        private static GeneratorResult Run(string source, SyntaxTreeOptionsProvider? severityProvider)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithSyntaxTreeOptionsProvider(severityProvider);
            var compilation = CSharpCompilation.Create(
                "NestedRequiredConstructorTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs") },
                references,
                compilationOptions);
            var inputErrors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                "Fixture errors must not masquerade as generator failures: " +
                string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorResult(driver.GetRunResult(), updatedCompilation);
        }

        private sealed class EditorConfigSeverityProvider : SyntaxTreeOptionsProvider
        {
            private readonly string id;
            private readonly ReportDiagnostic severity;

            public EditorConfigSeverityProvider(string id, ReportDiagnostic severity)
            {
                this.id = id;
                this.severity = severity;
            }

            public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) =>
                GeneratedKind.NotGenerated;

            public override bool TryGetDiagnosticValue(
                SyntaxTree tree,
                string diagnosticId,
                CancellationToken cancellationToken,
                out ReportDiagnostic configuredSeverity)
            {
                configuredSeverity = severity;
                return tree.FilePath == "Consumer.cs" && diagnosticId == id;
            }

            public override bool TryGetGlobalDiagnosticValue(
                string diagnosticId,
                CancellationToken cancellationToken,
                out ReportDiagnostic configuredSeverity)
            {
                configuredSeverity = ReportDiagnostic.Default;
                return false;
            }
        }

        private sealed class GeneratorResult
        {
            public GeneratorResult(GeneratorDriverRunResult runResult, Compilation compilation)
            {
                RunResult = runResult;
                Compilation = compilation;
            }

            public GeneratorDriverRunResult RunResult { get; }

            public Compilation Compilation { get; }
        }
    }
}
