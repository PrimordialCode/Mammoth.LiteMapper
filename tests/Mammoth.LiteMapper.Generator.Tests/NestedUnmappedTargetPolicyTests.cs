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
    public sealed class NestedUnmappedTargetPolicyTests
    {
        [TestMethod]
        [DataRow("Ignore", -1)]
        [DataRow("Info", (int)DiagnosticSeverity.Info)]
        [DataRow("Warning", (int)DiagnosticSeverity.Warning)]
        [DataRow("Error", (int)DiagnosticSeverity.Error)]
        public void AutomaticNestedHelperUsesEffectivePolicyAndPreservesDefaults(string policy, int expectedSeverity)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.POLICY)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto
{
    public int Value { get; set; }
    public int Extra { get; set; } = 12;
    public string? Note { get; set; }
}
public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new Source { Child = new Child { Value = 7 } });
        return mapped.Child.Value == 7 && mapped.Child.Extra == 12 && mapped.Child.Note == null;
    }
}
".Replace("POLICY", policy), null);

            AssertUnmappedSeverity(result, 2, expectedSeverity);
            AssertValidImplementation(result);
            Assert.IsTrue(EmitAndRun(result));
        }

        [TestMethod]
        public void MethodOverridesMapperAndMapperInheritsAssemblyPolicyForNestedHelpers()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[assembly: LiteMapperDefaults(UnmappedTargetMembers = UnmappedMemberPolicy.Info)]
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Unspecified)]
public static partial class Mapper
{
    public static partial Target Inherited(Source source);
    [MappingOptions(UnmappedTargetMembers = UnmappedMemberPolicy.Warning)]
    public static partial Target Overridden(Source source);
}
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Value { get; set; } public int Extra { get; set; } }
");

            var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "LITEMAPPER1001").ToArray();
            Assert.AreEqual(2, diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.AreEqual(1, diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Info));
            Assert.AreEqual(1, diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
            AssertValidImplementation(result);
        }

        [TestMethod]
        public void CollectionElementHelperUsesCallingPolicyAndPreservesElementDefaults()
        {
            var result = Run(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper { public static partial List<Target> Map(List<Source> source); }
public sealed class Source { public int Value { get; set; } }
public sealed class Target
{
    public int Value { get; set; }
    public int Extra { get; set; } = 12;
    public string? Note { get; set; }
}
public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new List<Source> { new Source { Value = 7 } });
        return mapped.Count == 1 && mapped[0].Value == 7 && mapped[0].Extra == 12 && mapped[0].Note == null;
    }
}
");

            AssertUnmappedSeverity(result, 2, (int)DiagnosticSeverity.Error);
            AssertValidImplementation(result);
            Assert.IsTrue(EmitAndRun(result));
        }

        [TestMethod]
        public void ExistingTargetNestedReplacementUsesPolicyAndPreservesDefaults()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper { public static partial void Apply(Source source, Target target); }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = new ChildDto { Extra = 99 }; }
