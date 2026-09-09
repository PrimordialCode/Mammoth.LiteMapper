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
    public sealed class ConfiguredPathNestedContractTests
    {
        [TestMethod]
        public void NullableNestedSegmentMapsToNullForNullableNestedTarget()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Child"", Target = nameof(Target.Child))]
    public static partial Target Map(Source source);
}

public sealed class Source
{
    public Data Data { get; } = new Data();
}

public sealed class Data
{
    public ChildSource? Child { get; set; }
}

public sealed class ChildSource
{
    public int Value { get; set; }
}

public sealed class Target
{
    public ChildTarget? Child { get; set; }
}

public sealed class ChildTarget
{
    public int Value { get; set; }
}

public static class Probe
{
    public static bool Run() => Mapper.Map(new Source()).Child == null;
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result,
                "Specification 9.4 and 14.3 require a null encountered after a non-null path segment to map to a nullable nested target as null.");
        }

        [TestMethod]
        public void PresentNullableNestedPathEvaluatesEachGetterOnce()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Child"", Target = nameof(Target.Child))]
    public static partial Target Map(Source source);
}

public sealed class Source
{
    private readonly Data? data;

    public Source(Data? data)
    {
        this.data = data;
    }

    public int DataReads { get; private set; }

    public Data? Data
    {
        get
        {
            DataReads++;
            return data;
        }
    }
}

public sealed class Data
{
    private readonly ChildSource? child;

    public Data(ChildSource? child)
    {
        this.child = child;
    }

    public int ChildReads { get; private set; }

    public ChildSource? Child
    {
        get
        {
            ChildReads++;
            return child;
        }
    }
}

public sealed class ChildSource
{
    public int Value { get; set; }
}

public sealed class Target
{
    public ChildTarget? Child { get; set; }
}

public sealed class ChildTarget
{
    public int Value { get; set; }
}

public static class Probe
{
    public static bool Run()
    {
        var data = new Data(new ChildSource { Value = 7 });
        var source = new Source(data);
        var target = Mapper.Map(source);
        return target.Child?.Value == 7 && source.DataReads == 1 && data.ChildReads == 1;
    }
}
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            AssertProbe(result,
                "Specification 9.4 requires every configured source-path getter to be evaluated no more than once.");
        }

        private static void AssertNoLiteMapperDiagnostics(GeneratorDriverRunResult result)
        {
            var diagnostics = result.Diagnostics
                .Where(static diagnostic => diagnostic.Id.StartsWith("LITEMAPPER", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(0, diagnostics.Length,
                string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        private static void AssertProbe(GeneratorRun result, string message)
        {
            var assembly = Emit(result.Compilation);
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null), message);
        }

        private static GeneratorRun RunGenerator(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "ConfiguredPathNestedContractTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static Assembly Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success,
                string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
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
