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
    public sealed class ConfiguredPathCollectionContractTests
    {
        [TestMethod]
        public void ContextualDefaultRejectsPathInducedNullableCollectionForNonNullableTarget()
        {
            var result = Run(CreateMappingSource(string.Empty, "List<int>"));

            AssertInvalidNullCollectionMapping(result,
                "The contextual default must reject Data.Items when nullable Data makes the collection path nullable.");
        }

        [TestMethod]
        public void ExplicitErrorRejectsPathInducedNullableCollectionForNonNullableTarget()
        {
            var result = Run(CreateMappingSource(
                "NullCollections = NullCollectionStrategy.Error",
                "List<int>"));

            AssertInvalidNullCollectionMapping(result,
                "Error must reject a collection made nullable by the Data.Items traversal.");
        }

        [TestMethod]
        public void ExplicitErrorRejectsPathInducedNullableCollectionForNullableTarget()
        {
            var result = Run(CreateMappingSource(
                "NullCollections = NullCollectionStrategy.Error",
                "List<int>?"));

            AssertInvalidNullCollectionMapping(result,
                "An explicit Error strategy rejects a nullable collection path even when the target is nullable.");
        }

        [TestMethod]
        public void PreserveRejectsPathInducedNullableCollectionForNonNullableTarget()
        {
            var result = Run(CreateMappingSource(
                "NullCollections = NullCollectionStrategy.Preserve",
                "List<int>"));

            AssertInvalidNullCollectionMapping(result,
                "Preserve cannot assign null from Data.Items to a non-nullable target collection.");
        }

        [TestMethod]
        public void PreserveMapsNullPathToNullableTargetAndMapsPresentCollection()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;

public sealed class Payload { public List<int> Items { get; set; } = new List<int>(); }
public sealed class Source { public Payload? Data { get; set; } }
public sealed class Target { public List<int>? Items { get; set; } }

[LiteMapper(NullCollections = NullCollectionStrategy.Preserve)]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Items"", Target = nameof(Target.Items))]
    public static partial Target Map(Source source);
}

public static class Probe
{
    public static bool Run()
    {
        var nullResult = Mapper.Map(new Source { Data = null });
        var presentResult = Mapper.Map(new Source
        {
            Data = new Payload { Items = new List<int> { 3 } }
        });
        return nullResult.Items == null &&
            presentResult.Items != null &&
            presentResult.Items.Count == 1 &&
            presentResult.Items[0] == 3;
    }
}
");

            AssertProbe(result);
        }

        [TestMethod]
        public void EmptyMapsNullPathToEmptyNonNullableTargetAndMapsPresentCollection()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;

public sealed class Payload { public List<int> Items { get; set; } = new List<int>(); }
public sealed class Source { public Payload? Data { get; set; } }
public sealed class Target { public List<int> Items { get; set; } = new List<int>(); }

[LiteMapper(NullCollections = NullCollectionStrategy.Empty)]
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Items"", Target = nameof(Target.Items))]
    public static partial Target Map(Source source);
}

public static class Probe
{
    public static bool Run()
    {
        var nullResult = Mapper.Map(new Source { Data = null });
        var presentResult = Mapper.Map(new Source
        {
            Data = new Payload { Items = new List<int> { 3 } }
        });
        return nullResult.Items.Count == 0 &&
            presentResult.Items.Count == 1 &&
            presentResult.Items[0] == 3;
    }
}
");

            AssertProbe(result);
        }

        private static string CreateMappingSource(string mapperOptions, string targetCollectionType)
        {
            var attribute = mapperOptions.Length == 0 ? "[LiteMapper]" : "[LiteMapper(" + mapperOptions + ")]";
            return @"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;

public sealed class Payload { public List<int> Items { get; set; } = new List<int>(); }
public sealed class Source { public Payload? Data { get; set; } }
public sealed class Target { public " + targetCollectionType + @" Items { get; set; } = null!; }

" + attribute + @"
public static partial class Mapper
{
    [MapProperty(Source = ""Data.Items"", Target = nameof(Target.Items))]
    public static partial Target Map(Source source);
}
";
        }

        private static void AssertInvalidNullCollectionMapping(
            (GeneratorDriverRunResult RunResult, Compilation Compilation) result,
            string message)
        {
            Assert.IsTrue(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER2002"),
                message + " Actual diagnostics: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER9001"),
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "An invalid configured-path collection mapping must not emit an implementation.");
        }

        private static void AssertProbe(
            (GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true,
                Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "ConfiguredPathCollectionContractTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
                .ToArray();
            Assert.AreEqual(0, inputErrors.Length,
                "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(static diagnostic => diagnostic.ToString())));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
