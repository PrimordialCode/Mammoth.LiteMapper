using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Mammoth.LiteMapper.Generator;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class OfficialGeneratorTests
    {
        [TestMethod]
        public async Task OfficialHarnessValidatesInvalidTargetPathDiagnostic()
        {
            var test = CreateTest(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    [{|#0:MapProperty(Source = ""Value"", Target = ""Child.Value"")|}]
    public static partial Target {|#1:Map|}(Source source);
}
public sealed class Source { public int Value { get; set; } }
public sealed class Target { public int Value { get; set; } }
");
            test.ExpectedDiagnostics.Add(new DiagnosticResult("LITEMAPPER1007", DiagnosticSeverity.Error)
                .WithLocation(0).WithArguments("Child.Value"));
            test.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError("CS8795")
                .WithLocation(1).WithArguments("Mapper.Map(Source)"));
            await test.RunAsync();
        }

        [TestMethod]
        public async Task OfficialHarnessValidatesExactGeneratedSourceAndCompilation()
        {
            var test = CreateTest(@"
using Mammoth.LiteMapper;
[LiteMapper]
public static partial class Mapper
{
    public static partial Target ToTarget(Source source);
}
public sealed class Source { public int Age { get; set; } public string Name { get; set; } = string.Empty; }
public sealed class Target { public int Age { get; set; } public string Name { get; set; } = string.Empty; }
");
            var expected = File.ReadAllText(Repository.Path("tests/Mammoth.LiteMapper.Generator.Tests/Snapshots/Milestone4FlatMapping.Mapper.g.cs"))
                .ReplaceLineEndings(Environment.NewLine);
            test.TestState.GeneratedSources.Add((typeof(LiteMapperGenerator), "Mapper.FA74CAA3.g.cs", expected));
            await test.RunAsync();
        }

        private static CSharpSourceGeneratorTest<LiteMapperGenerator, MstestVerifier> CreateTest(string source)
        {
            var test = new CSharpSourceGeneratorTest<LiteMapperGenerator, MstestVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
                CompilerDiagnostics = CompilerDiagnostics.Errors,
            };
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Where(static p => p.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
                .Select(static p => MetadataReference.CreateFromFile(p));
            test.TestState.AdditionalReferences.AddRange(references);
            test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location));
            test.SolutionTransforms.Add((solution, projectId) => solution
                .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11))
                .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)));
            return test;
        }

        public sealed class MstestVerifier : IVerifier
        {
            private readonly string _context;

            public MstestVerifier() : this(string.Empty) { }

            private MstestVerifier(string context) => _context = context;

            public void Empty<T>(string collectionName, IEnumerable<T> collection) => Assert.IsFalse(collection.Any(), _context + collectionName);

            public void NotEmpty<T>(string collectionName, IEnumerable<T> collection) => Assert.IsTrue(collection.Any(), _context + collectionName);

            public void Equal<T>(T expected, T actual, string? message = null) => Assert.AreEqual(expected, actual, _context + message);

            public void True(bool assert, string? message = null) => Assert.IsTrue(assert, _context + message);

            public void False(bool assert, string? message = null) => Assert.IsFalse(assert, _context + message);

            [DoesNotReturn]
            public void Fail(string? message = null) => Assert.Fail(_context + message);

            public void LanguageIsSupported(string language) => Assert.AreEqual(LanguageNames.CSharp, language, _context);

            public void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? equalityComparer = null, string? message = null)
                => Assert.IsTrue(expected.SequenceEqual(actual, equalityComparer), _context + message);

            public IVerifier PushContext(string context) => new MstestVerifier(_context + context + Environment.NewLine);
        }
    }
}
