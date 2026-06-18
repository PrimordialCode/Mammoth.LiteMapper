using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class Milestone16UsageDocumentationTests
    {
        [TestMethod]
        public void UsageDocumentationIsAssembledFromCompilingSamples()
        {
            RunDotnet("run --project samples\\Mammoth.LiteMapper.Samples.Basic\\Mammoth.LiteMapper.Samples.Basic.csproj --no-restore", Repository.Root);
            RunDotnet("run --project samples\\Mammoth.LiteMapper.Samples.Collections\\Mammoth.LiteMapper.Samples.Collections.csproj --no-restore", Repository.Root);
            RunDotnet("run --project samples\\Mammoth.LiteMapper.Samples.AspNetCore\\Mammoth.LiteMapper.Samples.AspNetCore.csproj --no-restore -- --smoke", Repository.Root);

            var usagePath = Repository.Path("docs/USAGE.md");
            Assert.IsTrue(File.Exists(usagePath), "Usage documentation must exist at docs/USAGE.md.");

            var usage = File.ReadAllText(usagePath);
            StringAssert.Contains(usage, "samples/Mammoth.LiteMapper.Samples.Basic/Program.cs");
            StringAssert.Contains(usage, "samples/Mammoth.LiteMapper.Samples.Collections/Program.cs");
            StringAssert.Contains(usage, "samples/Mammoth.LiteMapper.Samples.AspNetCore/Program.cs");
            StringAssert.Contains(usage, "[LiteMapper]");
            StringAssert.Contains(usage, "public static partial StaticTarget Map(StaticSource source);");
            StringAssert.Contains(usage, "public partial InstanceTarget Map(InstanceSource source);");
            StringAssert.Contains(usage, "ReferenceHandling.ThrowOnCycle");
            StringAssert.Contains(usage, "public static partial OrderTarget Map(OrderSource source);");
            StringAssert.Contains(usage, "public static partial UserDto Map(User source);");
            StringAssert.Contains(usage, "## Installation");
            StringAssert.Contains(usage, "PackageReference Include=\"Mammoth.LiteMapper\"");
            StringAssert.Contains(usage, "## Mapper declarations");
            StringAssert.Contains(usage, "nested mapper classes");
            StringAssert.Contains(usage, "Instance mappers may use fields");
            StringAssert.Contains(usage, "Ordinary inherited handwritten methods");
            StringAssert.Contains(usage, "## Public API quick reference");
            StringAssert.Contains(usage, "LiteMapperAttribute");
            StringAssert.Contains(usage, "MappingOptionsAttribute");
            StringAssert.Contains(usage, "LiteMapperCycleException");
            StringAssert.Contains(usage, "## Configuration scope and defaults");
            StringAssert.Contains(usage, "[assembly: LiteMapperDefaults");
            StringAssert.Contains(usage, "[MappingOptions");
            StringAssert.Contains(usage, "OptionState.Enabled");
            StringAssert.Contains(usage, "Library defaults");
            StringAssert.Contains(usage, "## Member matching and unmapped members");
            StringAssert.Contains(usage, "NameMatching.ExactThenIgnoreCase");
            StringAssert.Contains(usage, "UnmappedMemberPolicy.Error");
            StringAssert.Contains(usage, "Hidden members");
            StringAssert.Contains(usage, "Source paths");
            StringAssert.Contains(usage, "Target paths");
            StringAssert.Contains(usage, "Source methods");
            StringAssert.Contains(usage, "Duplicate configuration");
            StringAssert.Contains(usage, "## Mapping method signatures");
            StringAssert.Contains(usage, "this Customer source");
            StringAssert.Contains(usage, "ref CounterDto destination");
            StringAssert.Contains(usage, "## Construction, records, init, required, and value types");
            StringAssert.Contains(usage, "[MappingConstructor]");
            StringAssert.Contains(usage, "[UseTargetDefault");
            StringAssert.Contains(usage, "## Explicit member configuration");
            StringAssert.Contains(usage, "[MapProperty");
            StringAssert.Contains(usage, "[IgnoreTarget");
            StringAssert.Contains(usage, "[IgnoreSource");
            StringAssert.Contains(usage, "## Custom converters and extension patterns");
            StringAssert.Contains(usage, "[MappingConverter]");
            StringAssert.Contains(usage, "Map{TargetMember}");
            StringAssert.Contains(usage, "[DefaultMapping]");
            StringAssert.Contains(usage, "[UseMapper");
            StringAssert.Contains(usage, "post-processing wrapper");
            StringAssert.Contains(usage, "No after-map hook");
            StringAssert.Contains(usage, "## Built-in, explicit, and numeric conversions");
            StringAssert.Contains(usage, "NumericConversion.Checked");
            StringAssert.Contains(usage, "AllowExplicitOperators = true");
            StringAssert.Contains(usage, "## Nullability and null collections");
            StringAssert.Contains(usage, "NullableMismatchPolicy.Throw");
            StringAssert.Contains(usage, "NullCollectionStrategy.Empty");
            StringAssert.Contains(usage, "Root source null");
            StringAssert.Contains(usage, "## Nested object mapping");
            StringAssert.Contains(usage, "Closed generic");
            StringAssert.Contains(usage, "existing-target nested");
            StringAssert.Contains(usage, "## Existing-target and patch mapping");
            StringAssert.Contains(usage, "IgnoreNullSourceMembers");
            StringAssert.Contains(usage, "## Enum mapping");
            StringAssert.Contains(usage, "EnumMappingStrategy.ByName");
            StringAssert.Contains(usage, "UnmatchedEnumValuePolicy.Throw");
            StringAssert.Contains(usage, "EnumNumericConversion.Checked");
            StringAssert.Contains(usage, "## Recursive mappings and cycle detection");
            StringAssert.Contains(usage, "SourcePath");
            StringAssert.Contains(usage, "DestinationPath");
            StringAssert.Contains(usage, "## Supported collections");
            StringAssert.Contains(usage, "Public collection mappings");
            StringAssert.Contains(usage, "Interface target defaults");
            StringAssert.Contains(usage, "Ordering");
            StringAssert.Contains(usage, "mutable copy");
            StringAssert.Contains(usage, "rectangular multidimensional arrays");
            StringAssert.Contains(usage, "## Unsupported and special types");
            StringAssert.Contains(usage, "ValueTuple");
            StringAssert.Contains(usage, "Span<T>");
            StringAssert.Contains(usage, "## Generated code, trimming, and Native AOT");
            StringAssert.Contains(usage, "zero runtime reflection");
            StringAssert.Contains(usage, "## Package and platform support");
            StringAssert.Contains(usage, "netstandard2.0");
            StringAssert.Contains(usage, "Roslyn 4.8.0");
            StringAssert.Contains(usage, "## Analyzer options");
            StringAssert.Contains(usage, "LiteMapper_EmitDebugMetadata");
            StringAssert.Contains(usage, "LITEMAPPER0001");
            StringAssert.Contains(usage, "LITEMAPPER7005");
            StringAssert.Contains(usage, "configurable severity");
            StringAssert.Contains(usage, "non-configurable");
            StringAssert.Contains(usage, "Deferred features");
            StringAssert.Contains(usage, "EF Core expression projections");
            Assert.IsFalse(usage.Contains("IMapper", StringComparison.Ordinal), "Usage docs must not document a runtime IMapper API.");
            Assert.IsFalse(usage.Contains("Map<TDestination>(object)", StringComparison.Ordinal), "Usage docs must not document runtime object dispatch.");
        }

        private static void RunDotnet(string arguments, string workingDirectory)
        {
            using var process = new Process();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("dotnet " + arguments + " timed out.");
            }

            Assert.AreEqual(0, process.ExitCode, "dotnet " + arguments + Environment.NewLine + output + Environment.NewLine + error);
        }
    }
}
