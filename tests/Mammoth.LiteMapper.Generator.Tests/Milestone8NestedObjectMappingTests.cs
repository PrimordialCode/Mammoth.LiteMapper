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
    public sealed class Milestone8NestedObjectMappingTests
    {
        [TestMethod]
        public void NestedObjectMappingGeneratesPrivateClosedHelperAndRuns()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial CustomerDto ToDto(Customer source);
}

public sealed class Customer { public string Name { get; set; } = string.Empty; public Address Address { get; set; } = new Address(); }
public sealed class Address { public string City { get; set; } = string.Empty; }
public sealed class CustomerDto { public string Name { get; set; } = string.Empty; public AddressDto Address { get; set; } = new AddressDto(); }
public sealed class AddressDto { public string City { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Matches(generated, new System.Text.RegularExpressions.Regex(
                "Address = MapNested_Address_To_AddressDto_[0-9A-F]{8}\\(source\\.Address\\)"));
            StringAssert.Matches(generated, new System.Text.RegularExpressions.Regex(
                "private static AddressDto MapNested_Address_To_AddressDto_[0-9A-F]{8}\\(Address source\\)"));
            StringAssert.Contains(generated, "City = source.City");
            Assert.AreEqual(
                Normalize(File.ReadAllText(Repository.Path("tests/Mammoth.LiteMapper.Generator.Tests/Snapshots/Milestone8NestedMapping.Mapper.g.cs"))),
                Normalize(generated));
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Customer")!;
            source.GetType().GetProperty("Name")!.SetValue(source, "Ada");
            var address = source.GetType().GetProperty("Address")!.GetValue(source)!;
            address.GetType().GetProperty("City")!.SetValue(address, "Rome");

            var target = assembly.GetType("Mapper")!.GetMethod("ToDto")!.Invoke(null, new[] { source })!;
            var targetAddress = target.GetType().GetProperty("Address")!.GetValue(target)!;
            Assert.AreEqual("Ada", target.GetType().GetProperty("Name")!.GetValue(target));
            Assert.AreEqual("Rome", targetAddress.GetType().GetProperty("City")!.GetValue(targetAddress));
        }

        [TestMethod]
        public void ExistingVisibleMappingIsReusedBeforeStructuralHelper()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial CustomerDto ToDto(Customer source);
    [DefaultMapping]
    private static AddressDto ToAddressDto(Address source) => new AddressDto { City = ""mapped:"" + source.City };
    private static AddressDto UnrelatedHelper(Address source) => new AddressDto { City = ""unrelated"" };
}

