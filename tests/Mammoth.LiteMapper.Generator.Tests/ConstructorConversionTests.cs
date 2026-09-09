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
    public class ConstructorConversionTests
    {
        [TestMethod]
        [DataRow("[MapProperty(Source = nameof(Source.Raw), Target = nameof(Target.Value), Use = nameof(Parse))]", "private static int Parse(string value) => int.Parse(value);", "string Raw", "\"12\"", 12)]
        [DataRow("[MapProperty(Source = nameof(Source.Raw), Target = nameof(Target.Value))]", "", "int Raw", "12", 12)]
        [DataRow("", "[MappingConverter] private static int Adjust(int value) => value + 1;", "int Value", "12", 13)]
        [DataRow("[MapProperty(Target = nameof(Target.Value), Use = nameof(Build))]", "private static int Build(Source source) => source.Raw + 1;", "int Raw", "12", 13)]
        public void ConstructorArgumentsUseExplicitConfigurationAndConverterPrecedence(string configuration, string converter, string member, string value, int expected)
        {
            var sourceName = member.Split(' ')[1];
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper {
" + configuration + @" public static partial Target Map(Source source);
" + converter + @" }
public class Source { public " + member + @" { get; set; } }
public class Target { public Target(int value) { Value = value; } public int Value { get; } }
public static class Probe { public static int Run() => Mapper.Map(new Source { " + sourceName + " = " + value + " }).Value; }");
            Assert.AreEqual(expected, Execute(result));
        }

        [TestMethod]
        public void ConstructorArgumentsUseNumericEnumAndNestedConversions()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked)] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int Count { get; set; } public SourceColor Color { get; set; } public Child Child { get; set; } = new Child(); }
public class Target { public Target(byte count, TargetColor color, ChildDto child) { Count=count; Color=color; Child=child; } public byte Count { get; } public TargetColor Color { get; } public ChildDto Child { get; } }
public class Child { public int Id { get; set; } }
public class ChildDto { public ChildDto(int id) { Id=id; } public int Id { get; } }
public enum SourceColor { Red=1 } public enum TargetColor { Red=7 }
public static class Probe { public static int Run() { var target=Mapper.Map(new Source { Count=12, Color=SourceColor.Red, Child=new Child { Id=5 } }); return target.Count+(int)target.Color+target.Child.Id; } }");
            Assert.AreEqual(24, Execute(result));
        }

        [TestMethod]
        public void NullableConstructorArgumentRejectsErrorAndChecksThrowOnce()
        {
            const string model = @"
public class Source { public int Reads; public string? Value { get { Reads++; return null; } } }
public class Target { public Target(string value) { Value=value; } public string Value { get; } }
";
            var invalid = Run("using Mammoth.LiteMapper; [LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }" + model);
            Assert.IsTrue(invalid.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2001"));
            var valid = Run(@"using System; using Mammoth.LiteMapper; [LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class Mapper { public static partial Target Map(Source source); }" + model + @"
public static class Probe { public static int Run() { var source=new Source(); try { Mapper.Map(source); return 0; } catch (InvalidOperationException ex) { return ex.Message.Contains(""Value"") ? source.Reads : 0; } } }");
            Assert.AreEqual(1, Execute(valid));
        }

        [TestMethod]
        public void IgnoreCaseConstructorMatchingDoesNotPreferAnExactMatch()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper(NameMatching = NameMatching.IgnoreCase)] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int value { get; set; } public int VALUE { get; set; } }
public class Target { public Target(int value) { Value=value; } public int Value { get; } }");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER1004"));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        [TestMethod]
        public void OptionalDefaultStillAppliesWithoutAMatchingSource()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { }
public class Target { public Target(int value = 19) { Value=value; } public int Value { get; } }
public static class Probe { public static int Run() => Mapper.Map(new Source()).Value; }");
            Assert.AreEqual(19, Execute(result));
        }

        [TestMethod]
        [DataRow("internal", false)]
        [DataRow("public", true)]
        public void ReferencedAssemblyConstructorAccessibilityUsesTheCompilation(string accessibility, bool accepted)
        {
            var dependency = CreateCompilation("public class Target { " + accessibility + " Target(int value) { Value=value; } public int Value { get; } }", "Models");
            using var stream = new MemoryStream();
            Assert.IsTrue(dependency.Emit(stream).Success);
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int Value { get; set; } }
", MetadataReference.CreateFromImage(stream.ToArray()));
            if (accepted)
            {
                Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
                Assert.IsFalse(result.Compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error));
            }
            else
            {
                Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER1010"));
            }
        }

        [TestMethod]
        public void DiscardedConstructorDoesNotContributeHelpersOrDiagnostics()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public Child Child { get; set; } = new Child(); public int Value { get; set; } }
public class Child { public int Id { get; set; } }
public class ChildDto { public int Id { get; set; } }
public class Target { public Target(ChildDto child, string missing) { Value=child.Id; } public Target(int value) { Value=value; } public int Value { get; } }
public static class Probe { public static int Run() => Mapper.Map(new Source { Value=7 }).Value; }");
            Assert.AreEqual(7, Execute(result));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any(s => s.SourceText.ToString().Contains("MapNested_Child")));
        }

        [TestMethod]
        public void ConstructorNullabilityUsesParameterRatherThanPropertyAnnotation()
        {
            var accepted = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public string? Value { get; set; } }
