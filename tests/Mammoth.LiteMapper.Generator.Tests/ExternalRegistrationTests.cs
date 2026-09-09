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
    public sealed class ExternalRegistrationTests
    {
        [TestMethod]
        [DataRow("ExternalMapper", false)]
        [DataRow("MissingContainer", false)]
        [DataRow("ExternalMapper", true)]
        [DataRow("MissingContainer", true)]
        public void MissingRegistrationUsesMissingTypeDiagnosticRegardlessOfSpelling(string missingType, bool assemblyScope)
        {
            var result = Run(RegistrationSource(missingType, string.Empty, assemblyScope), allowMissingType: true);
            AssertInvalidRegistration(result, "LITEMAPPER0010", assemblyScope);
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER0011"),
                "A missing symbol is not evidence that an existing container is non-static.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExistingNonStaticRegistrationRetainsItsSpecificDiagnostic(bool assemblyScope)
        {
            var result = Run(RegistrationSource("External", "public class External { public static int Convert(int value) => value + 10; }", assemblyScope));
            AssertInvalidRegistration(result, "LITEMAPPER0011", assemblyScope);
        }

        [TestMethod]
        [DataRow("", false)]
        [DataRow("static External() { }", false)]
        [DataRow("public static int Value => 3;", false)]
        [DataRow("public static int Convert() => 3;", false)]
        [DataRow("public static void Convert(int value) { }", false)]
        [DataRow("public static int Convert(int first, int second, int third) => first;", false)]
        [DataRow("public static int Convert<T>(int value) => value;", false)]
        [DataRow("public static int Convert(ref int value) => value;", false)]
        [DataRow("public static System.Threading.Tasks.Task<int> Convert(int value) => System.Threading.Tasks.Task.FromResult(value);", false)]
        [DataRow("public static async System.Threading.Tasks.Task<int> Convert(int value) => await System.Threading.Tasks.Task.FromResult(value);", false)]
        [DataRow("static External() { }", true)]
        [DataRow("public static int Convert<T>(int value) => value;", true)]
        public void StaticContainerMustExposeAUsableMappingNotMerelyAMethodSymbol(string members, bool assemblyScope)
        {
            var result = Run(RegistrationSource("External", "public static class External { " + members + " }", assemblyScope));
            AssertInvalidRegistration(result, "LITEMAPPER0010", assemblyScope);
        }

        [TestMethod]
        [DataRow("External<>", "public static class External<T> { public static int Convert(int value) => value + 10; }")]
        [DataRow("External<int>", "public static class External<T> { public static int Convert(int value) => value + 10; }")]
        [DataRow("Outer<int>.External", "public class Outer<T> { public static class External { public static int Convert(int value) => value + 10; } }")]
        public void GenericRegisteredContainersRemainDeferredEvenWhenTypeArgumentsAreClosed(string registeredType, string container)
        {
            AssertInvalidRegistration(Run(RegistrationSource(registeredType, container, false)), "LITEMAPPER0010", false);
        }

        [TestMethod]
        public void InaccessiblePrivateMethodDoesNotMakeAnExternalContainerUsable()
        {
            var result = Run(RegistrationSource("External", "public static class External { private static int Convert(int value) => value + 10; }", false));
            AssertInvalidRegistration(result, "LITEMAPPER0010", false);
        }

        [TestMethod]
        public void UsableMethodForAnUnrelatedPairDoesNotInvalidateRegistration()
        {
            var source = RegistrationSource("External", "public static class External { public static int Parse(string value) => int.Parse(value); }", false) +
                "public static class Probe { public static int Run() => RegisteredMapper.Map(new Source { Value = 3 }).Value; }";
            Assert.AreEqual(3, Execute(Run(source)), "Registration validity must not depend on matching every current mapping pair.");
        }

        [TestMethod]
        [DataRow("public static void Apply(Source source, Target target) { target.Value = source.Value; }")]
        [DataRow("public static Target Apply(Source source, Target target) { target.Value = source.Value; return target; }")]
        [DataRow("public static void Apply(Source source, ref ValueTarget target) { target.Value = source.Value; }")]
        public void ExistingTargetMethodMakesRegistrationUsableWithoutAOneArgumentConverter(string method)
        {
            var source = RegistrationSource("External", "public static class External { " + method + " } public struct ValueTarget { public int Value; }", false) +
                "public static class Probe { public static int Run() => RegisteredMapper.Map(new Source { Value = 3 }).Value; }";
            Assert.AreEqual(3, Execute(Run(source)));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void PrivateConverterIsUsableFromItsNestedMapperUnderNormalAccessibility(bool assemblyScope)
        {
            var source = "using Mammoth.LiteMapper; " +
                (assemblyScope ? "[assembly: UseMapper(typeof(External))] " : string.Empty) +
                "public static partial class External { private static int Convert(int value) => value + 10; " +
                "[LiteMapper" + (assemblyScope ? string.Empty : ", UseMapper(typeof(External))") + "] public static partial class Mapper { " +
                "public static partial Target Map(Source source); } } " + Models +
                "public static class Probe { public static int Run() => External.Mapper.Map(new Source { Value = 3 }).Value; }";
            Assert.AreEqual(13, Execute(Run(source)), "Accessibility is relative to the generated mapper, including for assembly registrations.");
        }

        [TestMethod]
        public void ValidUnmarkedConverterSurvivesUnusableHelpersInTheSameContainer()
        {
            var source = RegistrationSource("External", @"public static class External {
    static External() { }
    public static int Value => 99;
    public static int Generic<T>(int value) => 99;
    public static int Convert(int value) => value + 10;
}", false) + "public static class Probe { public static int Run() => RegisteredMapper.Map(new Source { Value = 3 }).Value; }";
            Assert.AreEqual(13, Execute(Run(source)), "Explicit registration opts in the valid unmarked handwritten method.");
        }

        [TestMethod]
        [DataRow("ref")]
        [DataRow("ref readonly")]
        public void ReferenceReturningConverterCanSupplyAnOrdinaryValueCopy(string returnKind)
        {
            var result = Run("using Mammoth.LiteMapper; " +
                "public class Source { public int Value; } public class Target { public int Value { get; set; } } " +
                "public static class External { public static " + returnKind + " int Convert(Source source) => ref source.Value; } " +
                "[LiteMapper, UseMapper(typeof(External))] public static partial class Mapper { " +
                "[MapProperty(Target = nameof(Target.Value), Use = nameof(External.Convert))] public static partial Target Map(Source source); } " +
                "public static class Probe { public static int Run() { var source = new Source { Value = 3 }; " +
                "var target = Mapper.Map(source); source.Value = 7; return target.Value; } }");
            Assert.AreEqual(3, Execute(result), "Value assignment must copy the converter result rather than retain an alias to source storage.");
        }

        private const string Models = "public class Source { public int Value { get; set; } } public class Target { public int Value { get; set; } } ";

        private static string RegistrationSource(string registeredType, string container, bool assemblyScope)
        {
            var registration = "UseMapper(typeof(" + registeredType + "))";
            return "using Mammoth.LiteMapper; " + (assemblyScope ? "[assembly: " + registration + "] " : string.Empty) + container +
                "[LiteMapper" + (assemblyScope ? string.Empty : ", " + registration) + "] public static partial class RegisteredMapper { " +
                "public static partial Target Map(Source source); } " +
                "[LiteMapper] public static partial class HealthyMapper { public static partial Target Map(Source source); } " + Models;
        }

        private static void AssertInvalidRegistration((GeneratorDriverRunResult RunResult, Compilation Compilation) result, string diagnostic, bool assemblyScope)
        {
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == diagnostic), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            var invalidMethod = result.Compilation.GetTypeByMetadataName("RegisteredMapper")!.GetMembers("Map").OfType<IMethodSymbol>().Single();
            Assert.IsNull(invalidMethod.PartialImplementationPart, "An invalid registration must not generate a mapping implementation.");
            var healthyMethod = result.Compilation.GetTypeByMetadataName("HealthyMapper")!.GetMembers("Map").OfType<IMethodSymbol>().Single();
            if (assemblyScope)
            {
                Assert.IsNull(healthyMethod.PartialImplementationPart, "Fatal assembly configuration affects every mapper in that assembly.");
            }
            else
            {
                Assert.IsNotNull(healthyMethod.PartialImplementationPart, "Class registration errors must preserve an unrelated mapper.");
            }
        }

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.RunResult.Diagnostics.Length, string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
        }

        private static (GeneratorDriverRunResult RunResult, Compilation Compilation) Run(string source, bool allowMissingType = false)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("ExternalRegistrationTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795" &&
                !(allowMissingType && d.Id == "CS0246")).ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Only missing partial implementations and deliberately missing registration types are allowed input errors. " +
                string.Join(Environment.NewLine, inputErrors.Select(static d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
