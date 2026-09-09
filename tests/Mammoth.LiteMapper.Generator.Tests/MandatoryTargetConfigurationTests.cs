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
    public sealed class MandatoryTargetConfigurationTests
    {
        [TestMethod]
        [DataRow("public required string Name { get; set; }")]
        [DataRow("public required string Name { get; set; } = \"declared\";")]
        [DataRow("public string Name { get; set; }")]
        [DataRow("public string Name { get; set; } = \"declared\";")]
        public void IgnoreTargetDoesNotDischargeMandatoryReferenceMember(string member)
        {
            var result = RunGenerator("[IgnoreTarget(nameof(Target.Name))]", member);

            AssertFatal(result, "LITEMAPPER1002");
        }

        [TestMethod]
        public void IgnoreTargetDoesNotDischargeRequiredValueMember()
        {
            var result = RunGenerator("[IgnoreTarget(nameof(Target.Name))]", "public required int Name { get; set; }");

            AssertFatal(result, "LITEMAPPER1002");
        }

        [TestMethod]
        public void RealInitializerDoesNotSatisfyCSharpRequiredAssignment()
        {
            var result = RunGenerator("[UseTargetDefault(nameof(Target.Name))]",
                "public Target() { } public required string Name { get; set; } = \"declared\";");

            AssertFatal(result, "LITEMAPPER1002");
        }

        [TestMethod]
        [DataRow("public string? Name { get; set; }", null)]
        [DataRow("public int Name { get; set; } = 17;", 17)]
        public void OrdinaryNullableAndValueMembersMayBeIgnored(string member, object? expected)
        {
            var result = RunGenerator("[IgnoreTarget(nameof(Target.Name))]", member);

            AssertMappedName(result, expected);
        }

        [TestMethod]
        public void ConstructorBindingSatisfiesIgnoredNonNullableMember()
        {
            var result = RunGenerator("[IgnoreTarget(nameof(Target.Name))]", @"
    public Target(string name) { Name = name; }
    public string Name { get; }
");

            AssertMappedName(result, "source");
        }

        [TestMethod]
        public void SetsRequiredMembersConstructorSatisfiesIgnoredRequiredMember()
        {
            var result = RunGenerator("[IgnoreTarget(nameof(Target.Name))]", @"
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Target() { Name = ""constructor""; }
    public required string Name { get; set; }
");

            AssertMappedName(result, "constructor");
        }

        [TestMethod]
        public void SetsRequiredMembersConstructorAllowsPreservingRequiredMemberInitializer()
        {
            var result = RunGenerator("[UseTargetDefault(nameof(Target.Name))]", @"
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Target() { }
    public required string Name { get; set; } = ""declared"";
");

            AssertMappedName(result, "declared");
        }

        [TestMethod]
        public void ApprovedInitializerSatisfiesNonNullableMemberAndPreservesItsValue()
        {
            var result = RunGenerator("[UseTargetDefault(nameof(Target.Name))]",
                "public string Name { get; set; } = \"declared\";");

            AssertMappedName(result, "declared");
        }

        [TestMethod]
        public void ApprovedOptionalConstructorDefaultOverridesAvailableSourceValue()
        {
            var result = RunGenerator("[UseTargetDefault(nameof(Target.Name))]", @"
    public Target(string name = ""declared"") { Name = name; }
    public string Name { get; }
");

            AssertMappedName(result, "declared");
        }

        [TestMethod]
        [DataRow("[UseTargetDefault(nameof(Target.Id))]", 17)]
        [DataRow("", 99)]
        public void OptionalValueConstructorDefaultIsUsedOnlyWhenRequested(string attributes, int expected)
        {
            var result = RunGenerator(attributes, @"
    public Target(int id = 17) { Id = id; }
    public int Id { get; }
", "public int Id { get; set; } = 99;");

            AssertMappedName(result, expected, "Id");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ConstructorBoundRequiredMemberNeedsLanguageSatisfactionMetadata(bool setsRequiredMembers)
        {
            var result = RunGenerator("", (setsRequiredMembers ? "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]" : "") + @"
    public Target(string name) { Name = name; }
    public required string Name { get; set; }
");

            if (setsRequiredMembers)
            {
                AssertMappedName(result, "source");
            }
            else
            {
                AssertFatal(result, "LITEMAPPER1002");
            }
        }

        [TestMethod]
        [DataRow("[MapProperty(Source = nameof(Source.Name), Target = nameof(Target.Name))][IgnoreTarget(nameof(Target.Name))]")]
        [DataRow("[IgnoreTarget(nameof(Target.Name))][MapProperty(Source = nameof(Source.Name), Target = nameof(Target.Name))]")]
        [DataRow("[MapProperty(Source = nameof(Source.Name), Target = nameof(Target.Name))][UseTargetDefault(nameof(Target.Name))]")]
        [DataRow("[UseTargetDefault(nameof(Target.Name))][MapProperty(Source = nameof(Source.Name), Target = nameof(Target.Name))]")]
        public void ConflictingExplicitTargetConfigurationCannotSilentlyChooseAWinner(string attributes)
        {
            var result = RunGenerator(attributes, "public string? Name { get; set; } = \"declared\";");

            AssertFatal(result, "LITEMAPPER1008");
        }

        private static void AssertFatal(GeneratorRun result, string id)
        {
            var matches = result.RunResult.Diagnostics.Where(d => d.Id == id).ToArray();
            Assert.IsTrue(matches.Length > 0,
                "Sections 6.4, 9.7, and 10.5 prohibit bypassing mandatory target satisfaction or choosing conflicting configuration. Expected " +
                id + "; actual: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.AreEqual(DiagnosticSeverity.Error, matches[0].Severity);
            Assert.IsTrue(matches[0].Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable));
            var mapping = result.Compilation.GetTypeByMetadataName("Mapper")!
                .GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNull(mapping.PartialImplementationPart, "A fatal contract violation must omit the mapping implementation.");
        }

        private static void AssertMappedName(GeneratorRun result, object? expected, string propertyName = "Name")
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                "A constructor binding or approved default must retain supported behavior: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            var source = assembly.CreateInstance("Source")!;
            var target = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(expected, target.GetType().GetProperty(propertyName)!.GetValue(target),
                "Ignoring or preserving a default must not silently assign the available source value.");
        }

        private static GeneratorRun RunGenerator(string attributes, string targetMembers,
            string sourceMembers = "public string Name { get; set; } = \"source\";")
        {
            var source = @"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper
{
    " + attributes + @"
    public static partial Target Map(Source source);
}
public sealed class Source { " + sourceMembers + @" }
public sealed class Target
{
    " + targetMembers + @"
}
";
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, options) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                "Fixture errors must not masquerade as mapping defects: " + string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
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