public sealed class Customer { public Address Address { get; set; } = new Address(); }
public sealed class Address { public string City { get; set; } = string.Empty; }
public sealed class CustomerDto { public AddressDto Address { get; set; } = new AddressDto(); }
public sealed class AddressDto { public string City { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "Address = ToAddressDto(source.Address)");
            Assert.IsFalse(generated.Contains("MapNested_Address_To_AddressDto", StringComparison.Ordinal), generated);

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Customer")!;
            var target = assembly.GetType("Mapper")!.GetMethod("ToDto")!.Invoke(null, new[] { source })!;
            var address = target.GetType().GetProperty("Address")!.GetValue(target)!;
            Assert.AreEqual("mapped:", address.GetType().GetProperty("City")!.GetValue(address), "The declared default must win without considering an unrelated helper.");
        }

        [TestMethod]
        [DataRow("private")]
        [DataRow("public")]
        public void UnmarkedStructuralHelperDoesNotReplaceGeneratedMapping(string accessibility)
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
    " + accessibility + @" static ChildDto ToChild(Child source) => new ChildDto { Id = 99 };
}
public sealed class Source { public Child Child { get; set; } = new Child { Id = 7 }; }
public sealed class Child { public int Id { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Id { get; set; } }
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            var target = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { source })!;
            var child = target.GetType().GetProperty("Child")!.GetValue(target)!;
            Assert.AreEqual(7, child.GetType().GetProperty("Id")!.GetValue(child), "Neither accessibility nor a mapping-like name opts an unmarked helper into resolution.");
        }

        [TestMethod]
        public void UniqueDefaultMappingWinsOverEligiblePartialMappingForNestedPair()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
    private static partial ChildDto GeneratedChild(Child source);
    [DefaultMapping]
    private static ChildDto PreferredChild(Child source) => new ChildDto { Id = source.Id + 10 };
}
public sealed class Source { public Child Child { get; set; } = new Child { Id = 7 }; }
public sealed class Child { public int Id { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Id { get; set; } }
");
            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            var target = assembly.GetType("Mapper")!.GetMethod("Map")!.Invoke(null, new[] { source })!;
            var child = target.GetType().GetProperty("Child")!.GetValue(target)!;
            Assert.AreEqual(17, child.GetType().GetProperty("Id")!.GetValue(child), "Section 11.2 selects the unique default even when another eligible mapping exists.");
        }

        [TestMethod]
        public void DuplicateDefaultMappingsForNestedPairReportDuplicateDefaultDiagnostic()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
    [DefaultMapping]
    private static ChildDto First(Child source) => new ChildDto { Id = 1 };
    [DefaultMapping]
    private static ChildDto Second(Child source) => new ChildDto { Id = 2 };
}
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { public int Id { get; set; } }
public sealed class Target { public ChildDto Child { get; set; } = null!; }
public sealed class ChildDto { public int Id { get; set; } }
");
            AssertDiagnostic(result.RunResult, "LITEMAPPER3002");
        }

        [TestMethod]
        public void NullableNestedObjectFollowsNullablePolicy()
        {
            var error = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial CustomerDto ToDto(Customer source);
}

public sealed class Customer { public Address? Address { get; set; } }
public sealed class Address { public string? City { get; set; } }
public sealed class CustomerDto { public AddressDto Address { get; set; } = new AddressDto(); }
public sealed class AddressDto { public string? City { get; set; } }
");

            AssertDiagnostic(error.RunResult, "LITEMAPPER2001");

            var nullableTarget = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial CustomerDto ToDto(Customer source);
}

public sealed class Customer { public Address? Address { get; set; } }
public sealed class Address { public string? City { get; set; } }
public sealed class CustomerDto { public AddressDto? Address { get; set; } }
public sealed class AddressDto { public string? City { get; set; } }
");

            AssertNoLiteMapperDiagnostics(nullableTarget.RunResult);
            StringAssert.Matches(SingleGeneratedSource(nullableTarget.RunResult), new System.Text.RegularExpressions.Regex(
                "Address = source\\.Address is \\{ \\} (__sourcePath_Address_[0-9A-F]{8}) \\? MapNested_Address_To_AddressDto_[0-9A-F]{8}\\(\\1\\) : null"));
        }

        [TestMethod]
        public void AbstractObjectAndAmbiguousMappingsReportDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public ISource Child { get; set; } = new SourceChild(); }
public sealed class SourceChild : ISource { public int Id { get; set; } }
public interface ISource { int Id { get; set; } }
public sealed class Target { public ITarget Child { get; set; } = null!; }
public interface ITarget { int Id { get; set; } }
").RunResult, "LITEMAPPER3005");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public object Child { get; set; } = new object(); }
public sealed class Target { public ChildDto Child { get; set; } = new ChildDto(); }
public sealed class ChildDto { }
").RunResult, "LITEMAPPER2012");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
    private static partial ChildDto One(Child source);
    private static partial ChildDto Two(Child source);
}
public sealed class Source { public Child Child { get; set; } = new Child(); }
public sealed class Child { }
public sealed class Target { public ChildDto Child { get; set; } = new ChildDto(); }
public sealed class ChildDto { }
").RunResult, "LITEMAPPER3001");
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static void AssertNoRuntimeFeatures(string source)
        {
            Assert.IsFalse(source.Contains("System.Reflection", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("dynamic", StringComparison.Ordinal), source);
            Assert.IsFalse(source.Contains("Assembly", StringComparison.Ordinal), source);
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").TrimEnd();
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
