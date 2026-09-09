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
    public class InheritedMappingTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void InterfaceDiamondExposesInheritedPropertyOnce(bool path)
        {
            var result = Run(@"using Mammoth.LiteMapper;
public interface IBase { int Value { get; } }
public interface ILeft : IBase { }
public interface IRight : IBase { }
public interface ISource : ILeft, IRight { }
public class Data : ISource { public int Value => 3; }
public class Source { public ISource Data { get; set; } = new Data(); }
public class Target { public int Value { get; set; } }
[LiteMapper] public static partial class Mapper {
" + (path ? "[MapProperty(Source = \"Data.Value\", Target = \"Value\")] public static partial Target Map(Source source);" : "public static partial Target Map(ISource source);") + @"
}
public static class Probe { public static int Run() => Mapper.Map(new " + (path ? "Source" : "Data") + @"()).Value; }");
            Assert.AreEqual(3, Execute(result));
        }

        [TestMethod]
        public void ConfiguredPathFindsInheritedClassProperty()
        {
            Assert.AreEqual(3, Execute(Run(@"using Mammoth.LiteMapper;
public class Base { public int Value => 3; }
public class Data : Base { }
public class Source { public Data Data { get; set; } = new Data(); }
public class Target { public int Value { get; set; } }
[LiteMapper] public static partial class Mapper {
    [MapProperty(Source = ""Data.Value"", Target = ""Value"")]
    public static partial Target Map(Source source);
}
public static class Probe { public static int Run() => Mapper.Map(new Source()).Value; }")));
        }

        [TestMethod]
        [DataRow("IgnoreTarget")]
        [DataRow("UseTargetDefault")]
        public void InheritedMembersRemainValidConfigurationTargets(string attribute)
        {
            Assert.AreEqual(9, Execute(Run(@"using Mammoth.LiteMapper;
public interface IBase { int Value { get; } }
public interface ISource : IBase { }
public class Source : ISource { public int Value => 3; }
public class TargetBase { public int Value { get; set; } = 9; }
public class Target : TargetBase { }
[LiteMapper(UnmappedSourceMembers = UnmappedMemberPolicy.Error)] public static partial class Mapper {
    [IgnoreSource(""Value""), " + attribute + @"(""Value"")]
    public static partial Target Map(ISource source);
}
public static class Probe { public static int Run() => Mapper.Map(new Source()).Value; }")));
        }

        [TestMethod]
        [DataRow("protected", false)]
        [DataRow("public", false)]
        [DataRow("protected", true)]
        public void ExplicitSelectionFindsAccessibleInheritedConverter(string accessibility, bool staticMethod)
        {
            var modifier = staticMethod ? "static " : string.Empty;
            Assert.AreEqual(13, Execute(Run(@"using Mammoth.LiteMapper;
public class Base { " + accessibility + " " + modifier + @"int Parse(string value) => int.Parse(value) + 1; }
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public int Value { get; set; } }
[LiteMapper] public partial class Mapper : Base {
    [MapProperty(Source = ""Value"", Target = ""Value"", Use = nameof(Parse))]
    public " + modifier + @"partial Target Map(Source source);
}
public static class Probe { public static int Run() => " + (staticMethod ? "Mapper" : "new Mapper()") + @".Map(new Source()).Value; }")));
        }

        [TestMethod]
        [DataRow("private", false)]
        [DataRow("protected", true)]
        public void InheritedConverterMustBeAccessibleFromTheActualMapping(string accessibility, bool staticMapping)
        {
            var result = Run(@"using Mammoth.LiteMapper;
public class Base { " + accessibility + @" int Parse(string value) => int.Parse(value); }
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public int Value { get; set; } }
[LiteMapper] public partial class Mapper : Base {
    [MapProperty(Source = ""Value"", Target = ""Value"", Use = ""Parse"")]
    public " + (staticMapping ? "static " : string.Empty) + @"partial Target Map(Source source);
}");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER2009"));
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        [DataRow("int")]
        [DataRow("int?")]
        public void ExplicitInheritedOverloadsFollowNormalMemberLookup(string baseResult)
        {
            Assert.AreEqual(true, Execute(Run(@"using Mammoth.LiteMapper;
public class Base { protected " + baseResult + @" Parse(string value) => 13; }
public class Source { public string Value { get; set; } = ""12""; }
public class Target { public int Value { get; set; } }
[LiteMapper] public partial class Mapper : Base {
    protected int Parse(object value) => 14;
    public int Expected(string value) => Parse(value);
    [MapProperty(Source = ""Value"", Target = ""Value"", Use = nameof(Parse))]
    public partial Target Map(Source source);
}
public static class Probe { public static bool Run() { var mapper = new Mapper(); return mapper.Map(new Source()).Value == mapper.Expected(""12""); } }")));
        }

        [TestMethod]
        public void InheritedHelpersDoNotCreateMappingProfileInheritance()
        {
            Assert.AreEqual(3, Execute(Run(@"using Mammoth.LiteMapper;
public class Base {
    [MappingConverter] protected int Convert(int value) => value + 10;
    protected int MapValue(int value) => value + 20;
}
public class Source { public int Value { get; set; } = 3; }
public class Target { public int Value { get; set; } }
[LiteMapper] public partial class Mapper : Base { public partial Target Map(Source source); }
public static class Probe { public static int Run() => new Mapper().Map(new Source()).Value; }")));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UnrelatedInterfacePropertiesRemainAmbiguous(bool path)
        {
            var result = Run(@"using Mammoth.LiteMapper;
public interface ILeft { int Value { get; } }
public interface IRight { int Value { get; } }
public interface ISource : ILeft, IRight { }
public class Source { public ISource Data { get; set; } = null!; }
public class Target { public int Value { get; set; } }
[LiteMapper] public static partial class Mapper {
" + (path ? "[MapProperty(Source = \"Data.Value\", Target = \"Value\")] public static partial Target Map(Source source);" : "public static partial Target Map(ISource source);") + @"
}");
            Assert.IsTrue(result.RunResult.Diagnostics.Any(d => d.Id == "LITEMAPPER1004"), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
            Assert.AreEqual(0, result.RunResult.GeneratedTrees.Length);
        }

        [TestMethod]
        public void MostDerivedInterfacePropertyWinsWithHidingWarning()
        {
            var result = Run(@"using Mammoth.LiteMapper;
public interface IBase { int Value { get; } }
public interface ISource : IBase { new int Value { get; } }
public class Source : ISource { public int Value => 3; int IBase.Value => 7; }
public class Target { public int Value { get; set; } }
[LiteMapper] public static partial class Mapper { public static partial Target Map(ISource source); }
public static class Probe { public static int Run() => Mapper.Map(new Source()).Value; }");
            Assert.AreEqual("LITEMAPPER1005", result.RunResult.Diagnostics.Single().Id);
            Assert.AreEqual(3, Execute(result, "LITEMAPPER1005"));
        }

        private static object? Execute((GeneratorDriverRunResult RunResult, Compilation Compilation) result, string? allowedDiagnostic = null)
        {
            Assert.IsFalse(result.RunResult.Diagnostics.Any(d => d.Id != allowedDiagnostic), string.Join(Environment.NewLine, result.RunResult.Diagnostics));
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
            var compilation = CSharpCompilation.Create("InheritedMappingTests", new[] { CSharpSyntaxTree.ParseText(source, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LiteMapperGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (driver.GetRunResult(), updated);
        }
    }
}
