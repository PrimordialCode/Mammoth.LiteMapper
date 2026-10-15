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
    public sealed class TargetAccessorAccessibilityTests
    {
        [TestMethod]
        [DataRow("internal set")]
        [DataRow("internal init")]
        [DataRow("protected internal set")]
        [DataRow("private protected set")]
        public void AccessibleTargetAccessorsMapRootNestedAndCollectionValues(string accessor)
        {
            // A derived mapper can use private protected only through a derived target receiver.
            var derived = accessor == "private protected set";
            var result = Run(@"
using System.Collections.Generic;
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public partial class Mapper : ChildTarget
{
    public static partial MappedTarget Map(ChildSource source);
    public static partial Target MapRoot(Source source);
    public static partial List<CollectionTarget> MapMany(ChildSource[] source);
}
public sealed class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public sealed class Target { public NestedTarget Child { get; set; } = new NestedTarget(); }
public sealed class ChildSource { public int Value { get; set; } }
public class ChildTarget { public int Value { get; ACCESSOR; } }
public sealed class MappedTarget : BASE { }
public sealed class NestedTarget : BASE { }
public sealed class CollectionTarget : BASE { }
public static class Probe
{
    public static bool Run()
    {
        var source = new ChildSource { Value = 7 };
        return Mapper.Map(source).Value == 7 &&
            Mapper.MapRoot(new Source { Child = source }).Child.Value == 7 &&
            Mapper.MapMany(new[] { source })[0].Value == 7;
    }
}".Replace("ACCESSOR", accessor).Replace("BASE", derived ? "Mapper" : "ChildTarget"));

            AssertValidAndTrue(result);
            StringAssert.Contains(Generated(result), "Value = source.Value");
            var helpers = result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers().OfType<IMethodSymbol>()
                .Where(method => method.DeclaredAccessibility == Accessibility.Private).ToArray();
            Assert.IsTrue(helpers.Any(method => method.ReturnType.Name == "NestedTarget"), "The nested path must use an automatic structural helper.");
            Assert.IsTrue(helpers.Any(method => method.ReturnType.Name == "CollectionTarget"), "The element path must use an automatic structural helper.");
        }

        [TestMethod]
        [DataRow("internal set")]
        [DataRow("protected internal set")]
        public void AccessibleSettersSupportUpdatesAndNestedReplacement(string accessor)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial void Apply(ChildSource source, ChildTarget target);
    public static partial void ApplyRoot(Source source, Target target);
}
public sealed class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public sealed class Target { public ChildTarget Child { get; internal set; } = new ChildTarget(); }
public sealed class ChildSource { public int Value { get; set; } }
public sealed class ChildTarget { public int Value { get; ACCESSOR; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new ChildSource { Value = 7 };
        var child = new ChildTarget();
        Mapper.Apply(source, child);
        var root = new Target();
        var original = root.Child;
        Mapper.ApplyRoot(new Source { Child = source }, root);
        return child.Value == 7 && root.Child.Value == 7 && !object.ReferenceEquals(original, root.Child);
    }
}".Replace("ACCESSOR", accessor));
            AssertValidAndTrue(result);
        }

        [TestMethod]
        [DataRow("private set")]
        [DataRow("private init")]
        public void MapperNestedInsideTargetCanAssignItsPrivateAccessor(string accessor)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
public sealed partial class Target
{
    public int Value { get; ACCESSOR; }
    [LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
}
public sealed class Source { public int Value { get; set; } }
public static class Probe { public static bool Run() => Target.Mapper.Map(new Source { Value = 7 }).Value == 7; }
".Replace("ACCESSOR", accessor));
            AssertValidAndTrue(result);
        }

        [TestMethod]
        [DataRow("private set")]
        [DataRow("protected set")]
        [DataRow("private protected set")]
        public void UnrelatedMapperCannotAssignInaccessibleAccessor(string accessor)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public int Value { get; set; } }
public class Target { public int Value { get; ACCESSOR; } = 12; }
public static class Probe { public static bool Run() => Mapper.Map(new Source { Value = 7 }).Value == 12; }
".Replace("ACCESSOR", accessor));
            AssertValidAndTrue(result);
            Assert.IsFalse(Generated(result).Contains("Value = source.Value", StringComparison.Ordinal));
        }

        [TestMethod]
        public void ProtectedSetterCannotBeUsedThroughAnUnrelatedBaseReceiver()
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
public partial class Mapper : Target { public static partial Target Map(Source source); }
public sealed class Source { public int Value { get; set; } }
public class Target { public int Value { get; protected set; } = 12; }
public static class Probe { public static bool Run() => Mapper.Map(new Source { Value = 7 }).Value == 12; }
");
            AssertValidAndTrue(result);
        }

        [TestMethod]
        public void InternalSetterInAnotherAssemblyRemainsInaccessible()
        {
            var model = CSharpCompilation.Create("ExternalAccessorModel", new[] { CSharpSyntaxTree.ParseText(
                "public sealed class Target { public int Value { get; internal set; } = 12; }") }, References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = model.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Error)]
public static partial class Mapper { public static partial Target Map(Source source); }
public sealed class Source { public int Value { get; set; } }
", MetadataReference.CreateFromImage(stream.ToArray()));
            Assert.AreEqual("LITEMAPPER1001", result.RunResult.Diagnostics.Single().Id);
            AssertNoCompilerErrors(result);
            Assert.IsFalse(Generated(result).Contains("Value = source.Value", StringComparison.Ordinal));
        }

        [TestMethod]
        [DataRow("internal init")]
        [DataRow("init")]
        public void AccessibleInitAccessorStillCannotBeUpdated(string accessor)
        {
            var result = Run(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial void Apply(Source source, Target target); }
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; ACCESSOR; } }
".Replace("ACCESSOR", accessor));
            Assert.AreEqual("LITEMAPPER5004", result.RunResult.Diagnostics.Single().Id);
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
            Assert.IsTrue(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).All(d => d.Id == "CS8795"));
        }

        private static void AssertValidAndTrue((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            AssertNoCompilerErrors(result);
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            Assert.IsTrue((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!);
            Assert.IsFalse(Generated(result).Contains("System.Reflection", StringComparison.Ordinal));
        }

        private static void AssertNoCompilerErrors((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            var errors = result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        private static string Generated((GeneratorDriverRunResult RunResult, Compilation Compilation) result) =>
            string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));

        private static IEnumerable<MetadataReference> References() => AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path))
            .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source, MetadataReference? external = null)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var references = external == null ? References() : References().Concat(new[] { external });
            var compilation = CSharpCompilation.Create("TargetAccessorAccessibilityTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Fixture must be valid before generation: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new Mammoth.LiteMapper.Generator.LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
