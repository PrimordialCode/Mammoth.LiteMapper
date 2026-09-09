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
    public sealed class NullPolicyContractEdgeTests
    {
        [TestMethod]
        public void ErrorPolicyRejectsNullableReferenceRootToNonNullableTarget()
        {
            var result = Run(@"
#nullable enable
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Error)]
public static partial class Mapper
{
    public static partial string Map(string? source);
}
");

            AssertDiagnostic(result, "LITEMAPPER2001");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "A nullable reference root mismatch must not emit an implementation.");
        }

        [TestMethod]
        public void ErrorPolicyRejectsNullableReferenceCollectionElementToNonNullableTargetElement()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Error)]
public static partial class Mapper
{
    public static partial List<string> Map(List<string?> source);
}
");

            AssertDiagnostic(result, "LITEMAPPER2003");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "A nullable collection element mismatch must not emit an implementation.");
        }

        [TestMethod]
        public void DefaultNullCollectionStrategyRejectsNullableCollectionRootToNonNullableTarget()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial List<int> Map(List<int>? source);
}
");

            AssertDiagnostic(result, "LITEMAPPER2002");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "The contextual default must reject a nullable collection source for a nonnullable target.");
        }

        [TestMethod]
        public void ExplicitErrorNullCollectionStrategyRejectsNullableCollectionRootToNonNullableTarget()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullCollections = NullCollectionStrategy.Error)]
public static partial class Mapper
{
    public static partial List<int> Map(List<int>? source);
}
");

            AssertDiagnostic(result, "LITEMAPPER2002");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "NullCollections.Error must reject a nullable collection source for a nonnullable target during generation.");
        }

        [TestMethod]
        public void ExplicitErrorNullCollectionStrategyRejectsNullableCollectionRootToNullableTarget()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullCollections = NullCollectionStrategy.Error)]
public static partial class Mapper
{
    public static partial List<int>? Map(List<int>? source);
}
");

            AssertDiagnostic(result, "LITEMAPPER2002");
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length,
                "An explicit NullCollections.Error must reject a nullable collection source even when the target is nullable.");
        }

        [TestMethod]
        public void DefaultNullCollectionStrategyPreservesNullableCollectionRootToNullableTarget()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial List<int>? Map(List<int>? source);
}
public static class Probe
{
    public static bool Run() => Mapper.Map(null) == null;
}
");

            AssertProbe(result);
        }

        [TestMethod]
        public void ErrorPolicyAllowsNullableReferenceRootWhenTargetIsNullable()
        {
            var result = Run(@"
#nullable enable
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Error)]
public static partial class Mapper
{
    public static partial string? Map(string? source);
}
public static class Probe
{
    public static bool Run() => Mapper.Map(null) == null && Mapper.Map(""value"") == ""value"";
}
");

            AssertProbe(result);
        }

        [TestMethod]
        public void ErrorPolicyAllowsNullableReferenceCollectionElementWhenTargetElementIsNullable()
        {
            var result = Run(@"
#nullable enable
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Error)]
public static partial class Mapper
{
    public static partial List<string?> Map(List<string?> source);
}
public static class Probe
{
    public static bool Run()
    {
        var mapped = Mapper.Map(new List<string?> { null, ""value"" });
        return mapped.Count == 2 && mapped[0] == null && mapped[1] == ""value"";
    }
}
");

            AssertProbe(result);
        }

        [TestMethod]
        public void ThrowPolicyGeneratesNullableReferenceRootGuard()
        {
            var result = Run(@"
#nullable enable
using System;
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    public static partial string Map(string? source);
}
public static class Probe
{
    public static bool Run()
    {
        try
        {
            Mapper.Map(null);
            return false;
        }
        catch (ArgumentNullException ex)
        {
            return ex.ParamName == ""source"";
        }
    }
}
");

            AssertProbe(result);
        }

        [TestMethod]
        public void ThrowPolicyGeneratesNullableReferenceCollectionElementGuard()
        {
            var result = Run(@"
#nullable enable
using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper
{
    public static partial List<string> Map(List<string?> source);
}
public static class Probe
{
    public static bool Run()
    {
        try
        {
            Mapper.Map(new List<string?> { null });
            return false;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains(""item"");
        }
    }
}
");

            AssertProbe(result);
        }

        private static void AssertDiagnostic((GeneratorDriverRunResult RunResult, Compilation Compilation) result, string id)
        {
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == id),
                "Expected " + id + ": " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"),
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
        }

        private static void AssertProbe((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length,
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create(
                "NullPolicyContractEdgeTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics()
                .Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795")
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
