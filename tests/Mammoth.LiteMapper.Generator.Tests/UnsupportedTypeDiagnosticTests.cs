using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class UnsupportedTypeDiagnosticTests
    {
        [TestMethod]
        [DataRow("Item", "Value")]
        [DataRow("Child.Item", "Value")]
        [DataRow("Value", "Item")]
        public void ExplicitIndexerSelectionUsesIndexerDiagnostic(string sourcePath, string target)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = """ + sourcePath + @""", Target = """ + target + @""")]
    public static partial Target Map(Source source);
}
public sealed class Source
{
    public int Value { get; set; }
    public int this[int index] => index;
    public Child Child { get; set; } = new Child();
}
public sealed class Child { public int this[int index] => index; }
public sealed class Target
{
    public int Value { get; set; }
    public int this[int index] { get => index; set { } }
}
");

            AssertFatal(result, "LITEMAPPER1016");
        }

        [TestMethod]
        [DataRow("dynamic", "Target", "")]
        [DataRow("Source", "dynamic", "")]
        [DataRow("System.Collections.Generic.List<dynamic>", "System.Collections.Generic.List<object>", "")]
        [DataRow("int*", "int", "unsafe")]
        [DataRow("Source", "int*", "unsafe")]
        [DataRow("delegate*<int, int>", "int", "unsafe")]
        public void UnsupportedGeneratedSignatureUsesTypeDiagnostic(string sourceType, string targetType, string modifier)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static " + modifier + @" partial class Mapper
{
    public static partial " + targetType + @" Map(" + sourceType + @" source);
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertFatal(result, "LITEMAPPER2008");
        }

        [TestMethod]
        [DataRow("RefSource", "Target")]
        [DataRow("Source", "RefTarget")]
        [DataRow("System.Span<int>", "int")]
        public void RefLikeStructuralMappingUsesTypeDiagnostic(string sourceType, string targetType)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial " + targetType + @" Map(" + sourceType + @" source);
}
public ref struct RefSource { public int Value { get; set; } }
public ref struct RefTarget { public int Value { get; set; } }
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertFatal(result, "LITEMAPPER2008");
        }

        [TestMethod]
        [DataRow("private static async System.Threading.Tasks.Task<int> Convert(int value) { await System.Threading.Tasks.Task.Yield(); return value; }")]
        [DataRow("private static System.Threading.Tasks.Task<int> Convert(int value) => System.Threading.Tasks.Task.FromResult(value);")]
        public void ExplicitAsynchronousConverterUsesAsyncDiagnostic(string converter)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source source);
    " + converter + @"
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertFatal(result, "LITEMAPPER0006");
        }

        [TestMethod]
        public void HandwrittenRefLikeConverterRemainsUsable()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper
{
    public static partial int Map(System.Span<int> source);
    [MappingConverter] private static int Count(System.Span<int> source) => source.Length;
}
");

            AssertCompiles(result);
        }

        [TestMethod]
        public void UsableSynchronousOverloadAndUnrelatedHelpersAreNotRejected()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static unsafe partial class Mapper
{
    [MapProperty(Source = nameof(Source.Value), Target = nameof(Target.Value), Use = nameof(Convert))]
    public static partial Target Map(Source source);
    private static int Convert(int value) => value;
    private static System.Threading.Tasks.Task<int> Convert(string value) => System.Threading.Tasks.Task.FromResult(value.Length);
    private static int Read(int* value) => *value;
    private static int Count(System.Span<int> value) => value.Length;
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");

            AssertCompiles(result);
        }

        private static void AssertFatal(GeneratorRun result, string id)
        {
            var matches = result.Result.Diagnostics.Where(d => d.Id == id).ToArray();
            Assert.AreEqual(1, matches.Length, "Specification 18/20.2 requires " + id + ": " + string.Join(Environment.NewLine, result.Result.Diagnostics));
            Assert.AreEqual(DiagnosticSeverity.Error, matches[0].Severity);
            Assert.IsTrue(matches[0].Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable));
            var method = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNull(method.PartialImplementationPart, "Unsupported generated behavior must not receive an implementation.");
        }

        [TestMethod]
        [DataRow("dynamic", "object")]
        [DataRow("int", "dynamic")]
        [DataRow("int*", "int*")]
        [DataRow("delegate*<int, int>", "delegate*<int, int>")]
        [DataRow("RefValue", "ValueTarget")]
        public void UnsupportedMemberTypesCannotBypassSignatureValidation(string sourceType, string targetType)
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static unsafe partial class Mapper { public static partial Target Map(Source source); }
public unsafe class Source { public " + sourceType + @" Value => default!; }
public unsafe class Target { public " + targetType + @" Value { get; set; } = default!; }
public ref struct RefValue { public int Id { get; set; } }
public class ValueTarget { public int Id { get; set; } }");
            AssertFatal(result, "LITEMAPPER2008");
        }

        [TestMethod]
        [DataRow("RefSource", "Target", "")]
        [DataRow("Source", "RefTarget", "ref ")]
        public void RefLikeUpdatesRequireHandwrittenImplementation(string source, string target, string modifier)
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial void Map(" + source + " source, " + modifier + target + @" target); }
public class Source { public int Value { get; set; } }
public class Target { public int Value { get; set; } }
public ref struct RefSource { public int Value { get; set; } }
public ref struct RefTarget { public int Value { get; set; } }");
            AssertFatal(result, "LITEMAPPER2008");
        }

        [TestMethod]
        public void HandwrittenRefLikeMemberConverterRemainsUsable()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper {
public static partial Target Map(Source source);
[MappingConverter] private static int Count(System.Span<int> value) => value.Length;
}
public class Source { public System.Span<int> Value => default; }
public class Target { public int Value { get; set; } }");
            AssertCompiles(result);
        }

        private static void AssertCompiles(GeneratorRun result)
        {
            Assert.AreEqual(0, result.Result.Diagnostics.Length, string.Join(Environment.NewLine, result.Result.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }

        private static GeneratorRun Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("Tests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
            var inputErrors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Fixture input must be valid C#: " + string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            return new GeneratorRun(driver.GetRunResult(), output);
        }

        private sealed class GeneratorRun
        {
            public GeneratorRun(GeneratorDriverRunResult result, Compilation compilation)
            {
                Result = result;
                Compilation = compilation;
            }

            public GeneratorDriverRunResult Result { get; }
            public Compilation Compilation { get; }
        }
    }
}
