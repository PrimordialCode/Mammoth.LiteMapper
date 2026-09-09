using System;
using System.Collections.Immutable;
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
    public sealed class Milestone5ConstructionTests
    {
        [TestMethod]
        public void ParameterizedConstructorIsSelectedAndBoundMembersAreNotAssignedAgain()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}

public sealed class Source { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
public sealed class Target
{
    public Target(int id) { Id = id; }
    public int Id { get; }
    public string Name { get; set; } = string.Empty;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "new Target(id: source.Id)");
            Assert.IsFalse(generated.Contains("target.Id =", StringComparison.Ordinal), generated);
            StringAssert.Contains(generated, "Name = source.Name");

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Id")!.SetValue(source, 42);
            source.GetType().GetProperty("Name")!.SetValue(source, "Ada");
            var target = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(42, target.GetType().GetProperty("Id")!.GetValue(target));
            Assert.AreEqual("Ada", target.GetType().GetProperty("Name")!.GetValue(target));
        }

        [TestMethod]
        public void ConstructorDiagnosticsUseStableIds()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } public string Other { get; set; } = string.Empty; }
public sealed class Target
{
    public Target(int id) { }
    public Target(string other) { }
}
").RunResult, "LITEMAPPER1011");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target
{
    [MappingConstructor] public Target(int id) { }
}
").RunResult, "LITEMAPPER1013");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target
{
    [MappingConstructor] public Target(int id) { }
    [MappingConstructor] public Target() { }
}
").RunResult, "LITEMAPPER1012");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target
{
    private Target() { }
}
").RunResult, "LITEMAPPER1010");
        }

        [TestMethod]
        public void OptionalParametersRecordsInitRequiredAndValueTypesAreSupported()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
    public static partial PersonRecord ToRecord(Source source);
    public static partial PointStruct ToStruct(Source source);
}

public sealed class Source { public int Id { get; set; } public string Name { get; set; } = string.Empty; public int X { get; set; } public int Y { get; set; } }
public sealed class Target
{
    public Target(int id, int optional = 9) { Id = id; Optional = optional; }
    public int Id { get; }
    public int Optional { get; }
    public required string Name { get; init; }
}
public record PersonRecord(int Id) { public required string Name { get; init; } }
public readonly record struct PointStruct(int X, int Y);
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static t => t.GetText().ToString()));
            StringAssert.Contains(generated, "new Target(id: source.Id)");
            Assert.IsFalse(generated.Contains("default!", StringComparison.Ordinal), generated);
            StringAssert.Contains(generated, "Name = source.Name");
            StringAssert.Contains(generated, "new PersonRecord(Id: source.Id)");
            StringAssert.Contains(generated, "new PointStruct(X: source.X, Y: source.Y)");
            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Id")!.SetValue(source, 3);
            source.GetType().GetProperty("Name")!.SetValue(source, "Ada");
            source.GetType().GetProperty("X")!.SetValue(source, 4);
            source.GetType().GetProperty("Y")!.SetValue(source, 5);
            var mapper = assembly.GetType("Mapper")!;
            var target = mapper.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(3, target.GetType().GetProperty("Id")!.GetValue(target));
            Assert.AreEqual(9, target.GetType().GetProperty("Optional")!.GetValue(target));
            Assert.AreEqual("Ada", target.GetType().GetProperty("Name")!.GetValue(target));
            var record = mapper.GetMethod("ToRecord")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(3, record.GetType().GetProperty("Id")!.GetValue(record));
            Assert.AreEqual("Ada", record.GetType().GetProperty("Name")!.GetValue(record));
            var point = mapper.GetMethod("ToStruct")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(4, point.GetType().GetProperty("X")!.GetValue(point));
            Assert.AreEqual(5, point.GetType().GetProperty("Y")!.GetValue(point));
        }

        [TestMethod]
        public void RequiredMembersNeedSatisfactionUnlessConstructorSetsRequiredMembers()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target { public required string Name { get; init; } }
