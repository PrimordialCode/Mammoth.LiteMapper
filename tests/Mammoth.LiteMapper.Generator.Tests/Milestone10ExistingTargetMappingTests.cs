using System;
using System.Collections.Generic;
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
    public sealed class Milestone10ExistingTargetMappingTests
    {
        [TestMethod]
        public void VoidAndReturningReferenceUpdatesMutateExistingTarget()
        {
            var result = RunGenerator(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial void Apply(Source source, Target target);
    public static partial Target ApplyReturn(Source source, Target target);
}

public sealed class Source { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
public sealed class Target { public int Id { get; set; } public string Name { get; set; } = string.Empty; }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "target.Id = source.Id;");
            StringAssert.Contains(generated, "return target;");
            Assert.AreEqual(
                Normalize(File.ReadAllText(Repository.Path("tests/Mammoth.LiteMapper.Generator.Tests/Snapshots/Milestone10ExistingTarget.Mapper.g.cs"))),
                Normalize(generated));
            AssertNoRuntimeFeatures(generated);

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Id")!.SetValue(source, 42);
            source.GetType().GetProperty("Name")!.SetValue(source, "updated");
            var target = assembly.CreateInstance("Target")!;
            assembly.GetType("Mapper")!.GetMethod("Apply")!.Invoke(null, new[] { source, target });
            Assert.AreEqual(42, target.GetType().GetProperty("Id")!.GetValue(target));
            Assert.AreEqual("updated", target.GetType().GetProperty("Name")!.GetValue(target));

            var returned = assembly.GetType("Mapper")!.GetMethod("ApplyReturn")!.Invoke(null, new[] { source, target });
            Assert.AreSame(target, returned);
        }

        [TestMethod]
        public void RefStructUpdateAndNullableReturningDestinationAreSupported()
        {
            var result = RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial void Apply(Source source, ref TargetStruct target);
    public static partial Target ApplyNullable(Source source, Target? target);
}

public sealed class Source { public int Id { get; set; } }
public struct TargetStruct { public int Id; }
public sealed class Target { public int Id { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            source.GetType().GetProperty("Id")!.SetValue(source, 9);
            var created = assembly.GetType("Mapper")!.GetMethod("ApplyNullable")!.Invoke(null, new object?[] { source, null })!;
            Assert.AreEqual(9, created.GetType().GetProperty("Id")!.GetValue(created));
        }

        [TestMethod]
        public void NestedCollectionAndPatchUpdateSemanticsAreGenerated()
        {
            var result = RunGenerator(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    [MapProperty(Source = ""Child.Name"", Target = nameof(Target.ChildName))]
    public static partial void Apply(Source source, Target target);
}

public sealed class Source
{
    public string? Name { get; set; }
    public Child? Child { get; set; }
    public List<Item>? Items { get; set; }
}
public sealed class Child { public string? Name { get; set; } }
public sealed class Item { public int Id { get; set; } }
public sealed class Target
{
    public string Name { get; set; } = ""keep"";
    public string ChildName { get; set; } = ""child"";
    public List<ItemDto> Items { get; set; } = new List<ItemDto> { new ItemDto { Id = 1 } };
}
public sealed class ItemDto { public int Id { get; set; } }
");

            AssertNoLiteMapperDiagnostics(result.RunResult);
            var generated = SingleGeneratedSource(result.RunResult);
            StringAssert.Contains(generated, "if (source.Name != null)");
            StringAssert.Contains(generated, "if (source.Child != null && source.Child.Name != null)");
            StringAssert.Contains(generated, "target.Items = MapCollection_List_Item_To_List_ItemDto(source.Items);");

            var assembly = Emit(result.Compilation);
            var source = assembly.CreateInstance("Source")!;
            var target = assembly.CreateInstance("Target")!;
            assembly.GetType("Mapper")!.GetMethod("Apply")!.Invoke(null, new[] { source, target });
            Assert.AreEqual("keep", target.GetType().GetProperty("Name")!.GetValue(target));
            Assert.AreEqual("child", target.GetType().GetProperty("ChildName")!.GetValue(target));
        }

        [TestMethod]
        public void InvalidUpdateDeclarationsReportMilestoneDiagnostics()
        {
            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Bad(Source source, Target target); }
public sealed class Source { public int Id { get; set; } }
public struct Target { public int Id; }
").RunResult, "LITEMAPPER5003");

            AssertDiagnostic(RunGenerator(@"
#nullable enable
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial void Bad(Source source, Target? target); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public int Id { get; set; } }
").RunResult, "LITEMAPPER5002");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial void Bad(Source source, Target target); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public int Id { get; init; } }
").RunResult, "LITEMAPPER5004");

            AssertDiagnostic(RunGenerator(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial void Bad(Source source, Target target); }
public sealed class Source { public List<int> Items { get; set; } = new List<int>(); }
public sealed class Target { public List<int> Items { get; } = new List<int>(); }
").RunResult, "LITEMAPPER5005");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial void Bad(int[] source, int[] target); }
").RunResult, "LITEMAPPER5001");

            AssertDiagnostic(RunGenerator(@"
using Mammoth.LiteMapper;
[LiteMapper(IgnoreNullSourceMembers = true)] public static partial class Mapper { public static partial Target ToTarget(Source source); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public int Id { get; set; } }
").RunResult, "LITEMAPPER5006");
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

        private static GeneratorRun RunGenerator(string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11),
                optionsProvider: new TestAnalyzerConfigOptionsProvider(),
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            return new GeneratorRun(driver.GetRunResult(), updatedCompilation);
        }

        private static Compilation CreateCompilation(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });

            return CSharpCompilation.Create(
                "Tests",
                new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11)) },
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
