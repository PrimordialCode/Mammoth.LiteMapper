using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class InternalFailureIsolationTests
    {
        private const string SensitiveDetail = "Injected planning failure at C:/private/customer/model.cs";

        [TestMethod]
        public void PlanningFailureIsSanitizedAndUnrelatedMapperStillGenerates()
        {
            var result = RunGenerator(false);
            var diagnostic = result.Diagnostics.Single(d => d.Id == "LITEMAPPER9001");
            Assert.AreEqual(1, result.Diagnostics.Length, "A single sanitized mapper diagnostic must replace the driver exception.");
            StringAssert.Contains(diagnostic.GetMessage(), "BrokenMapper");
            Assert.IsFalse(diagnostic.ToString().Contains("private", StringComparison.Ordinal));
            Assert.AreEqual("BrokenMapper", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
            Assert.IsNull(result.Results.Single().Exception);
            Assert.AreEqual(1, result.GeneratedTrees.Length);
            StringAssert.Contains(result.GeneratedTrees.Single().GetText().ToString(), "class HealthyMapper");
        }

        [TestMethod]
        public void DevelopmentOptionExposesActualPlanningException()
        {
            var result = RunGenerator(true);
            var exception = result.Results.Single().Exception;
            Assert.IsNotNull(exception, "The development option must expose a real failure, not just diagnostic metadata.");
            StringAssert.Contains(exception.ToString(), SensitiveDetail);
            Assert.IsFalse(result.Diagnostics.Any(d => d.Id == "LITEMAPPER9001"));
        }

        private static GeneratorDriverRunResult RunGenerator(bool exposeExceptions)
        {
            var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
                .Split(Path.PathSeparator)
                .Select(static p => MetadataReference.CreateFromFile(p))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(LiteMapperAttribute).Assembly.Location) });
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var compilation = CSharpCompilation.Create("Tests", new[] { CSharpSyntaxTree.ParseText(@"
using Mammoth.LiteMapper;
[LiteMapper] public static partial class BrokenMapper { public static partial Target Map(Source source); }
[LiteMapper] public static partial class HealthyMapper { public static partial Target Map(Source source); }
public sealed class Source { public int Id { get; set; } }
public sealed class Target { public int Id { get; set; } }
", parseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new FaultingGenerator().AsSourceGenerator() },
                parseOptions: parseOptions, optionsProvider: new Options(exposeExceptions));
            return driver.RunGenerators(compilation).GetRunResult();
        }

        private sealed class FaultingGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context)
            {
                LiteMapperGenerator.InitializeCore(context, static symbol =>
                {
                    if (symbol.Name == "BrokenMapper")
                    {
                        throw new InvalidOperationException(SensitiveDetail);
                    }
                });
            }
        }

        private sealed class Options : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions values;
            public Options(bool exposeExceptions) { values = new Values(exposeExceptions); }
            public override AnalyzerConfigOptions GlobalOptions => values;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => values;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => values;
        }

        private sealed class Values : AnalyzerConfigOptions
        {
            private readonly bool exposeExceptions;
            public Values(bool exposeExceptions) { this.exposeExceptions = exposeExceptions; }
            public override bool TryGetValue(string key, out string value)
            {
                value = exposeExceptions ? "true" : "false";
                return key == "build_property.LiteMapper_TreatInternalGeneratorErrorsAsExceptions";
            }
        }
    }
}
