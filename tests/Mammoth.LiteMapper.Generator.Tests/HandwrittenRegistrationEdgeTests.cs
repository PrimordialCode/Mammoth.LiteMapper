using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mammoth.LiteMapper.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class HandwrittenRegistrationEdgeTests
    {
        [TestMethod]
        public void RegisteredPointerMethodIsAcceptedAsHandwrittenBehavior()
        {
            var source = @"using Mammoth.LiteMapper;
public class Source { public int Value { get; set; } }
public class Target { public int Value { get; set; } }
public static unsafe class External
{
    public static int Read(int* value) => *value;
}
[LiteMapper, UseMapper(typeof(External))]
public static partial class Mapper { public static partial Target Map(Source source); }
public static class Probe { public static int Run() => Mapper.Map(new Source { Value = 3 }).Value; }";

            Assert.AreEqual(3, Execute(Run(source, allowUnsafe: true)));
        }

        [TestMethod]
        public void RegisteredRefLikeUpdateMethodIsAcceptedAsHandwrittenBehavior()
        {
            var source = @"using System;
using Mammoth.LiteMapper;
public class Source { public int Value { get; set; } }
public class Target { public int Value { get; set; } }
public static class External
{
    public static void Apply(Span<int> values, Target target) { target.Value = values.Length; }
}
[LiteMapper, UseMapper(typeof(External))]
public static partial class Mapper { public static partial Target Map(Source source); }
public static class Probe { public static int Run() => Mapper.Map(new Source { Value = 3 }).Value; }";

            Assert.AreEqual(3, Execute(Run(source)));
        }

        [TestMethod]
        public void RegisteredMethodWithInvalidRefParameterStillFailsRegistration()
        {
            var source = @"using Mammoth.LiteMapper;
public class Source { public int Value { get; set; } }
public class Target { public int Value { get; set; } }
public static class External
{
    public static int Read(ref int value) => value;
}
[LiteMapper, UseMapper(typeof(External))]
public static partial class Mapper { public static partial Target Map(Source source); }";

            var result = Run(source);
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER0010"),
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var method = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNull(method.PartialImplementationPart);
        }

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source, bool allowUnsafe = false)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("HandwrittenRegistrationEdgeTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: allowUnsafe));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Only missing partial implementations are allowed input errors. " +
                string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
