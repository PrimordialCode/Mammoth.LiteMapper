using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class GenerationSafetyContractTests
    {
        [TestMethod]
        public void ConfiguredNullableSourcePathEvaluatesEveryGetterAtMostOnce()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Code))]
    public static partial Target Map(Source source);
}

public sealed class Source
{
    public static int AddressReads;
    private readonly Address address = new Address();
    public Address? Address { get { AddressReads++; return address; } }
}

public sealed class Address
{
    public static int CountryReads;
    private readonly Country country = new Country();
    public Country? Country { get { CountryReads++; return country; } }
}

public sealed class Country
{
    public static int CodeReads;
    public string Code { get { CodeReads++; return ""ready""; } }
}

public sealed class Target { public string Code { get; set; } = string.Empty; }

public static class Probe
{
    public static bool Run()
    {
        Source.AddressReads = 0;
        Address.CountryReads = 0;
        Country.CodeReads = 0;
        var target = Mapper.Map(new Source());
        return target.Code == ""ready"" && Source.AddressReads == 1 && Address.CountryReads == 1 && Country.CodeReads == 1;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void ConfiguredNullableSourcePathPreservesNullAndEvaluatesGettersOnce()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Code))]
    public static partial Target Map(Source source);
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
    private readonly Country country = new Country();
    public Country? Country { get { CountryReads++; return country; } }
}

public sealed class Country
{
    public static int CodeReads;
    public string Code { get { CodeReads++; return ""ready""; } }
}

public sealed class Target { public string? Code { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        Source.AddressReads = 0;
        Address.CountryReads = 0;
        Country.CodeReads = 0;
        var missing = Mapper.Map(new Source(null));
        if (missing.Code != null || Source.AddressReads != 1 || Address.CountryReads != 0 || Country.CodeReads != 0) return false;
        var present = Mapper.Map(new Source(new Address()));
        return present.Code == ""ready"" && Source.AddressReads == 2 && Address.CountryReads == 1 && Country.CodeReads == 1;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void ConverterBackedNullableSourcePathThrowsBeforeInvokingConverter()
        {
            var result = RunGenerator(@"
#nullable enable
using System;
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    public static int ConverterCalls;

    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Code), Use = nameof(ConvertCode))]
    public static partial Target Map(Source source);

    private static string ConvertCode(string code)
    {
        ConverterCalls++;
        return code + ""!"";
    }
}

public sealed class Source { public Address? Address { get; set; } }
public sealed class Address { public Country? Country { get; set; } }
public sealed class Country { public string Code { get; set; } = string.Empty; }
public sealed class Target { public string Code { get; set; } = string.Empty; }

public static class Probe
{
    public static bool Run()
    {
        Mapper.ConverterCalls = 0;
        try
        {
            Mapper.Map(new Source { Address = null });
            return false;
        }
        catch (InvalidOperationException exception)
        {
            return Mapper.ConverterCalls == 0 && exception.Message.Contains(""Address.Country.Code"", StringComparison.Ordinal);
        }
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void ConverterBackedNullableSourcePathReportsErrorUnderDefaultPolicy()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = ""Address.Country.Code"", Target = nameof(Target.Code), Use = nameof(ConvertCode))]
    public static partial Target Map(Source source);

    private static string ConvertCode(string code) => code;
}

public sealed class Source { public Address? Address { get; set; } }
public sealed class Address { public Country? Country { get; set; } }
public sealed class Country { public string Code { get; set; } = string.Empty; }
public sealed class Target { public string Code { get; set; } = string.Empty; }
");

            AssertDiagnostic(result.RunResult, "LITEMAPPER2001");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        public void KeywordMemberNamesAreEscapedInGeneratedAccess()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}

public sealed class Source { public int @class { get; set; } }
public sealed class Target { public int @class { get; set; } }

public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { @class = 17 }).@class == 17;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            StringAssert.Contains(SingleGeneratedSource(result.RunResult), "source.@class");
            AssertProbe(result);
        }

        [TestMethod]
        public void ExplicitConverterTypeUsesAQualifiedEscapedInvocation()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

namespace Tools
{
    public static class External
    {
        public static int @class(int value) => value + 10;
    }
}

public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value),
        Use = ""class"", ConverterType = typeof(Tools.External))]
    public static partial Target Map(Source source);
}

public static class Probe
{
    public static bool Run() => Mapper.Map(new Source { Value = 3 }).Value == 13;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            StringAssert.Contains(SingleGeneratedSource(result.RunResult), "global::Tools.External.@class(source.Value)");
            AssertProbe(result);
        }

        [TestMethod]
        public void EqualSimpleNestedTypeNamesUseDistinctClosedHelpers()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source source);
}

public sealed class Source
{
    public Alpha.Item Alpha { get; set; } = new Alpha.Item { Value = 3 };
    public Beta.Item Beta { get; set; } = new Beta.Item { Value = 7 };
}

public sealed class Target
{
    public Alpha.ItemDto Alpha { get; set; } = null!;
    public Beta.ItemDto Beta { get; set; } = null!;
}

namespace Alpha { public sealed class Item { public int Value { get; set; } } public sealed class ItemDto { public int Value { get; set; } } }
namespace Beta { public sealed class Item { public int Value { get; set; } } public sealed class ItemDto { public int Value { get; set; } } }

public static class Probe
{
    public static bool Run()
    {
        var target = Mapper.Map(new Source());
        return target.Alpha.Value == 3 && target.Beta.Value == 7;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            Assert.AreEqual(2, generated.Split(new[] { "private static" }, StringSplitOptions.None).Length - 1, generated);
            StringAssert.Contains(generated, "Alpha.Item");
            StringAssert.Contains(generated, "Beta.Item");
            AssertProbe(result);
        }

        [TestMethod]
        public void CollectionMappingUsesTheDeclaredSourceParameterName()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial List<int> Map(List<int> values);
}

public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new List<int> { 3, 7 });
        return mapped.Count == 2 && mapped[0] == 3 && mapped[1] == 7;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        private static void AssertProbe(GeneratorRun result)
        {
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static void AssertDiagnostic(GeneratorDriverRunResult result, string id)
        {
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Id == id),
                "Expected " + id + " but found " + string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Id)));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        private static string SingleGeneratedSource(GeneratorDriverRunResult result)
        {
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            return result.GeneratedTrees[0].GetText().ToString();
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "GenerationSafetyContractTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static MetadataReference[] References()
        {
            return AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) })
                .ToArray();
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
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
    }
}