public class Target { public Target(string? value) { Value=value ?? ""fallback""; } public string Value { get; } }
public static class Probe { public static string Run() => Mapper.Map(new Source()).Value; }");
            Assert.AreEqual("fallback", Execute(accepted));
            var rejected = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public string? Value { get; set; } }
public class Target { public Target(string value) { Value=value; } public string? Value { get; } }");
            Assert.IsTrue(rejected.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2001"));
        }

        [TestMethod]
        public void RecursiveConstructorArgumentsRetainCycleTracking()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public Source? Next { get; set; } }
public class Target { public Target(Target? next) { Next=next; } public Target? Next { get; } }
public static class Probe { public static int Run() { var source=new Source(); source.Next=source; try { Mapper.Map(source); return 0; } catch (LiteMapperCycleException) { return 1; } } }");
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var generated = result.RunResult.Results.SelectMany(r => r.GeneratedSources).Single().SourceText.ToString();
            StringAssert.Contains(generated, "__tracker.Enter");
            Assert.AreEqual(1, Execute(result));
        }

        [TestMethod]
        public void NullableNumericConstructorArgumentChecksBeforeCastingWithoutDoubleUnwrapping()
        {
            var result = Run(@"using System; using Mammoth.LiteMapper;
[LiteMapper(NumericConversion = NumericConversion.Checked, NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class Mapper { public static partial Target Map(Source source); }
public class Source { public int? Value { get; set; } }
public class Target { public Target(byte value) { Value=value; } public byte Value { get; } }
public static class Probe { public static int Run() { try { Mapper.Map(new Source()); return 0; } catch (InvalidOperationException) { return Mapper.Map(new Source { Value=12 }).Value; } } }");
            Assert.AreEqual(12, Execute(result));
        }

        [TestMethod]
        public void NullableUpdateConstructsWithConfiguredArgumentsWithoutRepeatingBoundSetters()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper {
[MapProperty(Source = nameof(Source.Raw), Target = nameof(Target.Value), Use = nameof(Parse))]
public static partial Target Update(Source source, Target? target);
private static int Parse(string value) => int.Parse(value);
}
public class Source { public string Raw { get; set; } = ""12""; public int Other { get; set; } = 7; }
public class Target {
private int writes; private int value;
public Target(int value) { this.value = value; }
public int Value { get => value; set { this.value = value; writes++; } }
public int Other { get; set; }
public int Writes() => writes;
}
public static class Probe { public static int Run() {
var source = new Source(); var created = Mapper.Update(source, null);
if (created.Value != 12 || created.Other != 7 || created.Writes() != 0) return -1;
source.Raw = ""23""; var updated = Mapper.Update(source, created);
return object.ReferenceEquals(created, updated) && updated.Value == 23 && updated.Writes() == 1 ? 1 : 0;
} }");
            Assert.AreEqual(1, Execute(result));
        }

        [TestMethod]
        public void NullableUpdateInitializesRequiredMembersWhenCreatingDestination()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { public static partial Target Update(Source source, Target? target); }
public class Source { public int Id { get; set; } = 12; public string Name { get; set; } = ""created""; }
public class Target { public Target(int id) { Id=id; } public int Id { get; set; } public required string Name { get; set; } }
public static class Probe { public static int Run() { var target=Mapper.Update(new Source(), null); return target.Name == ""created"" ? target.Id : 0; } }");
            Assert.AreEqual(12, Execute(result));
        }

        [TestMethod]
        public void NullableUpdatePreservesApprovedOptionalConstructorDefault()
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper {
[UseTargetDefault(nameof(Target.Value))] public static partial Target Update(Source source, Target? target);
}
public class Source { public int Value { get; set; } = 99; }
public class Target { public Target(int value = 19) { Value=value; } public int Value { get; set; } }
public static class Probe { public static int Run() => Mapper.Update(new Source(), null).Value; }");
            Assert.AreEqual(19, Execute(result));
        }

        [TestMethod]
        [DataRow("", "public Target(int value) { Value=value; }", "")]
        [DataRow("[IgnoreTarget(nameof(Target.Value))]", "public Target() { }", "")]
        [DataRow("[UseTargetDefault(nameof(Target.Value))]", "public Target() { }", " = 7;")]
        public void NullableUpdateCreationMustSatisfyRequiredLanguageContract(string configuration, string constructor, string initializer)
        {
            var result = Run(@"using Mammoth.LiteMapper;
[LiteMapper] public static partial class Mapper { " + configuration + @" public static partial Target Update(Source source, Target? target); }
public class Source { public int Value { get; set; } }
public class Target { " + constructor + " public required int Value { get; set; }" + initializer + " }");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER1002"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.IsFalse(result.RunResult.Results.SelectMany(r => r.GeneratedSources).Any());
        }

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static CSharpCompilation CreateCompilation(string source, string name, MetadataReference? reference = null)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>()
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            if (reference != null) references = references.Append(reference);
            return CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11)) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source, MetadataReference? reference = null)
        {
            var compilation = CreateCompilation(source, "ConstructorTests", reference);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
