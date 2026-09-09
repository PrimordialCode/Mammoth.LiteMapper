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
    public sealed class FinalDeclaredMappingNullabilityRegressionTests
    {
        [TestMethod]
        public void NullableDirectChildPatchSkipsNullAndUsesDeclaredMappingForPresentValue()
        {
            AssertProbe(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    [MappingOptions(IgnoreNullSourceMembers = OptionState.Enabled)]
    public static partial void Apply(Source source, Target target);

    public static partial ChildTarget MapChild(ChildSource source);
}

public sealed class Source { public ChildSource? Child { get; set; } }
public sealed class Target { public ChildTarget Child { get; set; } = new ChildTarget { Value = 7 }; }
public sealed class ChildSource { public int Value { get; set; } }
public sealed class ChildTarget { public int Value { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        var target = new Target();
        var originalChild = target.Child;
        Mapper.Apply(new Source { Child = null }, target);
        if (!ReferenceEquals(originalChild, target.Child) || target.Child.Value != 7) return false;

        Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target);
        return !ReferenceEquals(originalChild, target.Child) && target.Child.Value == 3;
    }
}
", "Sections 14.4 and 16.3 require a null child patch to preserve the existing child and a present child to use the declared new-object mapping without a nullable-input diagnostic.");
        }

        [TestMethod]
        public void NullableDestinationCreationPassesUnwrappedValueToRequiredNullableMemberConverter()
        {
            AssertProbe(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    public static int ConverterCalls;

    [MappingOptions(IgnoreNullSourceMembers = OptionState.Enabled)]
    [MapProperty(Source = ""Payload.Number"", Target = nameof(Target.Number), Use = nameof(Convert))]
    public static partial Target Apply(Source source, Target? destination);

    private static long? Convert(int value)
    {
        ConverterCalls++;
        return value;
    }
}

public sealed class Source { public Payload Payload { get; } = new Payload(); }
public sealed class Payload { public int? Number { get; set; } = 3; }
public sealed class Target { public required long? Number { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        Mapper.ConverterCalls = 0;
        var created = Mapper.Apply(new Source(), null);
        return created.Number == 3L && Mapper.ConverterCalls == 1;
    }
}
", "Sections 11.4 and 16.2 require destination creation to satisfy the selected converter's int parameter even though the required target member accepts long?.");
        }

        private static void AssertProbe(string source, string intent)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "FinalDeclaredMappingNullability_" + Guid.NewGuid().ToString("N"),
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
            Assert.AreEqual(0, diagnostics.Length, intent + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));

            using var stream = new MemoryStream();
            var emitted = updatedCompilation.Emit(stream);
            Assert.IsTrue(emitted.Success, intent + Environment.NewLine + string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null), intent);
        }
    }
}
