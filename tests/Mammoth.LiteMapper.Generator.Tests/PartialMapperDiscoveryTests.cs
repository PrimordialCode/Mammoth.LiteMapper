using System;
using System.Collections.Generic;
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
    public sealed class PartialMapperDiscoveryTests
    {
        private const string Imports = "using Mammoth.LiteMapper; using Marker = Mammoth.LiteMapper.LiteMapperAttribute;\n";
        private const string QualifiedMarker = "global::Mammoth.LiteMapper.LiteMapperAttribute";
        private const string FrameworkAttribute = "[global::System.Runtime.CompilerServices.CompilerGenerated]";
        private const string OtherFrameworkAttribute = "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]";
        private const string Models = "public sealed class Source { public int Value { get; set; } } public sealed class Target { public int Value { get; set; } }";
        private const string HealthyMapper = "[LiteMapper] public static partial class HealthyMapper { public static partial Target Map(Source source); }";

        [TestMethod]
        [DataRow(2, true, false, false, "LiteMapper")]
        [DataRow(2, false, false, false, "Marker")]
        [DataRow(3, true, false, false, QualifiedMarker)]
        [DataRow(3, false, false, false, "LiteMapper")]
        [DataRow(2, true, true, false, "Marker")]
        [DataRow(2, false, true, false, QualifiedMarker)]
        [DataRow(3, true, true, false, "LiteMapper")]
        [DataRow(3, false, true, false, "Marker")]
        [DataRow(2, false, false, true, QualifiedMarker)]
        [DataRow(3, false, true, true, "Marker")]
        public void AttributedPartialPartsProduceOnePlanAndOneExecutableFile(int partCount, bool sameFile, bool instance, bool nested, string marker)
        {
            var compilation = FlatCompilation(partCount, sameFile, instance, nested, marker);
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var driver = CreateDriver(planned).RunGenerators(compilation);
            var mapper = nested ? "Outer.Mapper" : "Mapper";

            AssertSuccessfulOutput(driver, mapper);
            CollectionAssert.AreEqual(new[] { mapper }, planned.ToArray(), "Merged partial declarations must be planned once, before rendering or diagnostic publication.");
            AssertCompilesAndExecutes(compilation, driver, 7);
        }

        [TestMethod]
        [DataRow(2, true, "Marker")]
        [DataRow(2, false, QualifiedMarker)]
        [DataRow(3, true, QualifiedMarker)]
        [DataRow(3, false, "Marker")]
        public void SeparatePartRegistrationAndFrameworkAttributesPreserveMergedMappingAndHealthyMapper(int partCount, bool sameFile, string marker)
        {
            var compilation = RegisteredCompilation(partCount, sameFile, marker);
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var driver = CreateDriver(planned).RunGenerators(compilation);

            AssertSuccessfulOutput(driver, "Mapper", "HealthyMapper");
            AssertPlannedOnce(planned, "Mapper", "HealthyMapper");
            StringAssert.Contains(SourceFor(driver, "Mapper"), "External.Convert");
            AssertCompilesAndExecutes(compilation, driver, "17|11");
        }

        [TestMethod]
        public void OrdinaryWarningIsReportedOnceForThreeAttributedPartialParts()
        {
            var compilation = FlatCompilation(3, false, false, false,
                "LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Warning)",
                Models.Replace("class Target {", "class Target { public int Extra { get; set; }"));
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var driver = CreateDriver(planned).RunGenerators(compilation);
            var result = driver.GetRunResult();

            Assert.IsNull(result.Results.Single().Exception);
            Assert.AreEqual(1, result.Diagnostics.Length, "A warning belongs to the mapper, not to each attributed partial declaration.");
            var diagnostic = result.Diagnostics.Single();
            Assert.AreEqual("LITEMAPPER1001", diagnostic.Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
            StringAssert.Contains(diagnostic.GetMessage(), "Extra");
            CollectionAssert.AreEqual(new[] { "Mapper" }, planned.ToArray());
            Assert.AreEqual(1, result.Results.Single().GeneratedSources.Length);
            AssertOutputStep(driver, "Mapper");
            AssertCompilesAndExecutes(compilation, driver, 7);
        }

        [TestMethod]
        public void PlanningFailureIsReportedOnceAtTheMarkerPartAndHealthyMapperStillGenerates()
        {
            var compilation = RegisteredCompilation(3, false, "Marker");
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var driver = CreateDriver(planned, failMapper: true).RunGenerators(compilation);
            var result = driver.GetRunResult();

            Assert.IsNull(result.Results.Single().Exception);
            Assert.AreEqual(1, result.Diagnostics.Length, "One failed mapper must have one diagnostic even when several partial parts have attributes.");
            var diagnostic = result.Diagnostics.Single();
            Assert.AreEqual("LITEMAPPER9001", diagnostic.Id);
            Assert.AreEqual("Mapper.Part2.cs", diagnostic.Location.SourceTree!.FilePath);
            Assert.AreEqual("Mapper", diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan));
            AssertPlannedOnce(planned, "Mapper", "HealthyMapper");
            Assert.AreEqual(1, result.Results.Single().GeneratedSources.Length);
            AssertOutputStep(driver, "HealthyMapper");
            StringAssert.Contains(result.Results.Single().GeneratedSources.Single().SourceText.ToString(), "class HealthyMapper");
            var outputErrors = compilation.AddSyntaxTrees(result.GeneratedTrees).GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(1, outputErrors.Length, DiagnosticsText(outputErrors));
            Assert.AreEqual("CS8795", outputErrors.Single().Id, "Only the deliberately failed mapper may lack its implementation.");
        }

        [TestMethod]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public void GenuineDuplicateMarkersRemainOneFatalConfigurationDiagnostic(bool samePart, bool sameFile)
        {
            var parts = new[]
            {
                "[Marker] " + (samePart ? "[" + QualifiedMarker + "] " : string.Empty) + "public static partial class Mapper { public static partial Target Map(Source source); }",
                (samePart ? FrameworkAttribute : "[" + QualifiedMarker + "]") + " public static partial class Mapper { }",
                OtherFrameworkAttribute + " public static partial class Mapper { }"
            };
            var compilation = CompilationFromParts(parts, sameFile, Models, HealthyMapper);
            AssertInputIsValid(compilation, duplicateMarker: true);
            var planned = new List<string>();
            var driver = CreateDriver(planned).RunGenerators(compilation);
            var result = driver.GetRunResult();

            Assert.IsNull(result.Results.Single().Exception);
            Assert.AreEqual(1, result.Diagnostics.Length, "Discovery deduplication must retain the actual duplicate marker error exactly once.");
            var diagnostic = result.Diagnostics.Single();
            Assert.AreEqual("LITEMAPPER0013", diagnostic.Id);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            CollectionAssert.Contains(diagnostic.Descriptor.CustomTags.ToArray(), WellKnownDiagnosticTags.NotConfigurable);
            StringAssert.Contains(diagnostic.GetMessage(), "LiteMapperAttribute");
            Assert.AreEqual("Mapper", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
            AssertPlannedOnce(planned, "Mapper", "HealthyMapper");
            Assert.AreEqual(1, result.Results.Single().GeneratedSources.Length);
            AssertOutputStep(driver, "HealthyMapper");
            StringAssert.Contains(result.Results.Single().GeneratedSources.Single().SourceText.ToString(), "class HealthyMapper");
            var outputErrors = compilation.AddSyntaxTrees(result.GeneratedTrees).GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(1, outputErrors.Count(static diagnostic => diagnostic.Id == "CS0579"));
            Assert.AreEqual(1, outputErrors.Count(static diagnostic => diagnostic.Id == "CS8795"));
            Assert.AreEqual(2, outputErrors.Length, DiagnosticsText(outputErrors));
        }

        [TestMethod]
        public void SameDriverRerunsAndReversedTreesPreserveOneOutputPerMapper()
        {
            var compilation = RegisteredCompilation(3, false, QualifiedMarker);
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var first = CreateDriver(planned).RunGenerators(compilation);
            AssertSuccessfulOutput(first, "Mapper", "HealthyMapper");
            AssertPlannedOnce(planned, "Mapper", "HealthyMapper");

            planned.Clear();
            var unchanged = first.RunGenerators(compilation);
            AssertSuccessfulOutput(unchanged, "Mapper", "HealthyMapper");
            AssertNoRepeatedPlanning(planned);
            AssertOutputStep(unchanged, "Mapper", IncrementalStepRunReason.Cached);
            AssertOutputStep(unchanged, "HealthyMapper", IncrementalStepRunReason.Cached);

            planned.Clear();
            var reversedCompilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(compilation.SyntaxTrees.Reverse());
            var reversed = unchanged.RunGenerators(reversedCompilation);
            AssertSuccessfulOutput(reversed, "Mapper", "HealthyMapper");
            AssertOutputStep(reversed, "Mapper", IncrementalStepRunReason.Cached);
            AssertOutputStep(reversed, "HealthyMapper", IncrementalStepRunReason.Cached);
            AssertNoRepeatedPlanning(planned);
            CollectionAssert.AreEqual(Files(first), Files(reversed));
            AssertCompilesAndExecutes(reversedCompilation, reversed, "17|11");

            planned.Clear();
            var fresh = CreateDriver(planned).RunGenerators(reversedCompilation);
            AssertSuccessfulOutput(fresh, "Mapper", "HealthyMapper");
            AssertPlannedOnce(planned, "Mapper", "HealthyMapper");
            CollectionAssert.AreEqual(Files(first), Files(fresh), "A fresh driver must choose the same mapper identity and source regardless of tree enumeration.");
            AssertCompilesAndExecutes(reversedCompilation, fresh, "17|11");
        }

        [TestMethod]
        public void RegistrationEditOnAnotherPartUpdatesOnlyAffectedEmissionInSameDriver()
        {
            var compilation = RegisteredCompilation(3, false, "Marker");
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var first = CreateDriver(planned).RunGenerators(compilation);
            AssertSuccessfulOutput(first, "Mapper", "HealthyMapper");
            AssertCompilesAndExecutes(compilation, first, "17|11");

            var original = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Mapper.Part0.cs");
            var changed = compilation.ReplaceSyntaxTree(original, CSharpSyntaxTree.ParseText(original.GetText().ToString().Replace("typeof(External)", "typeof(Alternative)"), ParseOptions(), original.FilePath));
            AssertInputIsValid(changed);
            planned.Clear();
            var second = first.RunGenerators(changed);

            AssertSuccessfulOutput(second, "Mapper", "HealthyMapper");
            Assert.AreEqual(1, planned.Count(static mapper => mapper == "Mapper"), "Changing a registration on another part must replan the merged mapper exactly once.");
            AssertNoRepeatedPlanning(planned);
            AssertOutputStep(second, "Mapper", IncrementalStepRunReason.Modified);
            AssertOutputStep(second, "HealthyMapper", IncrementalStepRunReason.Cached);
            Assert.AreEqual(SourceFor(first, "HealthyMapper"), SourceFor(second, "HealthyMapper"));
            StringAssert.Contains(SourceFor(second, "Mapper"), "Alternative.Convert");
            Assert.IsFalse(SourceFor(second, "Mapper").Contains("External.Convert", StringComparison.Ordinal));
            AssertCompilesAndExecutes(changed, second, "27|11");
        }

        [TestMethod]
        public void FrameworkAttributeEditOnAnotherPartKeepsUnchangedEmissionCached()
        {
            var compilation = FlatCompilation(3, false, false, false, "LiteMapper");
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var first = CreateDriver(planned).RunGenerators(compilation);
            AssertSuccessfulOutput(first, "Mapper");
            var original = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "Mapper.Part1.cs");
            var changed = compilation.ReplaceSyntaxTree(original, CSharpSyntaxTree.ParseText(original.GetText().ToString().Replace(FrameworkAttribute, "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"), ParseOptions(), original.FilePath));
            AssertInputIsValid(changed);
            planned.Clear();
            var second = first.RunGenerators(changed);

            AssertSuccessfulOutput(second, "Mapper");
            AssertNoRepeatedPlanning(planned);
            AssertOutputStep(second, "Mapper", IncrementalStepRunReason.Cached);
            CollectionAssert.AreEqual(Files(first), Files(second));
            AssertCompilesAndExecutes(changed, second, 7);
        }

        [TestMethod]
        public void UnattributedPartialPartsRemainAHealthyControl()
        {
            var compilation = FlatCompilation(3, false, false, false, "LiteMapper");
            compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(compilation.SyntaxTrees.Select(tree =>
                CSharpSyntaxTree.ParseText(tree.GetText().ToString().Replace(FrameworkAttribute, string.Empty).Replace(OtherFrameworkAttribute, string.Empty), ParseOptions(), tree.FilePath)));
            AssertInputIsValid(compilation);
            var planned = new List<string>();
            var driver = CreateDriver(planned).RunGenerators(compilation);

            AssertSuccessfulOutput(driver, "Mapper");
            CollectionAssert.AreEqual(new[] { "Mapper" }, planned.ToArray());
            AssertCompilesAndExecutes(compilation, driver, 7);
        }

        private static CSharpCompilation FlatCompilation(int partCount, bool sameFile, bool instance, bool nested, string marker, string? models = null)
        {
            var declaration = "public " + (instance ? "sealed " : "static ") + "partial class Mapper";
            var method = "public " + (instance ? string.Empty : "static ") + "partial Target Map(Source source);";
            var parts = new[] { "[" + marker + "] " + declaration + " { }", FrameworkAttribute + " " + declaration + " { " + method + " }", OtherFrameworkAttribute + " " + declaration + " { }" }
                .Take(partCount).Select(part => nested ? "public partial class Outer { " + part + " }" : part).ToArray();
            var mapper = nested ? "Outer.Mapper" : "Mapper";
            var invocation = instance ? "new " + mapper + "().Map" : mapper + ".Map";
            var probe = "public static class Probe { public static int Run() => " + invocation + "(new Source { Value = 7 }).Value; }";
            return CompilationFromParts(parts, sameFile, models ?? Models, probe);
        }

        private static CSharpCompilation RegisteredCompilation(int partCount, bool sameFile, string marker)
        {
            // Put the marker last and the method/registration elsewhere: the marker owns discovery, while the symbol owns all members and configuration.
            var parts = partCount == 2
                ? new[] { "[UseMapper(typeof(External))] " + FrameworkAttribute + " public static partial class Mapper { public static partial TextTarget Map(Source source); }", "[" + marker + "] public static partial class Mapper { }" }
                : new[] { "[UseMapper(typeof(External))] public static partial class Mapper { }", FrameworkAttribute + " public static partial class Mapper { public static partial TextTarget Map(Source source); }", "[" + marker + "] " + OtherFrameworkAttribute + " public static partial class Mapper { }" };
            const string external = "public sealed class TextTarget { public string Value { get; set; } = string.Empty; } " +
                "public static class External { [MappingConverter] public static string Convert(int source) => (source + 10).ToString(); } " +
                "public static class Alternative { [MappingConverter] public static string Convert(int source) => (source + 20).ToString(); }";
            const string probe = "public static class Probe { public static string Run() => Mapper.Map(new Source { Value = 7 }).Value + \"|\" + HealthyMapper.Map(new Source { Value = 11 }).Value; }";
            return CompilationFromParts(parts, sameFile, Models, HealthyMapper, external, probe);
        }

        private static CSharpCompilation CompilationFromParts(string[] parts, bool sameFile, params string[] support)
        {
            var trees = sameFile
                ? new[] { CSharpSyntaxTree.ParseText(Imports + string.Join("\n", parts), ParseOptions(), "Mappers.cs") }
                : parts.Select((part, index) => CSharpSyntaxTree.ParseText(Imports + part, ParseOptions(), "Mapper.Part" + index + ".cs")).ToArray();
            return CSharpCompilation.Create("PartialMapperDiscoveryTests", trees.Concat(new[] { CSharpSyntaxTree.ParseText(Imports + string.Join("\n", support), ParseOptions(), "Support.cs") }),
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path))
                    .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static CSharpParseOptions ParseOptions() => CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);

        private static void AssertInputIsValid(CSharpCompilation compilation, bool duplicateMarker = false)
        {
            var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            var unexpected = errors.Where(diagnostic => diagnostic.Id != "CS8795" && !(duplicateMarker && diagnostic.Id == "CS0579")).ToArray();
            Assert.AreEqual(0, unexpected.Length, "The fixture must be legal C# apart from expected missing partial implementations and the deliberate duplicate marker.\n" + DiagnosticsText(unexpected));
            Assert.AreEqual(duplicateMarker ? 1 : 0, errors.Count(static diagnostic => diagnostic.Id == "CS0579"));
        }

        private static GeneratorDriver CreateDriver(List<string> planned, bool failMapper = false) => CSharpGeneratorDriver.Create(
            new[] { new PlanningProbe(symbol =>
            {
                planned.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                if (failMapper && symbol.Name == "Mapper")
                {
                    throw new InvalidOperationException("Injected partial-mapper planning failure");
                }
            }).AsSourceGenerator() }, parseOptions: ParseOptions(),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        private static void AssertSuccessfulOutput(GeneratorDriver driver, params string[] mappers)
        {
            var result = driver.GetRunResult();
            Assert.IsNull(result.Results.Single().Exception);
            Assert.AreEqual(0, result.Diagnostics.Length, DiagnosticsText(result.Diagnostics));
            Assert.AreEqual(mappers.Length, result.Results.Single().GeneratedSources.Length, "Each mapper class must have exactly one generated file.");
            Assert.AreEqual(mappers.Length, result.Results.Single().GeneratedSources.Select(static source => source.HintName).Distinct(StringComparer.Ordinal).Count());
            foreach (var mapper in mappers)
            {
                AssertOutputStep(driver, mapper);
            }
        }

        private static void AssertPlannedOnce(List<string> planned, params string[] mappers) => CollectionAssert.AreEqual(
            mappers.OrderBy(static name => name, StringComparer.Ordinal).ToArray(), planned.OrderBy(static name => name, StringComparer.Ordinal).ToArray(), "Planning must run once per mapper symbol, before diagnostics or source emission.");

        private static void AssertNoRepeatedPlanning(List<string> planned) => Assert.AreEqual(planned.Count, planned.Distinct(StringComparer.Ordinal).Count(), "Replanning must never repeat a mapper symbol for its attributed partial parts.");

        private static IncrementalGeneratorRunStep[] OutputStepsFor(GeneratorDriver driver, string mapper) =>
            driver.GetRunResult().Results.Single().TrackedOutputSteps.Values.SelectMany(static values => values)
                .Where(step => step.Inputs.Any(input => input.Source.Name == "MapperEmission" &&
                    (string?)input.Source.Outputs[input.OutputIndex].Value.GetType().GetProperty("DisplayName")?.GetValue(input.Source.Outputs[input.OutputIndex].Value) == mapper)).ToArray();

        private static IncrementalStepRunReason AssertOutputStep(GeneratorDriver driver, string mapper, IncrementalStepRunReason? expected = null)
        {
            var allSteps = OutputStepsFor(driver, mapper);
            var states = string.Join(", ", allSteps.SelectMany(static step => step.Outputs).Select(static output => output.Reason));
            var steps = allSteps
                .Where(static step => step.Outputs.Any(static output => output.Reason != IncrementalStepRunReason.Removed)).ToArray();
            Assert.AreEqual(1, steps.Length, "Expected one active independent source-output callback for " + mapper + ". Observed states: " + states);
            var outputs = steps.SelectMany(static step => step.Outputs).Where(static output => output.Reason != IncrementalStepRunReason.Removed).ToArray();
            Assert.AreEqual(1, outputs.Length, "Expected one active output for " + mapper + ". Observed states: " + states);
            var reason = outputs.Single().Reason;
            if (expected.HasValue)
            {
                Assert.AreEqual(expected.Value, reason, "Unexpected incremental output state for " + mapper + ". Observed states: " + states);
            }
            return reason;
        }

        private static string SourceFor(GeneratorDriver driver, string mapper) => driver.GetRunResult().Results.Single().GeneratedSources
            .Single(source => source.HintName.StartsWith(mapper + ".", StringComparison.Ordinal)).SourceText.ToString();

        private static string[] Files(GeneratorDriver driver) => driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .Select(static source => source.HintName + "\n" + source.SourceText).ToArray();

        private static string DiagnosticsText(IEnumerable<Diagnostic> diagnostics) => string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));

        private static void AssertCompilesAndExecutes(CSharpCompilation compilation, GeneratorDriver driver, object expected)
        {
            var output = compilation.AddSyntaxTrees(driver.GetRunResult().GeneratedTrees);
            var errors = output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, DiagnosticsText(errors));
            using var stream = new MemoryStream();
            var emitted = output.Emit(stream);
            Assert.IsTrue(emitted.Success, DiagnosticsText(emitted.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(expected, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private sealed class PlanningProbe : IIncrementalGenerator
        {
            private readonly Action<INamedTypeSymbol> beforePlanning;
            public PlanningProbe(Action<INamedTypeSymbol> beforePlanning) => this.beforePlanning = beforePlanning;
            public void Initialize(IncrementalGeneratorInitializationContext context) => LiteMapperGenerator.InitializeCore(context, beforePlanning);
        }
    }
}
