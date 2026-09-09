using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mammoth.LiteMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class IncrementalIsolationTests
    {
        private const string MapperA = "using Mammoth.LiteMapper; [LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)] public static partial class A { public static partial AT Map(AS source); }";
        private const string Models = "public sealed class AS { public int Value { get; set; } } public sealed class AT { public int Value { get; set; } public int Extra { get; set; } }";
        private const string MapperB = "using Mammoth.LiteMapper; [LiteMapper] public static partial class B { public static partial BT Map(BS source); } public sealed class BS { public int Value { get; set; } } public sealed class BT { public int Value { get; set; } }";

        [TestMethod]
        [DataRow("invalid", false)]
        [DataRow("remove", false)]
        [DataRow("add", false)]
        [DataRow("invalid", true)]
        [DataRow("remove", true)]
        [DataRow("add", true)]
        public void MapperPopulationChangesPreserveUnaffectedOutput(string change, bool sameFile)
        {
            var compilation = CreateCompilation();
            const string import = "using Mammoth.LiteMapper; ";
            var firstMapper = MapperA.Replace(import, string.Empty);
            var secondMapper = MapperB.Replace(import, string.Empty);
            if (sameFile)
            {
                // Keep B's location fixed so this checks mapper identity independently of diagnostic relocation.
                compilation = compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(static tree => tree.FilePath == "A.cs" || tree.FilePath == "B.cs"))
                    .AddSyntaxTrees(CSharpSyntaxTree.ParseText(import + (change == "add" ? new string(' ', firstMapper.Length) : firstMapper) + secondMapper, ParseOptions(), "Mappers.cs"));
            }
            else if (change == "add")
            {
                compilation = compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Single(tree => tree.FilePath == "A.cs"));
            }

            var first = CreateDriver().RunGenerators(compilation);
            var changed = sameFile
                ? ReplaceTree(compilation, "Mappers.cs", import + (change == "remove" ? new string(' ', firstMapper.Length) : change == "invalid" ? firstMapper.Replace("partial class A", "        class A") : firstMapper) + secondMapper)
                : change == "invalid"
                ? ReplaceTree(compilation, "A.cs", MapperA.Replace("static partial class A", "static class A"))
                : change == "remove"
                    ? compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Single(tree => tree.FilePath == "A.cs"))
                    : compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(MapperA, ParseOptions(), "A.cs"));
            var second = first.RunGenerators(changed);
            if (sameFile && change != "invalid")
            {
                AssertRecreatedOutput(second, "B");
            }
            else
            {
                AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            }
            AssertStableFile(first, second, "B");
            var sources = second.GetRunResult().Results.Single().GeneratedSources;
            Assert.AreEqual(change == "add" ? 2 : 1, sources.Length);
            if (change == "invalid")
            {
                Assert.IsTrue(second.GetRunResult().Diagnostics.Any(static diagnostic => diagnostic.Id == "LITEMAPPER0001"));
            }
            else
            {
                AssertCompiles(second, changed);
            }
        }

        [TestMethod]
        public void UnrelatedDeclarationBeforeMapperDoesNotRegenerateUnchangedSource()
        {
            var compilation = CreateCompilation();
            var first = CreateDriver().RunGenerators(compilation);
            var changed = ReplaceTree(compilation, "B.cs", MapperB.Replace("[LiteMapper]", "public class UnrelatedBeforeMapper { } [LiteMapper]"));
            var second = first.RunGenerators(changed);
            AssertOutputReason(second, "A", IncrementalStepRunReason.Cached);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            AssertCompiles(second, changed);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RoslynRecreatesSameFileCandidatesEvenWithAttributeTargetedDiscovery(bool adding)
        {
            var compilation = CreateCompilation();
            var mapperTree = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "A.cs");
            const string firstMapper = "[Mammoth.LiteMapper.LiteMapper] public partial class ProbeA { } ";
            const string secondMapper = "[Mammoth.LiteMapper.LiteMapper] public partial class ProbeB { }";
            compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText((adding ? new string(' ', firstMapper.Length) : firstMapper) + secondMapper, ParseOptions(), mapperTree.FilePath));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new AttributeIdentityProbe().AsSourceGenerator() }, parseOptions: ParseOptions(),
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
            driver = driver.RunGenerators(compilation);
            var changed = ReplaceTree(compilation, mapperTree.FilePath, (adding ? firstMapper : new string(' ', firstMapper.Length)) + secondMapper);
            var second = driver.RunGenerators(changed);
            AssertRecreatedOutput(second, "ProbeB");
            AssertStableFile(driver, second, "ProbeB");
        }

        [TestMethod]
        public void ExactFrameworkMemberCapabilityInvalidatesOnlyCollectionMapper()
        {
            var absent = CoreLibraryReference("public static T[] Empty<T>(int ignored) => new T[0];");
            var present = CoreLibraryReference("public static T[] Empty<T>() => new T[0];");
            var compilation = CSharpCompilation.Create("CapabilityTests", new[]
            {
                CSharpSyntaxTree.ParseText("namespace Mammoth.LiteMapper { public enum NullCollectionStrategy { Unspecified = 0, Error = 1, Preserve = 2, Empty = 3 } public sealed class LiteMapperAttribute : System.Attribute { public NullCollectionStrategy NullCollections { get; set; } } }", ParseOptions(), "Attributes.cs"),
                CSharpSyntaxTree.ParseText("using Mammoth.LiteMapper; [LiteMapper(NullCollections = NullCollectionStrategy.Empty)] public static partial class A { public static partial int[] Map(int[]? source); }", ParseOptions(), "A.cs"),
                CSharpSyntaxTree.ParseText(MapperB, ParseOptions(), "B.cs")
            }, new[] { absent }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var first = CreateDriver().RunGenerators(compilation);
            var firstSource = first.GetRunResult().Results.Single().GeneratedSources.Single(static source => source.HintName.StartsWith("A.", StringComparison.Ordinal)).SourceText.ToString();
            StringAssert.Contains(firstSource, "new int[0]");
            AssertCompiles(first, compilation);
            var changed = compilation.ReplaceReference(absent, present);
            var second = first.RunGenerators(changed);
            StringAssert.Contains(second.GetRunResult().Results.Single().GeneratedSources.Single(static source => source.HintName.StartsWith("A.", StringComparison.Ordinal)).SourceText.ToString(), "global::System.Array.Empty<int>()");
            AssertOutputReason(second, "A", IncrementalStepRunReason.Modified);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            AssertCompiles(second, changed);
        }

        [TestMethod]
        public void IdenticalAndEquivalentReparsedInputsCacheEveryMapperOutput()
        {
            var compilation = CreateCompilation();
            var first = CreateDriver().RunGenerators(compilation);
            var second = first.RunGenerators(compilation);
            AssertOutputReason(second, "A", IncrementalStepRunReason.Cached);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            var reparsed = second.RunGenerators(CreateCompilation());
            AssertOutputReason(reparsed, "A", IncrementalStepRunReason.Cached);
            AssertOutputReason(reparsed, "B", IncrementalStepRunReason.Cached);
        }

        [TestMethod]
        [DataRow("mapper")]
        [DataRow("model")]
        [DataRow("unrelated")]
        public void OnlySemanticallyAffectedMapperOutputIsRegenerated(string change)
        {
            var compilation = CreateCompilation();
            var first = CreateDriver().RunGenerators(compilation);
            var changed = change == "mapper"
                ? ReplaceTree(compilation, "A.cs", MapperA.Replace(" Map(", " Convert("))
                : change == "model"
                    ? ReplaceTree(compilation, "Models.cs", Models.Replace("class AS {", "class AS { public int Extra { get; set; }"))
                    : ReplaceTree(compilation, "Unrelated.cs", "public sealed class Unrelated { public string Text { get; set; } = string.Empty; }");
            var second = first.RunGenerators(changed);
            AssertOutputReason(second, "A", change == "unrelated" ? IncrementalStepRunReason.Cached : IncrementalStepRunReason.Modified);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            AssertCompiles(second, changed);
        }

        [TestMethod]
        public void PreprocessorCapabilityChangeInvalidatesOnlyAffectedOutput()
        {
            const string models = "public sealed class AS { public int Value { get; set; }\n#if EXTRA\npublic int Extra { get; set; }\n#endif\n} public sealed class AT { public int Value { get; set; } public int Extra { get; set; } }";
            var compilation = ReplaceTree(CreateCompilation(), "Models.cs", models);
            var first = CreateDriver().RunGenerators(compilation);
            var parseOptions = ParseOptions().WithPreprocessorSymbols("EXTRA");
            var changed = Reparse(compilation, parseOptions);
            var second = first.WithUpdatedParseOptions(parseOptions).RunGenerators(changed);
            AssertOutputReason(second, "A", IncrementalStepRunReason.Modified);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            AssertCompiles(second, changed);
        }

        [TestMethod]
        public void ReferencedModelApiChangeInvalidatesOnlyDependentOutput()
        {
            var originalReference = ModelReference("public sealed class AS { public int Value { get; set; } }");
            var updatedReference = ModelReference("public sealed class AS { public int Value { get; set; } public int Extra { get; set; } }");
            var compilation = ReplaceTree(CreateCompilation(), "Models.cs", "public sealed class AT { public int Value { get; set; } public int Extra { get; set; } }")
                .AddReferences(originalReference);
            var first = CreateDriver().RunGenerators(compilation);
            var changed = compilation.ReplaceReference(originalReference, updatedReference);
            var second = first.RunGenerators(changed);
            AssertOutputReason(second, "A", IncrementalStepRunReason.Modified);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            AssertCompiles(second, changed);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void LanguageChangeOnlyReemitsWhenGeneratedMetadataChanges(bool debugMetadata)
        {
            var options = new Options(debugMetadata);
            var compilation = Reparse(CreateCompilation(), ParseOptions(LanguageVersion.CSharp9));
            var first = CreateDriver(LanguageVersion.CSharp9, options).RunGenerators(compilation);
            var changed = Reparse(compilation, ParseOptions());
            var second = first.WithUpdatedParseOptions(ParseOptions()).RunGenerators(changed);
            var expected = debugMetadata ? IncrementalStepRunReason.Modified : IncrementalStepRunReason.Cached;
            AssertOutputReason(second, "A", expected);
            AssertOutputReason(second, "B", expected);
            AssertCompiles(second, changed);
        }

        [TestMethod]
        public void RelevantAnalyzerOptionsReemitAndUnrelatedOptionsRemainCached()
        {
            var compilation = CreateCompilation();
            var first = CreateDriver().RunGenerators(compilation);
            var second = first.WithUpdatedAnalyzerConfigOptions(new Options(false, unrelated: true)).RunGenerators(compilation);
            AssertOutputReason(second, "A", IncrementalStepRunReason.Cached);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
            var third = second.WithUpdatedAnalyzerConfigOptions(new Options(true)).RunGenerators(compilation);
            AssertOutputReason(third, "A", IncrementalStepRunReason.Modified);
            AssertOutputReason(third, "B", IncrementalStepRunReason.Modified);
        }

        [TestMethod]
        public void CachedSourceDoesNotReuseStaleDiagnosticLocations()
        {
            var compilation = ReplaceTree(CreateCompilation(), "A.cs", MapperA.Replace("(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)", string.Empty));
            var first = CreateDriver().RunGenerators(compilation);
            var original = first.GetRunResult().Diagnostics.Single(d => d.Id == "LITEMAPPER1001");
            var changed = ReplaceTree(compilation, "Models.cs", "\n\n" + Models);
            var second = first.RunGenerators(changed);
            var diagnostic = second.GetRunResult().Diagnostics.Single(d => d.Id == "LITEMAPPER1001");
            Assert.AreEqual(original.Location.GetLineSpan().StartLinePosition.Line + 2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
            CollectionAssert.Contains(changed.SyntaxTrees.ToArray(), diagnostic.Location.SourceTree);
            AssertOutputReason(second, "A", IncrementalStepRunReason.Cached);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
        }

        [TestMethod]
        public void ReversingSyntaxTreeOrderPreservesSortedOutputsAndCachedSources()
        {
            var compilation = CreateCompilation();
            var first = CreateDriver().RunGenerators(compilation);
            var changed = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(compilation.SyntaxTrees.Reverse());
            var second = first.RunGenerators(changed);
            var originalHints = first.GetRunResult().Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
            var updatedHints = second.GetRunResult().Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
            CollectionAssert.AreEqual(originalHints.OrderBy(static hint => hint, StringComparer.Ordinal).ToArray(), updatedHints);
            var fresh = CreateDriver().RunGenerators(changed);
            var originalFiles = first.GetRunResult().Results.Single().GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
                .Select(static source => source.HintName + "\n" + source.SourceText).ToArray();
            var freshFiles = fresh.GetRunResult().Results.Single().GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
                .Select(static source => source.HintName + "\n" + source.SourceText).ToArray();
            CollectionAssert.AreEqual(originalFiles, freshFiles, "A fresh driver must emit identical per-file content regardless of input-tree enumeration.");
            AssertOutputReason(second, "A", IncrementalStepRunReason.Cached);
            AssertOutputReason(second, "B", IncrementalStepRunReason.Cached);
        }

        private static void AssertOutputReason(GeneratorDriver driver, string mapper, IncrementalStepRunReason expected)
        {
            var steps = OutputStepsFor(driver, mapper);
            Assert.AreEqual(1, steps.Length, "Expected an independently tracked source output for mapper " + mapper + ", not an aggregate output shared by all mappers.");
            Assert.AreEqual(expected, steps[0].Outputs.Single().Reason, "Only changed emission payloads may rerun the source-output callback for " + mapper + ".");
        }

        private static void AssertRecreatedOutput(GeneratorDriver driver, string mapper)
        {
            // Section 19.2 says 'where possible': Roslyn does not compare New/Removed candidates.
            Assert.IsTrue(OutputStepsFor(driver, mapper).SelectMany(static step => step.Outputs).Any(static output => output.Reason == IncrementalStepRunReason.New || output.Reason == IncrementalStepRunReason.Modified),
                "The pinned Roslyn host recreates candidate identity for same-file mapper insertion/removal.");
        }

        private static void AssertStableFile(GeneratorDriver first, GeneratorDriver second, string mapper)
        {
            var original = first.GetRunResult().Results.Single().GeneratedSources.Single(source => source.HintName.StartsWith(mapper + ".", StringComparison.Ordinal));
            var updated = second.GetRunResult().Results.Single().GeneratedSources.Single(source => source.HintName == original.HintName);
            Assert.AreEqual(original.SourceText.ToString(), updated.SourceText.ToString(), "Recreated candidates must preserve identical hint names and generated content.");
        }

        private static IncrementalGeneratorRunStep[] OutputStepsFor(GeneratorDriver driver, string mapper)
        {
            var result = driver.GetRunResult().Results.Single();
            var steps = result.TrackedOutputSteps.Values.SelectMany(static values => values)
                .Where(step => step.Inputs.Any(input => input.Source.Name == "MapperEmission" &&
                    ((string?)input.Source.Outputs[input.OutputIndex].Value.GetType().GetProperty("HintName")?.GetValue(input.Source.Outputs[input.OutputIndex].Value))?.StartsWith(mapper + ".", StringComparison.Ordinal) == true))
                .ToArray();
            return steps;
        }

        private static void AssertCompiles(GeneratorDriver driver, Compilation compilation)
        {
            var errors = compilation.AddSyntaxTrees(driver.GetRunResult().GeneratedTrees).GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
        }

        private static CSharpParseOptions ParseOptions(LanguageVersion version = LanguageVersion.CSharp11) => CSharpParseOptions.Default.WithLanguageVersion(version);

        private static CSharpCompilation CreateCompilation() => CSharpCompilation.Create("Tests",
            new[] { ("A.cs", MapperA), ("Models.cs", Models), ("B.cs", MapperB), ("Unrelated.cs", "public sealed class Unrelated { }") }
                .Select(static file => CSharpSyntaxTree.ParseText(file.Item2, ParseOptions(), file.Item1)),
            References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        private static IEnumerable<MetadataReference> References() => AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
            .Split(Path.PathSeparator)
            .Where(static path => path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });

        private static CSharpCompilation ReplaceTree(CSharpCompilation compilation, string path, string source) => compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(tree => tree.FilePath == path), CSharpSyntaxTree.ParseText(source, ParseOptions(), path));

        private static CSharpCompilation Reparse(CSharpCompilation compilation, CSharpParseOptions options) => compilation.RemoveAllSyntaxTrees()
            .AddSyntaxTrees(compilation.SyntaxTrees.Select(tree => CSharpSyntaxTree.ParseText(tree.GetText(), options, tree.FilePath)));

        private static MetadataReference ModelReference(string source)
        {
            var compilation = CSharpCompilation.Create("ReferencedModels", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static MetadataReference CoreLibraryReference(string arrayMember)
        {
            var source = @"namespace System {
public class Object { }
public abstract class ValueType { }
public struct Void { }
public struct Boolean { }
public struct Int32 { }
public struct Byte { }
public sealed class String { }
public class Attribute { }
public abstract class Enum : ValueType { }
public enum AttributeTargets { All = 32767 }
public sealed class AttributeUsageAttribute : Attribute {
public AttributeUsageAttribute(AttributeTargets targets) { }
public bool AllowMultiple { get; set; }
public bool Inherited { get; set; }
}
public class Exception { }
public class ArgumentNullException : Exception { public ArgumentNullException(string name) { } }
public abstract class Array { public int Length => 0; " + arrayMember + @" }
}
namespace System.Collections {
public interface IEnumerable { IEnumerator GetEnumerator(); }
public interface IEnumerator { object Current { get; } bool MoveNext(); }
}";
            var compilation = CSharpCompilation.Create("SyntheticCoreLibrary", new[] { CSharpSyntaxTree.ParseText(source) }, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static GeneratorDriver CreateDriver(LanguageVersion version = LanguageVersion.CSharp11, AnalyzerConfigOptionsProvider? options = null) => CSharpGeneratorDriver.Create(
            new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: ParseOptions(version), optionsProvider: options ?? new Options(false),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        private sealed class Options : AnalyzerConfigOptionsProvider
        {
            private readonly Values _values;
            public Options(bool debugMetadata, bool unrelated = false) => _values = new Values(debugMetadata, unrelated);
            public override AnalyzerConfigOptions GlobalOptions => _values;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _values;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _values;
        }

        private sealed class AttributeIdentityProbe : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context)
            {
                var names = context.SyntaxProvider.ForAttributeWithMetadataName("Mammoth.LiteMapper.LiteMapperAttribute",
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    static (attribute, _) => new { HintName = attribute.TargetSymbol.Name + ".g.cs" })
                    .WithTrackingName("MapperEmission");
                context.RegisterSourceOutput(names, static (production, name) => production.AddSource(name.HintName, "// " + name.HintName));
            }
        }

        private sealed class Values : AnalyzerConfigOptions
        {
            private readonly bool _debugMetadata;
            private readonly bool _unrelated;
            public Values(bool debugMetadata, bool unrelated) { _debugMetadata = debugMetadata; _unrelated = unrelated; }
            public override bool TryGetValue(string key, out string value)
            {
                value = key == "build_property.LiteMapper_EmitDebugMetadata" ? _debugMetadata.ToString() : _unrelated.ToString();
                return key == "build_property.LiteMapper_EmitDebugMetadata" || key == "build_property.Unrelated";
            }
        }
    }
}
