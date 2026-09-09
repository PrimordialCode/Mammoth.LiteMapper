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
    public sealed class ConfiguredPathPatchContractTests
    {
        // SPECIFICATION 9.4 requires single evaluation; 16.3 requires null paths to preserve the target.
        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        public void DirectAssignmentReadsEachConfiguredPathSegmentOnceAndSkipsNull(int nullSegment)
        {
            var actual = Execute(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Code))]
    public static partial void Apply(Source source, Target target);
}

public sealed class Source
{
    public static int AddressReads;
    private readonly Address? address;
    public Source(Address? address) { this.address = address; }
    public Address? Address { get { AddressReads++; return address; } }
}

public sealed class Address
{
    public static int CountryReads;
    private readonly Country? country;
    public Address(Country? country) { this.country = country; }
    public Country? Country { get { CountryReads++; return country; } }
}

public sealed class Country
{
    public static int CodeReads;
    private readonly string? code;
    public Country(string? code) { this.code = code; }
    public string? Code { get { CodeReads++; return code; } }
}

public sealed class Target
{
    private string code = ""retained"";
    private int writes;
    public string Code { get => code; set { writes++; code = value; } }
    public int Writes() => writes;
}

public static class Probe
{
    public static int[] Run(int nullSegment)
    {
        Source.AddressReads = Address.CountryReads = Country.CodeReads = 0;
        var country = nullSegment == 1 ? null : new Country(nullSegment == 2 ? null : ""ready"");
        var source = new Source(nullSegment == 0 ? null : new Address(country));
        var target = new Target();
        Mapper.Apply(source, target);
        return new[]
        {
            Source.AddressReads, Address.CountryReads, Country.CodeReads, target.Writes(),
            target.Code == (nullSegment == 3 ? ""ready"" : ""retained"") ? 1 : 0
        };
    }
}
", nullSegment);

            CollectionAssert.AreEqual(new[]
            {
                1, nullSegment == 0 ? 0 : 1, nullSegment < 2 ? 0 : 1,
                nullSegment == 3 ? 1 : 0, 1
            }, actual, "A patch must read only the traversed segments once and must not call the setter for a null path.");
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        public void SelectedNestedUpdaterReadsEachConfiguredPathSegmentOnceAndSkipsNull(int nullSegment)
        {
            var actual = Execute(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    public static int UpdaterCalls;

    [MapProperty(Source = ""Address.Country.Child"", Target = nameof(Target.Child), Use = nameof(UpdateChild))]
    public static partial void Apply(Source source, Target target);

    private static void UpdateChild(ChildSource source, ChildTarget target)
    {
        UpdaterCalls++;
        target.Value = source.Value;
    }
}

public sealed class Source
{
    public static int AddressReads;
    private readonly Address? address;
    public Source(Address? address) { this.address = address; }
    public Address? Address { get { AddressReads++; return address; } }
}

public sealed class Address
{
    public static int CountryReads;
    private readonly Country? country;
    public Address(Country? country) { this.country = country; }
    public Country? Country { get { CountryReads++; return country; } }
}

public sealed class Country
{
    public static int ChildReads;
    private readonly ChildSource? child;
    public Country(ChildSource? child) { this.child = child; }
    public ChildSource? Child { get { ChildReads++; return child; } }
}

public sealed class ChildSource { public int Value { get; set; } }
public sealed class ChildTarget { public int Value { get; set; } }
public sealed class Target { public ChildTarget Child { get; } = new ChildTarget { Value = 7 }; }

public static class Probe
{
    public static int[] Run(int nullSegment)
    {
        Source.AddressReads = Address.CountryReads = Country.ChildReads = Mapper.UpdaterCalls = 0;
        var country = nullSegment == 1 ? null : new Country(nullSegment == 2 ? null : new ChildSource { Value = 3 });
        var source = new Source(nullSegment == 0 ? null : new Address(country));
        var target = new Target();
        var originalChild = target.Child;
        Mapper.Apply(source, target);
        return new[]
        {
            Source.AddressReads, Address.CountryReads, Country.ChildReads, Mapper.UpdaterCalls,
            ReferenceEquals(originalChild, target.Child) ? 1 : 0,
            target.Child.Value == (nullSegment == 3 ? 3 : 7) ? 1 : 0
        };
    }
}
", nullSegment);

            CollectionAssert.AreEqual(new[]
            {
                1, nullSegment == 0 ? 0 : 1, nullSegment < 2 ? 0 : 1,
                nullSegment == 3 ? 1 : 0, 1, 1
            }, actual, "A selected updater must consume the captured non-null child once, preserving the existing child when traversal encounters null.");
        }

        private static int[] Execute(string source, int nullSegment)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "ConfiguredPathPatchContract_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            var diagnostics = driver.GetRunResult().Diagnostics;
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));

            using var stream = new MemoryStream();
            var emitResult = updatedCompilation.Emit(stream);
            Assert.IsTrue(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            return (int[])assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, new object[] { nullSegment })!;
        }
    }
}
