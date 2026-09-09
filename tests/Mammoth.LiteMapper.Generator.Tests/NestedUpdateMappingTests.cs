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
    public sealed class NestedUpdateMappingTests
    {
        [TestMethod]
        [DataRow(true, "local", 3)]
        [DataRow(true, "external", 23)]
        [DataRow(false, "none", 3)]
        public void NestedUpdatePreservesIdentityOnlyWhenMutationIsSelected(bool getOnly, string updater, int expected)
        {
            var result = Run(Fixture(getOnly, updater) + @"
public static class Probe {
    public static int Run() {
        var source = new Source { Child = new ChildSource { Value = 3 } };
        var target = new Target();
        var original = target.Child;
        Mapper.Apply(source, target);
        if (object.ReferenceEquals(original, target.Child) != " + (getOnly ? "true" : "false") + @")
            throw new System.InvalidOperationException(""Nested update identity contract was violated."");
        return target.Child.Value;
    }
}");
            Assert.AreEqual(expected, Execute(result));
        }

        [TestMethod]
        public void GetOnlyChildWithoutAnEligibleUpdaterStillFails()
        {
            var result = Run(Fixture(true, "none"));
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER5005"),
                string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsNull(result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Apply")
                .OfType<IMethodSymbol>().Single().PartialImplementationPart);
        }

        [TestMethod]
        public void ExplicitNestedUpdaterOverridesTheAutomaticLocalMethod()
        {
            var fixture = Fixture(true, "local")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "[MapProperty(Source = nameof(Source.Child), Target = nameof(Target.Child), Use = nameof(Chosen))] public static partial void Apply(Source source, Target target);")
                .Replace("public static partial void ApplyChild(ChildSource source, ChildTarget target);",
                    "public static partial void ApplyChild(ChildSource source, ChildTarget target); private static void Chosen(ChildSource source, ChildTarget target) { target.Value = source.Value + 10; }");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target); return target.Child.Value; } }");
            Assert.AreEqual(13, Execute(result), "Explicit Use selects the handwritten updater before automatic local mappings.");
        }

        [TestMethod]
        public void NestedUpdaterReceivesTheConfiguredSourcePath()
        {
            var fixture = Fixture(true, "local")
                .Replace("public class Source {", "public class Holder { public ChildSource Item { get; set; } = new ChildSource { Value = 7 }; } public class Source { public Holder Other { get; set; } = new Holder();")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "[MapProperty(Source = \"Other.Item\", Target = nameof(Target.Child))] public static partial void Apply(Source source, Target target);");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target); return target.Child.Value; } }");
            Assert.AreEqual(7, Execute(result), "The selected path supplies the nested updater rather than the same-name source child.");
        }

        [TestMethod]
        public void EquallyRegisteredNestedUpdatersAreAmbiguous()
        {
            var fixture = Fixture(true, "external")
                .Replace("[UseMapper(typeof(External))]", "[UseMapper(typeof(External)), UseMapper(typeof(Other))]")
                + "public static class Other { public static void ApplyChild(ChildSource source, ChildTarget target) { target.Value = source.Value + 10; } }";
            var result = Run(fixture);
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER3001"),
                "Container order must not resolve equally eligible nested mappings: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
        }

        [TestMethod]
        public void ClassRegisteredNestedUpdaterPrecedesAssemblyRegistration()
        {
            var fixture = Fixture(true, "external")
                .Replace("using Mammoth.LiteMapper;", "using Mammoth.LiteMapper; [assembly: UseMapper(typeof(AExternal))]")
                + "public static class AExternal { public static void ApplyChild(ChildSource source, ChildTarget target) { target.Value = source.Value + 10; } }";
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target); return target.Child.Value; } }");
            Assert.AreEqual(23, Execute(result), "Class registration wins even when the assembly container sorts first.");
        }

        [TestMethod]
        public void ExplicitNestedUpdaterPrefersIdentitySourceOverload()
        {
            var fixture = Fixture(true, "none")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "[MapProperty(Source = nameof(Source.Child), Target = nameof(Target.Child), Use = nameof(Chosen))] public static partial void Apply(Source source, Target target); " +
                    "private static void Chosen(ChildSource source, ChildTarget target) { target.Value = source.Value + 10; } " +
                    "private static void Chosen(object source, ChildTarget target) { target.Value = 99; }");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target); return target.Child.Value; } }");
            Assert.AreEqual(13, Execute(result), "Identity source compatibility must beat an implicit conversion to object for explicit Use.");
        }

        [TestMethod]
        public void ExplicitWritableChildUpdaterPreservesTheExistingChild()
        {
            var fixture = Fixture(false, "local")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "[MapProperty(Source = nameof(Source.Child), Target = nameof(Target.Child), Use = nameof(ApplyChild))] public static partial void Apply(Source source, Target target);");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); var original = target.Child; Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target); if (!object.ReferenceEquals(original, target.Child)) throw new System.InvalidOperationException(); return target.Child.Value; } }");
            Assert.AreEqual(3, Execute(result), "Explicit updater selection requests mutation even when the child has a setter.");
        }

        [TestMethod]
        public void ExplicitRootUpdaterDoesNotRequireASameNameSourceMember()
        {
            var fixture = Fixture(true, "none")
                .Replace("public class Source { public ChildSource Child { get; set; } = new ChildSource(); }", "public class Source { public int Value { get; set; } }")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "[MapProperty(Target = nameof(Target.Child), Use = nameof(ApplyChild))] public static partial void Apply(Source source, Target target); private static void ApplyChild(Source source, ChildTarget target) { target.Value = source.Value; }");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); Mapper.Apply(new Source { Value = 3 }, target); return target.Child.Value; } }");
            Assert.AreEqual(3, Execute(result), "Omitted Source explicitly selects the root object, independently of same-name matching.");
        }

        [TestMethod]
        public void PatchModeSkipsANullNestedUpdaterSource()
        {
            var fixture = Fixture(true, "local")
                .Replace("public ChildSource Child { get; set; } = new ChildSource();", "public ChildSource? Child { get; set; }")
                .Replace("[LiteMapper]", "[LiteMapper(IgnoreNullSourceMembers = true)]");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); target.Child.Value = 5; Mapper.Apply(new Source { Child = null }, target); return target.Child.Value; } }");
            Assert.AreEqual(5, Execute(result), "Patch semantics must skip the updater and retain the destination child.");
        }

        [TestMethod]
        public void NullableNestedUpdaterSourceRequiresTheConfiguredMismatchPolicy()
        {
            var fixture = Fixture(true, "local")
                .Replace("public ChildSource Child { get; set; } = new ChildSource();", "public ChildSource? Child { get; set; }");
            var result = Run(fixture);
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2001"),
                "A nullable updater source cannot flow into a non-null parameter under Error: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsNull(result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Apply")
                .OfType<IMethodSymbol>().Single().PartialImplementationPart);
        }

        [TestMethod]
        public void ThrowPolicyReportsTheNullNestedUpdaterSourcePath()
        {
            var fixture = Fixture(true, "local")
                .Replace("public ChildSource Child { get; set; } = new ChildSource();", "public ChildSource? Child { get; set; }")
                .Replace("[LiteMapper]", "[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]");
            var result = Run(fixture + "public static class Probe { public static string Run() { try { Mapper.Apply(new Source { Child = null }, new Target()); return \"none\"; } catch (System.InvalidOperationException ex) { return ex.Message; } } }");
            StringAssert.Contains((string)Execute(result)!, "Child", "The runtime mismatch must identify the null member path.");
        }

        [TestMethod]
        public void ReturningUpdaterCreatesAndAssignsANullableWritableChild()
        {
            var fixture = Fixture(false, "none")
                .Replace("public ChildTarget Child { get; set; } = new ChildTarget();", "public ChildTarget? Child { get; set; }")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "[MapProperty(Source = nameof(Source.Child), Target = nameof(Target.Child), Use = nameof(ApplyChild))] public static partial void Apply(Source source, Target target); " +
                    "private static ChildTarget ApplyChild(ChildSource source, ChildTarget? target) { target ??= new ChildTarget(); target.Value = source.Value; return target; }");
            var result = Run(fixture + "public static class Probe { public static int Run() { var target = new Target(); Mapper.Apply(new Source { Child = new ChildSource { Value = 3 } }, target); return target.Child == null ? -1 : target.Child.Value; } }");
            Assert.AreEqual(3, Execute(result), "The updater return must replace an initially null writable child.");
        }

        [TestMethod]
        public void NullableGetOnlyChildCannotDiscardAReplacementUpdaterResult()
        {
            var fixture = Fixture(true, "none")
                .Replace("public ChildTarget Child { get;  } = new ChildTarget();", "public ChildTarget? Child { get; }")
                .Replace("public static partial void Apply(Source source, Target target);",
                    "public static partial void Apply(Source source, Target target); [DefaultMapping] private static ChildTarget ApplyChild(ChildSource source, ChildTarget? target) { return target ?? new ChildTarget { Value = source.Value }; }");
            var result = Run(fixture);
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER5005"),
                "A get-only nullable child cannot store a replacement returned by an updater: " + string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsNull(result.Compilation.GetTypeByMetadataName("Mapper")!.GetMembers("Apply")
                .OfType<IMethodSymbol>().Single().PartialImplementationPart);
        }

        private static string Fixture(bool getOnly, string updater) => @"
using Mammoth.LiteMapper;
public class ChildSource { public int Value { get; set; } }
public class ChildTarget { public int Value { get; set; } }
public class Source { public ChildSource Child { get; set; } = new ChildSource(); }
public class Target { public ChildTarget Child { get; " + (getOnly ? string.Empty : "set;") + @" } = new ChildTarget(); }
" + (updater == "external" ? @"
public static class External { public static void ApplyChild(ChildSource source, ChildTarget target) { target.Value = source.Value + 20; } }
[UseMapper(typeof(External))]" : string.Empty) + @"
[LiteMapper] public static partial class Mapper {
    public static partial void Apply(Source source, Target target);
    " + (updater == "local" ? "public static partial void ApplyChild(ChildSource source, ChildTarget target);" : string.Empty) + @"
}
";

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("NestedUpdateMappingTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
