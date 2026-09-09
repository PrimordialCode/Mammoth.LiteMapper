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
    public sealed class NullableValueMappingTests
    {
        [TestMethod]
        [DataRow("bool")]
        [DataRow("Guid")]
        [DataRow("DateTime")]
        [DataRow("Kind")]
        [DataRow("Value")]
        public void ErrorPolicyRejectsEveryNullableValueBoundary(string type)
        {
            foreach (var boundary in new[] { "root", "member", "element" })
            {
                var declaration = boundary == "root" ? "public static partial " + type + " Map(" + type + "? source);"
                    : boundary == "element" ? "public static partial " + type + "[] Map(" + type + "?[] source);"
                    : "public static partial Target Map(Source source);";
                var result = Run(Preamble + "[LiteMapper] public static partial class Mapper { " + declaration + " } public class Source { public " + type + "? Item { get; set; } } public class Target { public " + type + " Item { get; set; } }");
                var id = boundary == "element" ? "LITEMAPPER2003" : "LITEMAPPER2001";
                Assert.IsTrue(result.Result.Diagnostics.Any(d => d.Id == id), boundary + ": " + string.Join(Environment.NewLine, result.Result.Diagnostics));
                Assert.AreEqual(0, result.Result.GeneratedTrees.Length, "Invalid nullable mapping must be omitted: " + boundary);
            }
        }

        [TestMethod]
        [DataRow("bool", "true")]
        [DataRow("Guid", "Guid.NewGuid()")]
        [DataRow("DateTime", "new DateTime(2020, 1, 2)")]
        [DataRow("Kind", "Kind.One")]
        [DataRow("Value", "new Value { Number = 17 }")]
        public void ThrowPolicyChecksNullAndPreservesValuesAtEveryBoundary(string type, string value)
        {
            var result = Run(Preamble + @"
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class RootMapper { public static partial TYPE Map(TYPE? source); }
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class MemberMapper { public static partial Target Map(Source source); }
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class ElementMapper { public static partial TYPE[] Map(TYPE?[] source); }
public class Source { public TYPE? Item { get; set; } }
public class Target { public TYPE Item { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        TYPE value = VALUE;
        try { RootMapper.Map(null); return false; } catch (ArgumentNullException) { }
        try { MemberMapper.Map(new Source()); return false; } catch (InvalidOperationException ex) { if (!ex.Message.Contains(""Item"")) return false; }
        try { ElementMapper.Map(new TYPE?[] { null }); return false; } catch (InvalidOperationException ex) { if (!ex.Message.Contains(""item"")) return false; }
        return RootMapper.Map(value).Equals(value) && MemberMapper.Map(new Source { Item = value }).Item.Equals(value) && ElementMapper.Map(new TYPE?[] { value })[0].Equals(value);
    }
}".Replace("TYPE", type).Replace("VALUE", value));
            AssertProbe(result);
        }

        [TestMethod]
        public void NullableIdentityAndLiftingPreserveNullAndValues()
        {
            var result = Run(Preamble + @"
[LiteMapper] public static partial class Mapper
{
    public static partial bool? Identity(bool? source);
    public static partial bool? Lift(bool source);
}
public static class Probe { public static bool Run() => Mapper.Identity(null) == null && Mapper.Identity(true) == true && Mapper.Lift(false) == false; }");
            AssertProbe(result);
        }

        [TestMethod]
        [DataRow("bool?")]
        [DataRow("bool")]
        public void PatchNullPreservesTargetAndNonNullStillMaps(string targetType)
        {
            var result = Run(Preamble + @"
[LiteMapper(IgnoreNullSourceMembers = true)] public static partial class Mapper { public static partial void Apply(Source source, Target destination); }
public class Source { public bool? Flag { get; set; } }
public class Target { public TYPE Flag { get; set; } }
public static class Probe
{
    public static bool Run()
    {
        var target = new Target { Flag = true };
        Mapper.Apply(new Source(), target);
        if (target.Flag != true) return false;
        Mapper.Apply(new Source { Flag = false }, target);
        return target.Flag == false;
    }
}".Replace("TYPE", targetType));
            AssertProbe(result);
        }

        [TestMethod]
        [DataRow("root", "object")]
        [DataRow("member", "object")]
        [DataRow("element", "object")]
        [DataRow("root", "IComparable")]
        [DataRow("element", "IComparable")]
        public void NullableBoxingCannotBypassErrorPolicy(string boundary, string targetType)
        {
            var declaration = boundary == "root" ? "public static partial TARGET Map(int? source);"
                : boundary == "element" ? "public static partial TARGET[] Map(int?[] source);"
                : "public static partial Target Map(Source source);";
            var result = Run((Preamble + "[LiteMapper] public static partial class Mapper { " + declaration +
                " } public class Source { public int? Item { get; set; } } public class Target { public TARGET Item { get; set; } = null!; }").Replace("TARGET", targetType));
            var id = boundary == "element" ? "LITEMAPPER2003" : "LITEMAPPER2001";
            Assert.IsTrue(result.Result.Diagnostics.Any(d => d.Id == id), string.Join(Environment.NewLine, result.Result.Diagnostics));
            Assert.AreEqual(0, result.Result.GeneratedTrees.Length, "Boxing a null must not silently violate the target contract.");
        }

        [TestMethod]
        [DataRow("object")]
        [DataRow("IComparable")]
        public void NullableBoxingThrowChecksEveryBoundary(string targetType)
        {
            AssertProbe(Run((Preamble + @"
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class RootMapper { public static partial TARGET Map(int? source); }
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class MemberMapper { public static partial Target Map(Source source); }
[LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)] public static partial class ElementMapper { public static partial TARGET[] Map(int?[] source); }
public class Source { public int? Item { get; set; } }
public class Target { public TARGET Item { get; set; } = null!; }
public static class Probe
{
    public static bool Run()
    {
        try { RootMapper.Map(null); return false; } catch (ArgumentNullException) { }
        try { MemberMapper.Map(new Source()); return false; } catch (InvalidOperationException ex) { if (!ex.Message.Contains(""Item"")) return false; }
        try { ElementMapper.Map(new int?[] { null }); return false; } catch (InvalidOperationException ex) { if (!ex.Message.Contains(""item"")) return false; }
        return RootMapper.Map(3).Equals(3) && MemberMapper.Map(new Source { Item = 3 }).Item.Equals(3) && ElementMapper.Map(new int?[] { 3 })[0].Equals(3);
    }
}").Replace("TARGET", targetType)));
        }

        [TestMethod]
        public void NullableBoxingPreservesNullWhenAllowedAndSkipsPatchNull()
        {
            AssertProbe(Run(Preamble + @"
[LiteMapper] public static partial class Mapper
{
    public static partial object? Map(int? source);
    public static partial object?[] MapItems(int?[] source);
}
[LiteMapper(IgnoreNullSourceMembers = true)] public static partial class PatchMapper { public static partial void Apply(Source source, Target destination); }
public class Source { public int? Item { get; set; } }
public class Target { public object Item { get; set; } = 7; }
public static class Probe
{
    public static bool Run()
    {
        var target = new Target();
        PatchMapper.Apply(new Source(), target);
        if (!target.Item.Equals(7)) return false;
        PatchMapper.Apply(new Source { Item = 3 }, target);
        var items = Mapper.MapItems(new int?[] { null, 3 });
        return target.Item.Equals(3) && Mapper.Map(null) == null && Mapper.Map(3)!.Equals(3) && items[0] == null && items[1]!.Equals(3);
    }
}"));
        }

        [TestMethod]
        public void ObliviousReferenceTargetsDoNotIntroduceBoxingNullabilityErrors()
        {
            AssertProbe(Run("#nullable disable\n" + Preamble + @"
[LiteMapper] public static partial class MemberMapper { public static partial Target Map(Source source); }
[LiteMapper] public static partial class ElementMapper { public static partial object[] Map(int?[] source); }
public class Source { public int? Item { get; set; } }
public class Target { public object Item { get; set; } }
public static class Probe
{
    public static bool Run() => MemberMapper.Map(new Source()).Item == null && MemberMapper.Map(new Source { Item = 3 }).Item.Equals(3)
        && ElementMapper.Map(new int?[] { null, 3 }) is var items && items[0] == null && items[1].Equals(3);
}
"));
        }

        private const string Preamble = "using System; using Mammoth.LiteMapper; public enum Kind { Zero, One } public struct Value { public int Number; } ";

        [TestMethod]
        [DataRow("bool?[]", "new bool?[] { null, true }", "target.Length == 2 && target[0] == null && target[1] == true")]
        [DataRow("System.Collections.Generic.List<bool?>", "new System.Collections.Generic.List<bool?> { null, true }", "target.Count == 2 && target[0] == null && target[1] == true")]
        [DataRow("System.Collections.Generic.HashSet<bool?>", "new System.Collections.Generic.HashSet<bool?> { null, true }", "target.Count == 2 && target.Contains(null) && target.Contains(true)")]
        [DataRow("System.Collections.Generic.Dictionary<int, bool?>", "new System.Collections.Generic.Dictionary<int, bool?> { [1] = null, [2] = true }", "target.Count == 2 && target[1] == null && target[2] == true")]
        public void CollectionConstructionPreservesNullableElementType(string type, string value, string check)
        {
            AssertProbe(Run(Preamble + "[LiteMapper(NullCollections = NullCollectionStrategy.Empty)] public static partial class Mapper { public static partial " + type + " Map(" + type + "? source); } public static class Probe { public static bool Run() { var target = Mapper.Map(" + value + "); return " + check + " && Mapper.Map(null) != null; } }"));
        }

        private static (GeneratorDriverRunResult Result, Compilation Compilation) Run(string source)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("NullableValueTests", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }

        private static void AssertProbe((GeneratorDriverRunResult Result, Compilation Compilation) result)
        {
            Assert.AreEqual(0, result.Result.Diagnostics.Length, string.Join(Environment.NewLine, result.Result.Diagnostics));
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.AreEqual(true, Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }
    }
}
