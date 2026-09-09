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
    public sealed class ConfiguredPathCaptureEdgeTests
    {
        [TestMethod]
        public void PatchCaptureSatisfiesNonNullExplicitConverterParameter()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    public static int Calls;

    [MapProperty(Source = ""Address.Code"", Target = nameof(Target.Code), Use = nameof(Normalize))]
    public static partial void Apply(Source source, Target target);

    private static string Normalize(string value)
    {
        Calls++;
        return value + ""!"";
    }
}

public sealed class Source { public Address? Address { get; set; } }
public sealed class Address { public string? Code { get; set; } }
public sealed class Target { public string Code { get; set; } = ""original""; }

public static class Probe
{
    public static bool Run()
    {
        Mapper.Calls = 0;
        var target = new Target();
        Mapper.Apply(new Source { Address = null }, target);
        if (target.Code != ""original"" || Mapper.Calls != 0) return false;
        Mapper.Apply(new Source { Address = new Address { Code = ""ready"" } }, target);
        return target.Code == ""ready!"" && Mapper.Calls == 1;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void PatchCaptureUnwrapsNullableValueLeaf()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Value"", Target = nameof(Target.Value))]
    public static partial void Apply(Source source, Target target);
}

public sealed class Source { public Data? Data { get; set; } }
public sealed class Data { public int? Value { get; set; } }
public sealed class Target { public int Value { get; set; } = 7; }

public static class Probe
{
    public static bool Run()
    {
        var target = new Target();
        Mapper.Apply(new Source { Data = new Data { Value = null } }, target);
        if (target.Value != 7) return false;
        Mapper.Apply(new Source { Data = new Data { Value = 3 } }, target);
        return target.Value == 3;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void NullableTraversalContinuesThroughNonNullableValueSegment()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Point.X"", Target = nameof(Target.X))]
    public static partial Target Map(Source source);
}

public sealed class Source { public Data? Data { get; set; } }
public sealed class Data { public Point Point { get; set; } }
public struct Point { public int X { get; set; } }
public sealed class Target { public int? X { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        var missing = Mapper.Map(new Source { Data = null });
        var present = Mapper.Map(new Source { Data = new Data { Point = new Point { X = 3 } } });
        return missing.X == null && present.X == 3;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        [TestMethod]
        public void PatchNonNullablePathDoesNotEvaluateASeparateNullGuard()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Code"", Target = nameof(Target.Code))]
    public static partial void Apply(Source source, Target target);
}

public sealed class Source
{
    public static int DataReads;
    private readonly Data data = new Data();
    public Data Data { get { DataReads++; return data; } }
}

public sealed class Data
{
    public static int CodeReads;
    public string Code { get { CodeReads++; return ""ready""; } }
}

public sealed class Target { public string Code { get; set; } = ""original""; }

public static class Probe
{
    public static bool Run()
    {
        Source.DataReads = Data.CodeReads = 0;
        var target = new Target();
        Mapper.Apply(new Source(), target);
        return target.Code == ""ready"" && Source.DataReads == 1 && Data.CodeReads == 1;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result);
        }

        private static void AssertProbe(GeneratorRun result)
        {
            using var stream = new MemoryStream();
            var emit = result.Compilation.Emit(stream);
            Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(0, diagnostics.Length, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "ConfiguredPathCaptureEdgeTests",
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
