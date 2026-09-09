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
    public sealed class GeneratedIdentifierCollisionTests
    {
        [TestMethod]
        public void SourceParameterNamedTargetDoesNotCollideWithConstructedDestination()
        {
            AssertProbe(@"
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial Target Map(Source target);
}

public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        var source = new Source { Value = 3 };
        var mapped = Mapper.Map(source);
        return mapped.Value == 3 && source.Value == 3;
    }
}
", "Specification 8.1 permits this parameter name; generated destination locals must not shadow it.");
        }

        [TestMethod]
        [DataRow("item")]
        [DataRow("target")]
        [DataRow("index")]
        public void CollectionSourceParameterDoesNotCollideWithLoopLocals(string parameterName)
        {
            AssertProbe(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class Mapper
{
    public static partial int[] Map(List<int> SOURCE_PARAMETER);
}

public static class Probe
{
    public static bool Run()
    {
        var source = new List<int> { 3, 7 };
        var mapped = Mapper.Map(source);
        var empty = Mapper.Map(new List<int>());
        return mapped.Length == 2 && mapped[0] == 3 && mapped[1] == 7
            && empty.Length == 0 && source.Count == 2 && source[0] == 3 && source[1] == 7;
    }
}
".Replace("SOURCE_PARAMETER", parameterName),
                "Specifications 8.1 and 15.1 require collection mappings to work regardless of names chosen for generated loop locals.");
        }

        [TestMethod]
        [DataRow("__tracker")]
        [DataRow("__memberPath")]
        public void RecursiveSourceParameterDoesNotCollideWithTrackingInfrastructure(string parameterName)
        {
            AssertProbe(@"
#nullable enable
using Mammoth.LiteMapper;

[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class Mapper
{
    public static partial ADto MapA(A SOURCE_PARAMETER);
    public static partial BDto MapB(B other);
}

public sealed class A { public B? B { get; set; } }
public sealed class B { public A? A { get; set; } }
public sealed class ADto { public BDto? B { get; set; } }
public sealed class BDto { public ADto? A { get; set; } }

public static class Probe
{
    public static bool Run()
    {
        var finite = Mapper.MapA(new A { B = new B() });
        if (finite.B == null || finite.B.A != null) return false;

        var cyclic = new A();
        cyclic.B = new B { A = cyclic };
        try
        {
            Mapper.MapA(cyclic);
            return false;
        }
        catch (LiteMapperCycleException exception)
        {
            return exception.MappingMethod == ""MapA"" && exception.MemberPath == ""B.A""
                && exception.SourceType == typeof(A) && exception.DestinationType == typeof(ADto);
        }
    }
}
".Replace("SOURCE_PARAMETER", parameterName),
                "Specifications 8.1 and 17.2-17.5 require tracking to preserve valid parameter names and still detect the actual cycle.");
        }

        private static void AssertProbe(string source, string intent)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(Mammoth.LiteMapper.LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "GeneratedIdentifierCollision_" + Guid.NewGuid().ToString("N"),
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
            var emitted = updatedCompilation.Emit(stream);
            Assert.IsTrue(emitted.Success, intent + Environment.NewLine + string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(stream.ToArray());
            try
            {
                Assert.AreEqual(true, assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null), intent);
            }
            catch (TargetInvocationException exception)
            {
                var generated = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString()));
                Assert.Fail(intent + Environment.NewLine + exception.InnerException + Environment.NewLine + generated);
            }
        }
    }
}
