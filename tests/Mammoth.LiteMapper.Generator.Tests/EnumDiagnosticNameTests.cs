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
    public sealed class EnumDiagnosticNameTests
    {
        [TestMethod]
        [DataRow("create", true, false, false)]
        [DataRow("create", false, true, false)]
        [DataRow("create", true, true, false)]
        [DataRow("create", true, true, true)]
        [DataRow("constructor", true, false, false)]
        [DataRow("constructor", true, true, false)]
        [DataRow("update", true, false, false)]
        [DataRow("update", true, true, false)]
        [DataRow("patch", true, false, false)]
        [DataRow("patch", true, true, false)]
        [DataRow("flags", true, false, false)]
        [DataRow("flags", true, true, true)]
        public void ConfiguredPathsKeepStableExceptionMetadataAndReadEachGetterOnce(string mode, bool nullableChild, bool nullableLeaf, bool nullableTarget)
        {
            AssertRuns(Generate(PathFixture(mode, nullableChild, nullableLeaf, nullableTarget)));
        }

        [TestMethod]
        [DataRow("input", false)]
        [DataRow("input", true)]
        [DataRow("@event", false)]
        [DataRow("__nullableEnumValue", true)]
        public void RootEnumMetadataUsesTheDeclaredParameter(string parameter, bool nullable)
        {
            var suffix = nullable ? "?" : string.Empty;
            var name = parameter.TrimStart('@');
            AssertRuns(Generate(Preamble + @"
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
public static partial class Mapper { public static partial To Map(From" + suffix + " " + parameter + @"); }
public static class Probe
{
    public static bool Run()
    {
        if (Mapper.Map(From.Ready) != To.Ready) return false;
        try { Mapper.Map((From)99); return false; }
        catch (ArgumentOutOfRangeException ex)
        {
            return ex.ParamName == """ + name + @""" && ex.ActualValue is From value && value == (From)99;
        }
    }
}"));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void DirectMemberMetadataUsesTheSourceMemberEvenAfterCapture(bool nullable, bool patch)
        {
            var suffix = nullable ? "?" : string.Empty;
            var call = patch ? "Mapper.Map(source, new Target())" : "Mapper.Map(source)";
            AssertRuns(Generate(Preamble + @"
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw, IgnoreNullSourceMembers = " + (patch ? "true" : "false") + @")]
public static partial class Mapper
{
    public static partial Target Map(Source source" + (patch ? ", Target destination" : "") + @");
}
public sealed class Source
{
    public int Reads;
    public From" + suffix + @" State { get { Reads++; return (From)99; } }
}
public sealed class Target { public To state { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var source = new Source();
        try { " + call + @"; return false; }
        catch (ArgumentOutOfRangeException ex)
        {
            return ex.ParamName == ""State"" && ex.ActualValue is From value && value == (From)99 && source.Reads == 1;
        }
    }
}"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ErrorPolicyStillRejectsNullableConfiguredPaths(bool nullableLeaf)
        {
            var result = Generate(PathFixture("create", true, nullableLeaf, false).Replace("NullableMismatchPolicy.Throw", "NullableMismatchPolicy.Error"));
            Assert.IsTrue(result.Result.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER2001"));
            Assert.IsFalse(result.Result.Diagnostics.Any(diagnostic => diagnostic.Id == "LITEMAPPER9001"));
            Assert.AreEqual(0, result.Result.GeneratedTrees.Length);
        }

        [TestMethod]
        public void NullableIntermediateToNullableTargetCompilesWithStableExceptionMetadata()
        {
            // Runtime null preservation for a non-null leaf is a separate, pre-existing defect.
            var result = Generate(PathFixture("create", true, false, true));
            AssertCompiles(result);
            StringAssert.Contains(result.Result.GeneratedTrees.Single().ToString(), "ArgumentOutOfRangeException(\"Child.State\"");
        }

        [TestMethod]
        [DataRow("sequence", false, "item")]
        [DataRow("sequence", true, "item")]
        [DataRow("key", false, "Key")]
        [DataRow("value", false, "Value")]
        [DataRow("value", true, "Value")]
        public void CollectionEnumNamesSurviveNullableCaptures(string boundary, bool nullable, string expectedName)
        {
            var suffix = nullable ? "?" : string.Empty;
            var sourceType = boundary == "sequence" ? "From" + suffix + "[]"
                : boundary == "key" ? "Dictionary<From, int>" : "Dictionary<int, From" + suffix + ">";
            var targetType = boundary == "sequence" ? "To" + suffix + "[]"
                : boundary == "key" ? "Dictionary<To, int>" : "Dictionary<int, To" + suffix + ">";
            var input = boundary == "sequence" ? "new From" + suffix + "[] { (From)99 }"
                : boundary == "key" ? "new Dictionary<From, int> { [(From)99] = 1 }"
                : "new Dictionary<int, From" + suffix + "> { [1] = (From)99 }";
            AssertRuns(Generate("using System.Collections.Generic; " + Preamble + @"
[LiteMapper] public static partial class Mapper { public static partial " + targetType + " Map(" + sourceType + @" source); }
public static class Probe
{
    public static bool Run()
    {
        try { Mapper.Map(" + input + @"); return false; }
        catch (ArgumentOutOfRangeException ex)
        {
            return ex.ParamName == """ + expectedName + @""" && ex.ActualValue is From value && value == (From)99;
        }
    }
}"));
        }

        [TestMethod]
        public void NullableTupleFieldPathUsesItsConfiguredName()
        {
            var source = PathFixture("create", true, false, false)
                .Replace("Child.State", "Child.Values.Item1")
                .Replace("public From State { get { Reads++; return state; } }",
                    "public (From, int) Values { get { Reads++; return (state, 1); } }");
            AssertRuns(Generate(source));
        }

        [TestMethod]
        public void DeclaredUnmatchedValueKeepsConfiguredPathMetadata()
        {
            var source = PathFixture("create", true, false, false)
                .Replace("Other = 2", "Other = 2, Unmatched = 99")
                .Replace("NullableMismatch = NullableMismatchPolicy.Throw,", "UnmatchedEnumValues = UnmatchedEnumValuePolicy.Throw, NullableMismatch = NullableMismatchPolicy.Throw,");
            AssertRuns(Generate(source));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void GetterExceptionsPropagateWithoutReevaluation(bool terminal)
        {
            var source = PathFixture("create", true, false, false);
            source = source.Replace(terminal ? "Reads++; return state;" : "Reads++; return child;", "Reads++; throw Probe.Failure;");
            source = source.Substring(0, source.IndexOf("public static class Probe", StringComparison.Ordinal)) + @"
public static class Probe
{
    public static readonly Exception Failure = new Exception(""getter"");
    public static bool Run()
    {
        var child = new Child(From.Ready);
        var source = new Source(child);
        try { Mapper.Map(source); return false; }
        catch (Exception ex) { return object.ReferenceEquals(ex, Failure) && source.Reads == 1 && child.Reads == " + (terminal ? "1" : "0") + @"; }
    }
}";
            AssertRuns(Generate(source));
        }

        [TestMethod]
        public void ExceptionMetadataIsDeterministic()
        {
            var source = PathFixture("create", true, false, false);
            var first = Generate(source);
            var second = Generate(source);
            AssertRuns(first);
            CollectionAssert.AreEqual(first.Result.GeneratedTrees.Select(tree => tree.ToString()).ToArray(),
                second.Result.GeneratedTrees.Select(tree => tree.ToString()).ToArray());
        }

        private const string Preamble = "using System; using Mammoth.LiteMapper; public enum From { None = 0, Ready = 1, Other = 2 } public enum To { None = 0, Ready = 4, Other = 8 } ";

        private static string PathFixture(string mode, bool nullableChild, bool nullableLeaf, bool nullableTarget)
        {
            var update = mode == "update" || mode == "patch";
            var patch = mode == "patch";
            var inputType = "From" + (nullableLeaf ? "?" : "");
            var childType = "Child" + (nullableChild ? "?" : "");
            var targetType = "To" + (nullableTarget ? "?" : "");
            var invoke = update ? "Mapper.Map(source, destination)" : "Mapper.Map(source)";
            var target = mode == "constructor"
                ? "public sealed class Target { public Target(" + targetType + " state) { State = state; } public " + targetType + " State { get; } }"
                : "public sealed class Target { public " + targetType + " State { get; set; } }";
            var destination = mode == "constructor" ? "new Target(To.Other)" : "new Target { State = To.Other }";
            var nullCheck = patch ? "return object.ReferenceEquals(mapped, destination) && mapped.State == To.Other;"
                : nullableTarget ? "return mapped.State == null;" : "return false;";
            var nullCatch = !patch && !nullableTarget ? "return ex.Message.Contains(\"Child.State\");" : "return false;";
            return (mode == "flags" ? Preamble.Replace("public enum", "[Flags] public enum") : Preamble) + @"
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw, IgnoreNullSourceMembers = " + (patch ? "true" : "false") + @")]
public static partial class Mapper
{
    [MapProperty(Source = ""Child.State"", Target = nameof(Target.State))]
    public static partial Target Map(Source source" + (update ? ", Target destination" : "") + @");
}
public sealed class Source
{
    private readonly " + childType + @" child;
    public int Reads;
    public Source(" + childType + @" child) { this.child = child; }
    public " + childType + @" Child { get { Reads++; return child; } }
}
public sealed class Child
{
    private readonly " + inputType + @" state;
    public int Reads;
    public Child(" + inputType + @" state) { this.state = state; }
    public " + inputType + @" State { get { Reads++; return state; } }
}
" + target + @"
public static class Probe
{
    private static bool Check(" + childType + @" child, int scenario)
    {
        var source = new Source(child);
        var destination = " + destination + @";
        try
        {
            var mapped = " + invoke + @";
            if (scenario == 0) return mapped.State == To.Ready;
            if (scenario == 3) return mapped.State == (To.Ready | To.Other);
            if (scenario == 1) return false;
            " + nullCheck + @"
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return scenario == 1 && ex.ParamName == ""Child.State"" && ex.ActualValue is From value && value == (From)99;
        }
        catch (InvalidOperationException ex)
        {
            if (scenario != 2) return false;
            " + nullCatch + @"
        }
        finally
        {
            if (source.Reads != 1 || (child != null && child.Reads != 1))
                throw new Exception(""Source getters were not evaluated exactly once."");
        }
    }
    public static bool Run() => Check(new Child(From.Ready), 0) && Check(new Child((From)99), 1)" +
                (nullableChild ? " && Check(null, 2)" : "") + (nullableLeaf ? " && Check(new Child(null), 2)" : "") +
                (mode == "flags" ? " && Check(new Child(From.Ready | From.Other), 3)" : "") + @";
}";
        }

        private static (GeneratorDriverRunResult Result, Compilation Compilation) Generate(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var compilation = CSharpCompilation.Create("EnumDiagnosticNameTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, "Invalid test input: " + string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }


        private static void AssertCompiles((GeneratorDriverRunResult Result, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.Result.Diagnostics.Length, string.Join(Environment.NewLine, result.Result.Diagnostics));
            var errors = result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error ||
                diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Location.SourceTree != null && result.Result.GeneratedTrees.Contains(diagnostic.Location.SourceTree)).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        private static void AssertRuns((GeneratorDriverRunResult Result, Compilation Compilation) result)
        {
            AssertCompiles(result);
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }
    }
}