public sealed class ChildDto
{
    public int Value { get; set; }
    public int Extra { get; set; } = 12;
    public string? Note { get; set; }
}
public static class Probe
{
    public static bool Run()
    {
        var target = new Target();
        var old = target.Child;
        Mapper.Apply(new Source { Child = new Child { Value = 7 } }, target);
        return target.Child.Value == 7 && target.Child.Extra == 12 && target.Child.Note == null && !ReferenceEquals(old, target.Child);
    }
}
");

            AssertUnmappedSeverity(result, 2, (int)DiagnosticSeverity.Error);
            AssertValidImplementation(result);
            Assert.IsTrue(EmitAndRun(result));
        }

        [TestMethod]
        public void NestedRequiredMemberSatisfiedBySetsRequiredMembersConstructorPreservesConstructorValue()
        {
            var result = Run(@"
using System.Diagnostics.CodeAnalysis;
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto
{
    public int Value { get; set; }
    public required int RequiredValue { get; set; }

    [SetsRequiredMembers]
    public ChildDto()
    {
        RequiredValue = 12;
    }
}
public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new Source { Child = new Child { Value = 7 } });
        return mapped.Child.Value == 7 && mapped.Child.RequiredValue == 12;
    }
}
");

            Assert.AreEqual(0, result.RunResult.Diagnostics.Count(diagnostic => diagnostic.Id == "LITEMAPPER1001"), DiagnosticsText(result));
            Assert.AreEqual(0, result.RunResult.Diagnostics.Count(diagnostic => diagnostic.Id == "LITEMAPPER1002"), DiagnosticsText(result));
            AssertValidImplementation(result);
            Assert.IsTrue(EmitAndRun(result));
        }

        [TestMethod]
        [DataRow("public required string RequiredName { get; set; }")]
        [DataRow("public string RequiredName { get; set; }")]
        public void NestedMandatoryTargetCannotBeRelaxedByIgnoreOrSuppression(string member)
        {
            var source = @"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Value { get; set; } MEMBER }
".Replace("MEMBER", member);
            var result = Run(source, new EditorConfigSeverityProvider("LITEMAPPER1002", ReportDiagnostic.Suppress));

            var diagnostic = result.RunResult.Diagnostics.Single(d => d.Id == "LITEMAPPER1002");
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            CollectionAssert.Contains(diagnostic.Descriptor.CustomTags.ToArray(), WellKnownDiagnosticTags.NotConfigurable);
            AssertOnlyMissingPartialImplementation(result);
        }

        [TestMethod]
        [DataRow(ReportDiagnostic.Warn, (int)DiagnosticSeverity.Warning)]
        [DataRow(ReportDiagnostic.Suppress, -1)]
        public void ErrorPolicyDowngradeOrSuppressionKeepsNestedImplementation(ReportDiagnostic configuredSeverity, int expectedSeverity)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Value { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Value { get; set; } public int Extra { get; set; } }
public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { Child = new Child { Value = 7 } }).Child.Value == 7;
}
", new EditorConfigSeverityProvider("LITEMAPPER1001", configuredSeverity));

            AssertUnmappedSeverity(result, 1, expectedSeverity);
            AssertValidImplementation(result);
            Assert.IsTrue(EmitAndRun(result));
        }

        private static void AssertUnmappedSeverity(GeneratorResult result, int expectedCount, int expectedSeverity)
        {
            var diagnostics = result.RunResult.Diagnostics.Where(d => d.Id == "LITEMAPPER1001" && !d.IsSuppressed).ToArray();
            Assert.AreEqual(expectedSeverity < 0 ? 0 : expectedCount, diagnostics.Length, DiagnosticsText(result));
            if (expectedSeverity >= 0)
            {
                Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Severity == (DiagnosticSeverity)expectedSeverity), DiagnosticsText(result));
            }
        }

        private static void AssertValidImplementation(GeneratorResult result)
        {
            var methods = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.Name == "Map" || method.Name == "Inherited" || method.Name == "Overridden" || method.Name == "Apply")
                .ToArray();
            Assert.IsTrue(methods.Length > 0, DiagnosticsText(result));
            Assert.IsTrue(methods.All(method => method.PartialImplementationPart != null), DiagnosticsText(result));
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        private static void AssertOnlyMissingPartialImplementation(GeneratorResult result)
        {
            var method = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers().OfType<IMethodSymbol>()
                .Single(candidate => candidate.Name == "Map");
            Assert.IsNull(method.PartialImplementationPart, DiagnosticsText(result));
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        private static string DiagnosticsText(GeneratorResult result) => string.Join(Environment.NewLine, result.RunResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));

        private static bool EmitAndRun(GeneratorResult result)
        {
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            return (bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }

        private static GeneratorResult Run(string source, SyntaxTreeOptionsProvider? severityProvider = null)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSyntaxTreeOptionsProvider(severityProvider);
            var compilation = CSharpCompilation.Create("NestedUnmappedTargetPolicyTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs") }, references, options);
            var inputErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(diagnostic => diagnostic.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorResult(driver.GetRunResult(), updatedCompilation);
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
