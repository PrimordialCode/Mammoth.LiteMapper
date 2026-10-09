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
    public sealed class PatchNestedUpdaterCaptureTests
    {
        [TestMethod]
        [DataRow("declared-void", "present")]
        [DataRow("declared-void", "null")]
        [DataRow("declared-void", "changing")]
        [DataRow("declared-void", "null-second")]
        [DataRow("declared-return", "present")]
        [DataRow("declared-return", "null")]
        [DataRow("declared-return", "changing")]
        [DataRow("declared-return", "null-second")]
        [DataRow("default-void", "present")]
        [DataRow("default-void", "null")]
        [DataRow("default-void", "changing")]
        [DataRow("default-void", "null-second")]
        [DataRow("default-return", "present")]
        [DataRow("default-return", "null")]
        [DataRow("default-return", "changing")]
        [DataRow("default-return", "null-second")]
        [DataRow("explicit-void", "present")]
        [DataRow("explicit-void", "null")]
        [DataRow("explicit-void", "changing")]
        [DataRow("explicit-void", "null-second")]
        [DataRow("explicit-return", "present")]
        [DataRow("explicit-return", "null")]
        [DataRow("explicit-return", "changing")]
        [DataRow("explicit-return", "null-second")]
        public void PatchGuardAndNestedUpdaterConsumeTheSameValue(string updaterKind, string getter)
        {
            var result = Run(Fixture(updaterKind));
            CollectionAssert.AreEqual(new[] { 1, getter == "null" ? 0 : 1, getter == "null" ? 42 : 7, 1 },
                Execute<int[]>(result, getter),
                "A nested updater must consume the guarded first value once, or leave the existing child untouched when it is null.");
        }

        [TestMethod]
        [DataRow("present")]
        [DataRow("null")]
        [DataRow("changing")]
        [DataRow("null-second")]
        public void ConfiguredNestedPathRetainsSingleEvaluation(string getter)
        {
            var source = Fixture("explicit-return")
                .Replace("Source = nameof(Source.Child)", "Source = \"Container.Child\"")
                .Replace("public sealed class Source\n{", "public sealed class Source\n{\n    private int containerReads;\n    public Source Container { get { if (++containerReads > 1) throw new Exception(\"Container read twice.\"); return this; } }");
            CollectionAssert.AreEqual(new[] { 1, getter == "null" ? 0 : 1, getter == "null" ? 42 : 7, 1 },
                Execute<int[]>(Run(source), getter),
                "Configured-path segments and the leaf must continue to share their patch capture.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void InstanceUpdaterUsesTheDirectCapture(bool nullableParameter)
        {
            var source = Fixture("default-void")
                .Replace("public static partial class Mapper", "public partial class Mapper")
                .Replace("public static partial Target Apply", "public partial Target Apply")
                .Replace("private static void ApplyChild", "private void ApplyChild")
                .Replace("target.Value = source.Value;", "target.Value = " + (nullableParameter ? "source?.Value ?? -1" : "source.Value") + ";")
                .Replace("var returned = Mapper.Apply(source, target);", "var returned = new Mapper().Apply(source, target);");
            if (nullableParameter)
                source = source.Replace("ApplyChild(ChildSource source", "ApplyChild(ChildSource? source");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "changing"),
                "Instance and nullable-parameter updaters must receive the guarded source instance.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void LaterFailureRetainsEarlierAssignmentsAndDoesNotReadLaterMembers(bool updaterThrows)
        {
            var source = Fixture("default-void")
                .Replace("target.Value = source.Value;", "target.Value = source.Value;" + (updaterThrows ? " throw Probe.Failure;" : ""))
                .Replace("public ChildSource? Child", "public int Alpha => 3;\n    public int Later => throw Probe.Failure;\n    public int Zulu => throw new Exception(\"Later member must not run.\");\n    public ChildSource? Child")
                .Replace("public sealed class Target\n{", "public sealed class Target\n{\n    public int Alpha { get; set; }\n    public int Later { get; set; } = 51;\n    public int Zulu { get; set; } = 61;")
                .Replace("public static class Probe\n{", "public static class Probe\n{\n    public static readonly Exception Failure = new Exception(\"Expected failure.\");")
                .Replace("var returned = Mapper.Apply(source, target);", @"
        try { Mapper.Apply(source, target); throw new Exception(""Expected failure was lost.""); }
        catch (Exception error) when (object.ReferenceEquals(error, Failure)) { }
        var returned = target;
        if (target.Alpha != 3 || target.Later != 51 || target.Zulu != 61)
            throw new Exception(""Member updates lost their sequential failure behavior."");");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "changing"),
                "The direct capture stays inside its member update; a later failure preserves earlier mutations and exception identity.");
        }

        [TestMethod]
        public void ThrowPolicyStillConsumesTheGuardedDirectValue()
        {
            var source = Fixture("declared-return")
                .Replace("IgnoreNullSourceMembers = true", "NullableMismatch = NullableMismatchPolicy.Throw");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "null-second"),
                "Adding patch capture preference must preserve the existing Throw-policy capture.");
        }

        [TestMethod]
        public void PatchCaptureRemainsDeterministicWithUnusualParameterNames()
        {
            var source = Fixture("declared-void")
                .Replace("Source source, Target target", "Source __value_Child, Target __source_Child");
            var first = Run(source);
            var second = Run(source);
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(first, "changing"));
            Assert.AreEqual(first.Source, second.Source,
                "Equivalent compilation inputs must retain identical generated capture names and output.");
        }

        [TestMethod]
        [DataRow("present")]
        [DataRow("null")]
        [DataRow("changing")]
        [DataRow("null-second")]
        public void NullableValueCapturePreservesSelectedUpdaterOverload(string getter)
        {
            var source = Fixture("default-void")
                .Replace("public sealed class ChildSource", "public struct ChildSource")
                .Replace("ApplyChild(ChildSource source", "ApplyChild(ChildSource? source")
                .Replace("target.Value = source.Value;", "target.Value = source?.Value ?? -1;")
                .Replace("[DefaultMapping]", "private static void ApplyChild(ChildSource source, ChildTarget target) { target.Value = 99; } [DefaultMapping]");
            var actual = Execute<int[]>(Run(source), getter);
            Assert.AreEqual(getter == "null" ? 42 : 7, actual[2], "Unwrapping a capture must not make an unmarked overload override the selected nullable updater.");
            Assert.AreEqual(1, actual[0], "Nullable value getters must be captured exactly once too.");
        }

        [TestMethod]
        [DataRow("Child", "explicit-void", "present")]
        [DataRow("Child", "explicit-void", "null")]
        [DataRow("Child", "explicit-void", "changing")]
        [DataRow("Child", "explicit-void", "null-second")]
        [DataRow("Child", "explicit-return", "present")]
        [DataRow("Child", "explicit-return", "null")]
        [DataRow("Child", "explicit-return", "changing")]
        [DataRow("Child", "explicit-return", "null-second")]
        [DataRow("Container.Child", "explicit-void", "present")]
        [DataRow("Container.Child", "explicit-void", "null")]
        [DataRow("Container.Child", "explicit-void", "changing")]
        [DataRow("Container.Child", "explicit-void", "null-second")]
        [DataRow("Container.Child", "explicit-return", "present")]
        [DataRow("Container.Child", "explicit-return", "null")]
        [DataRow("Container.Child", "explicit-return", "changing")]
        [DataRow("Container.Child", "explicit-return", "null-second")]
        [DataRow("Container.Child", "explicit-void", "missing-container")]
        [DataRow("Container.Child", "explicit-return", "missing-container")]
        public void ConfiguredNullableValueCapturePreservesSelectedUpdaterOverload(string path, string updaterKind, string getter)
        {
            var result = Run(NullableOverloadFixture(path, updaterKind));
            var skipped = getter == "null" || getter == "missing-container";
            CollectionAssert.AreEqual(new[] { getter == "missing-container" ? 0 : 1, skipped ? 0 : 1, skipped ? 42 : 7, 1 },
                Execute<int[]>(result, getter),
                "Explicit source-path selection must preserve the nullable overload and evaluate each guarded segment only once.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ConfiguredNullableOverloadRetainsGetOnlyOrReturnedReplacement(bool replacement)
        {
            var source = NullableOverloadFixture("Container.Child", "explicit-return");
            if (replacement)
            {
                source = source.Replace("target.Value = source?.Value ?? -1; return target;", "return new ChildTarget { Value = source?.Value ?? -1 };")
                    .Replace("object.ReferenceEquals(child, target.Child)", "!object.ReferenceEquals(child, target.Child)");
            }
            else
            {
                source = source.Replace("public ChildTarget Child { get; set;", "public ChildTarget Child { get;");
            }
            CollectionAssert.AreEqual(new[] { 1, replacement ? 0 : 1, 7, 1 }, Execute<int[]>(Run(source), "null-second"),
                "A selected returning updater must retain get-only mutation or writable replacement semantics.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ConfiguredNullableOverloadRetainsInstanceAndExternalReceiver(bool external)
        {
            var source = NullableOverloadFixture("Container.Child", "explicit-void");
            if (external)
            {
                source = source.Replace("Use = nameof(ApplyChild)", "Use = nameof(External.ApplyChild), ConverterType = typeof(External)")
                    .Replace("    public static partial Target Apply(Source source, Target target);", "    public static partial Target Apply(Source source, Target target);\n}\npublic static class External\n{")
                    .Replace("private static void ApplyChild", "public static void ApplyChild");
            }
            else
            {
                source = source.Replace("public static partial class Mapper", "public partial class Mapper")
                    .Replace("public static partial Target Apply", "public partial Target Apply")
                    .Replace("private static void ApplyChild", "private void ApplyChild")
                    .Replace("Mapper.Apply(source, target)", "new Mapper().Apply(source, target)");
            }
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "changing"),
                "The selected receiver and overload must both survive the configured patch capture.");
        }

        [TestMethod]
        public void ConfiguredNullableOverloadRemainsDeterministic()
        {
            var source = NullableOverloadFixture("Container.Child", "explicit-void");
            var first = Run(source);
            Assert.AreEqual(first.Source, Run(source).Source,
                "Restoring the selected nullable type must not introduce unstable output.");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(first, "present"));
        }

        [TestMethod]
        public void ConfiguredNullableOverloadRetainsNonPatchControl()
        {
            var source = NullableOverloadFixture("Child", "explicit-void")
                .Replace("IgnoreNullSourceMembers = true", "NullableMismatch = NullableMismatchPolicy.Throw");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "present"));
            CollectionAssert.AreEqual(new[] { 1, 1, -1, 1 }, Execute<int[]>(Run(source), "null"),
                "Without patch skipping, the selected nullable updater must still receive null.");
        }

        [TestMethod]
        [DataRow("Child")]
        [DataRow("Container.Child")]
        public void ConfiguredSourceOnlyPreservesDefaultNullableUpdater(string path)
        {
            var source = NullableOverloadFixture(path, "explicit-void")
                .Replace(", Use = nameof(ApplyChild)", "")
                .Replace("private static void ApplyChild(ChildSource? source", "[DefaultMapping] private static void ApplyChild(ChildSource? source")
                .Replace("public ChildTarget Child { get; set;", "public ChildTarget Child { get;");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "changing"),
                "Configuring only Source must preserve a selected nullable default updater too.");
        }

        [TestMethod]
        [DataRow("present")]
        [DataRow("missing-container")]
        public void NullableIntermediateDoesNotChangeNonNullableLeafOverload(string getter)
        {
            var source = NullableOverloadFixture("Container.Child", "explicit-void")
                .Replace("public ChildSource? Child", "public ChildSource Child")
                .Replace("if (behavior == \"null\") return null;", "")
                .Replace("return behavior == \"null-second\" ? null : new ChildSource { Value = 99 };", "return new ChildSource { Value = 99 };");
            var skipped = getter == "missing-container";
            CollectionAssert.AreEqual(new[] { skipped ? 0 : 1, skipped ? 0 : 1, skipped ? 42 : 99, 1 },
                Execute<int[]>(Run(source), getter),
                "A nullable intermediate must not make a non-nullable terminal member choose the nullable overload.");
        }

        [TestMethod]
        public void ConfiguredNullableOverloadRetainsSequentialFailureBehavior()
        {
            var source = NullableOverloadFixture("Container.Child", "explicit-void")
                .Replace("public ChildSource? Child", "public int Alpha => 3;\n    public int Later => throw Probe.Failure;\n    public int Zulu => throw new Exception(\"Later member must not run.\");\n    public ChildSource? Child")
                .Replace("public sealed class Target\n{", "public sealed class Target\n{\n    public int Alpha { get; set; }\n    public int Later { get; set; } = 51;\n    public int Zulu { get; set; } = 61;")
                .Replace("public static class Probe\n{", "public static class Probe\n{\n    public static readonly Exception Failure = new Exception(\"Expected failure.\");")
                .Replace("var returned = Mapper.Apply(source, target);", @"
        try { Mapper.Apply(source, target); throw new Exception(""Expected failure was lost.""); }
        catch (Exception error) when (object.ReferenceEquals(error, Failure)) { }
        var returned = target;
        if (target.Alpha != 3 || target.Later != 51 || target.Zulu != 61)
            throw new Exception(""Member updates lost their sequential failure behavior."");");
            CollectionAssert.AreEqual(new[] { 1, 1, 7, 1 }, Execute<int[]>(Run(source), "null-second"),
                "The configured capture must stay inside its member update and preserve prior mutations on later failure.");
        }

        private static string NullableOverloadFixture(string path, string updaterKind)
        {
            var source = Fixture(updaterKind)
                .Replace("Source = nameof(Source.Child)", "Source = \"" + path + "\"")
                .Replace("public sealed class ChildSource", "public struct ChildSource")
                .Replace("ApplyChild(ChildSource source", "ApplyChild(ChildSource? source")
                .Replace("target.Value = source.Value;", "target.Value = source?.Value ?? -1;")
                .Replace("public sealed class Source\n{", "public sealed class Source\n{\n    private int containerReads;\n    public Source? Container { get { if (++containerReads > 1) throw new Exception(\"Container read twice.\"); return behavior == \"missing-container\" ? null : this; } }")
                .Replace("    public static partial Target Apply(Source source, Target target);", "    public static partial Target Apply(Source source, Target target);\n    private static " +
                    (updaterKind.EndsWith("return", StringComparison.Ordinal) ? "ChildTarget" : "void") +
                    " ApplyChild(ChildSource source, ChildTarget target) { target.Value = 99;" +
                    (updaterKind.EndsWith("return", StringComparison.Ordinal) ? " return target;" : "") + " }");
            return source;
        }

        private static string Fixture(string updaterKind)
        {
            var returns = updaterKind.EndsWith("return", StringComparison.Ordinal);
            var declared = updaterKind.StartsWith("declared", StringComparison.Ordinal);
            var explicitlySelected = updaterKind.StartsWith("explicit", StringComparison.Ordinal);
            var updater = declared
                ? "public static partial " + (returns ? "ChildTarget" : "void") + " ApplyChild(ChildSource source, ChildTarget target);"
                : (explicitlySelected ? "" : "[DefaultMapping] ") + "private static " + (returns ? "ChildTarget" : "void") +
                  " ApplyChild(ChildSource source, ChildTarget target) { target.Value = source.Value;" + (returns ? " return target;" : "") + " }";
            return (@"
#nullable enable
using System;
using Mammoth.LiteMapper;
[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class Mapper
{
    " + (explicitlySelected ? "[MapProperty(Source = nameof(Source.Child), Target = nameof(Target.Child), Use = nameof(ApplyChild))]" : "") + @"
    public static partial Target Apply(Source source, Target target);
    " + updater + @"
}
public sealed class Source
{
    private readonly string behavior;
    private int reads;
    private readonly ChildSource first = new ChildSource { Value = 7 };
    public Source(string behavior) { this.behavior = behavior; }
    public int ReadCount() => reads;
    public ChildSource? Child
    {
        get
        {
            reads++;
            if (behavior == ""null"") return null;
            if (reads == 1 || behavior == ""present"") return first;
            return behavior == ""null-second"" ? null : new ChildSource { Value = 99 };
        }
    }
}
public sealed class ChildSource { public int Value { get; set; } }
public sealed class ChildTarget
{
    private int value = 42;
    private int writes;
    public int Value { get => value; set { writes++; this.value = value; } }
    public int WriteCount() => writes;
}
public sealed class Target
{
    public ChildTarget Child { get; " + (explicitlySelected ? "set;" : "") + @" } = new ChildTarget();
}
public static class Probe
{
    public static int[] Run(string behavior)
    {
        var source = new Source(behavior);
        var target = new Target();
        var child = target.Child;
        var returned = Mapper.Apply(source, target);
        return new[] { source.ReadCount(), child.WriteCount(), target.Child.Value,
            object.ReferenceEquals(target, returned) && object.ReferenceEquals(child, target.Child) ? 1 : 0 };
    }
}
").Replace("\r\n", "\n");
        }

        private static (Compilation Compilation, string Source) Run(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var compilation = CSharpCompilation.Create("PatchUpdater_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795").ToArray();
            Assert.AreEqual(0, inputErrors.Length, string.Join(Environment.NewLine, inputErrors.Select(d => d.ToString())));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            Assert.AreEqual(0, driver.GetRunResult().Diagnostics.Length,
                string.Join(Environment.NewLine, driver.GetRunResult().Diagnostics));
            var problems = updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning).ToArray();
            Assert.AreEqual(0, problems.Length, string.Join(Environment.NewLine, problems.Select(d => d.ToString())));
            return (updatedCompilation, string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(t => t.ToString())));
        }

        private static T Execute<T>((Compilation Compilation, string Source) result, params object[] arguments)
        {
            using var stream = new MemoryStream();
            var emit = result.Compilation.Emit(stream);
            Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return (T)Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, arguments)!;
        }
    }
}
