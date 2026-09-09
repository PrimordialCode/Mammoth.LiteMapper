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
    public sealed class ConfiguredPathAdditionalPatchTests
    {
        // SPECIFICATION 9.4 requires single evaluation; 16.3 requires a null leaf to preserve the target.
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void PatchWithNullableLeafReadsNonNullableIntermediatesOnce(bool hasValue)
        {
            var actual = Execute<int[]>(@"
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
    private readonly Address address;
    public Source(Address address) { this.address = address; }
    public Address Address { get { AddressReads++; return address; } }
}

public sealed class Address
{
    public static int CountryReads;
    private readonly Country country;
    public Address(Country country) { this.country = country; }
    public Country Country { get { CountryReads++; return country; } }
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
    public static int[] Run(bool hasValue)
    {
        Source.AddressReads = Address.CountryReads = Country.CodeReads = 0;
        var target = new Target();
        Mapper.Apply(new Source(new Address(new Country(hasValue ? ""ready"" : null))), target);
        return new[]
        {
            Source.AddressReads,
            Address.CountryReads,
            Country.CodeReads,
            target.Writes(),
            target.Code == (hasValue ? ""ready"" : ""retained"") ? 1 : 0
        };
    }
}
", hasValue);

            CollectionAssert.AreEqual(
                new[] { 1, 1, 1, hasValue ? 1 : 0, 1 },
                actual,
                "The patch guard and assignment must share one captured nullable leaf value.");
        }

        // SPECIFICATION 10.5 and 16.2 require valid construction; 16.3 preserves an existing member on null.
        [TestMethod]
        public void NullableDestinationCreationInitializesRequiredPatchMemberBeforeGuardedUpdates()
        {
            var actual = Execute<bool>(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    [MapProperty(Source = ""Payload.Name"", Target = nameof(Target.Name))]
    public static partial Target Apply(Source source, Target? destination);
}

public sealed class Source
{
    public Source(Payload payload) { Payload = payload; }
    public Payload Payload { get; }
}

public sealed class Payload
{
    public Payload(string? name) { Name = name; }
    public string? Name { get; }
}

public sealed class Target
{
    public required string? Name { get; set; }
}

public static class Probe
{
    public static bool Run()
    {
        var createdFromValue = Mapper.Apply(new Source(new Payload(""ready"")), null);
        var createdFromNull = Mapper.Apply(new Source(new Payload(null)), null);
        var existing = new Target { Name = ""retained"" };
        var returned = Mapper.Apply(new Source(new Payload(null)), existing);

        return createdFromValue.Name == ""ready""
            && createdFromNull.Name is null
            && object.ReferenceEquals(existing, returned)
            && returned.Name == ""retained"";
    }
}
");

            Assert.IsTrue(actual,
                "Creation must satisfy the required nullable member, while a null patch value must leave an existing destination unchanged.");
        }

        // SPECIFICATION 10.5 and 16.2 require a present nullable value to satisfy required-member construction.
        [TestMethod]
        public void NullableDestinationCreationUnwrapsPresentValueForRequiredValueMember()
        {
            var actual = Execute<int>(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    [MapProperty(Source = ""Payload.Number"", Target = nameof(Target.Number))]
    public static partial Target Apply(Source source, Target? destination);
}

public sealed class Source
{
    public Payload Payload { get; } = new Payload();
}

public sealed class Payload
{
    public int? Number { get; set; }
}

public sealed class Target
{
    public required int Number { get; set; }
}

public static class Probe
{
    public static int Run()
    {
        return Mapper.Apply(new Source { Payload = { Number = 3 } }, null).Number;
    }
}
");

            Assert.AreEqual(3, actual,
                "A present nullable configured value must be unwrapped when it initializes a required non-nullable value member.");
        }

        // SPECIFICATION 9.4 requires single evaluation; 12.4 requires the full path to throw before the updater runs.
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ThrowingNestedUpdaterReadsConfiguredPathOnceAndThrowsBeforeCallingUpdater(bool hasValue)
        {
            var actual = Execute<int[]>(@"
#nullable enable
using System;
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
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
    public static int[] Run(bool hasValue)
    {
        Source.AddressReads = Address.CountryReads = Country.ChildReads = Mapper.UpdaterCalls = 0;
        var source = new Source(new Address(new Country(hasValue ? new ChildSource { Value = 3 } : null)));
        var target = new Target();
        var expectedOutcome = 0;

        try
        {
            Mapper.Apply(source, target);
            expectedOutcome = hasValue ? 1 : 0;
        }
        catch (InvalidOperationException exception)
        {
            expectedOutcome = !hasValue && exception.Message.Contains(""Address.Country.Child"") ? 1 : 0;
        }

        return new[]
        {
            Source.AddressReads,
            Address.CountryReads,
            Country.ChildReads,
            Mapper.UpdaterCalls,
            target.Child.Value == (hasValue ? 3 : 7) ? 1 : 0,
            expectedOutcome
        };
    }
}
", hasValue);

            CollectionAssert.AreEqual(
                new[] { 1, 1, 1, hasValue ? 1 : 0, 1, 1 },
                actual,
                "Throw policy must use one captured path value and must reject null before invoking the updater.");
        }

        private static T Execute<T>(string source, params object[] arguments)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "ConfiguredPathAdditionalPatch_" + Guid.NewGuid().ToString("N"),
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
            return (T)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, arguments)!;
        }
    }
}