").RunResult, "LITEMAPPER1002");

            var result = RunGenerator(@"
using System.Diagnostics.CodeAnalysis;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { }
public sealed class Target
{
    [SetsRequiredMembers]
    public Target() { Name = ""set""; }
    public required string Name { get; init; }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var target = assembly.GetType("Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { assembly.CreateInstance("Source") })!;
            Assert.AreEqual("set", target.GetType().GetProperty("Name")!.GetValue(target));
        }

        [TestMethod]
        public void PrivateConstructorIsUsableWhenMapperIsNestedInTargetType()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

public sealed partial class Target
{
    private Target() { }
    public int Id { get; init; }

    [LiteMapper]
    public static partial class Mapper
    {
        public static partial Target ToTarget(Source source);
    }
}

public sealed class Source { public int Id { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            StringAssert.Contains(SingleGeneratedSource(result.RunResult), "new Target()");
            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Id")!.SetValue(source, 3);
            var target = assembly.GetType("Target+Mapper")!.GetMethod("ToTarget")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(3, target.GetType().GetProperty("Id")!.GetValue(target));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ConstructorSelectionUsesGreatestArityUnlessExplicitlyMarked(bool marked)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
public class Target
{
    public static int Selected;
    " + (marked ? "[MappingConstructor]" : string.Empty) + @" public Target(int id) { Id = id; Selected = 1; }
    public Target(int id, string name) { Id = id; Selected = 2; }
    public int Id { get; }
}
public static class Probe { public static bool Run() => Mapper.Map(new Source { Id = 3, Name = ""Ada"" }).Id == 3 && Target.Selected == " + (marked ? "1" : "2") + "; }");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            Assert.AreEqual(true, Emit(result.Compilation).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        [TestMethod]
        public void ReadOnlyFieldsAreInitializedThroughTheSelectedConstructor()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int Value { get; set; } }
public class Target { public Target(int value) { Value = value; } public readonly int Value; }
public static class Probe { public static bool Run() => Mapper.Map(new Source { Value = 3 }).Value == 3; }");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            Assert.AreEqual(true, Emit(result.Compilation).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        [TestMethod]
        [DataRow("Error")]
        [DataRow("Warning")]
        [DataRow("Ignore")]
        public void UnboundReadOnlyFieldCannotReceiveAnAssignment(string policy)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy." + policy + @")] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int Value { get; set; } }
public class Target { public readonly int Value; }
");
            if (policy != "Ignore")
            {
                AssertDiagnostic(result.RunResult, "LITEMAPPER1001");
                Assert.AreEqual(policy == "Error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    result.RunResult.Diagnostics.Single(d => d.Id == "LITEMAPPER1001").Severity);
            }
            else
            {
                AssertNoLiteMapperDiagnostics(result.RunResult);
            }
            // Ordinary configurable diagnostics retain legal generated code, including when configured as errors.
            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Value")!.SetValue(source, 3);
            var target = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { source })!;
            Assert.AreEqual(0, target.GetType().GetField("Value")!.GetValue(target), "An ordinary unbound read-only field must retain its value.");
        }

        [TestMethod]
        public void NestedUnboundReadOnlyFieldUsesTargetPolicy()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Error)] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public class Target { public ChildTarget Child { get; set; } = new ChildTarget(); }
public class ChildSource { public int Value { get; set; } }
public class ChildTarget { public readonly int Value; }");
            AssertDiagnostic(result.RunResult, "LITEMAPPER1001");
            Emit(result.Compilation);
        }

        [TestMethod]
        public void AccessibleRecordCopyConstructorDoesNotCompeteWithMappingConstruction()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
public partial record Target(int Value)
{
    protected Target(Target original) { Value = original.Value + 100; }
    [LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
}
public class Source { public int Value { get; set; } public Target Original { get; set; } = new Target(7); }
public static class Probe { public static bool Run() => Target.Mapper.Map(new Source { Value = 3 }).Value == 3; }");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            Assert.AreEqual(true, Emit(result.Compilation).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id), "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(d => d.Id)));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(static d => d.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString())));
        }

        private static GeneratorRun RunGenerator(string source, LanguageVersion languageVersion = LanguageVersion.CSharp11)
        {
            var compilation = CreateCompilation(source, languageVersion);
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid construction fixture: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(languageVersion),
                optionsProvider: new TestAnalyzerConfigOptionsProvider(),
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static Compilation CreateCompilation(string source, LanguageVersion languageVersion)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Console.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });

            return CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(languageVersion)) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString())));
            stream.Position = 0;
            return Assembly.Load(stream.ToArray());
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
