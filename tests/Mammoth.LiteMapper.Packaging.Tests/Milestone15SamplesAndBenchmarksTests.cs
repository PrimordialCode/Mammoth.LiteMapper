using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class Milestone15SamplesAndBenchmarksTests
    {
        [TestMethod]
        public void SamplesCompileAndRun()
        {
            RunDotnet("run --project samples\\Mammoth.LiteMapper.Samples.Basic\\Mammoth.LiteMapper.Samples.Basic.csproj --no-restore", Repository.Root);
            RunDotnet("run --project samples\\Mammoth.LiteMapper.Samples.Collections\\Mammoth.LiteMapper.Samples.Collections.csproj --no-restore", Repository.Root);
            RunDotnet("run --project samples\\Mammoth.LiteMapper.Samples.AspNetCore\\Mammoth.LiteMapper.Samples.AspNetCore.csproj --no-restore -- --smoke", Repository.Root);
        }

        [TestMethod]
        public void BenchmarkHarnessRunsDryJobAndRecordsPinnedComparisons()
        {
            var result = RunDotnet("run --project benchmarks\\Mammoth.LiteMapper.Benchmarks\\Mammoth.LiteMapper.Benchmarks.csproj -c Release --no-restore -- --filter *FlatObjectBenchmarks* --job Dry --warmupCount 1 --iterationCount 1", Repository.Root);

            StringAssert.Contains(result.Output, "Manual");
            StringAssert.Contains(result.Output, "LiteMapper");
            StringAssert.Contains(result.Output, "Mapperly");
            StringAssert.Contains(result.Output, "Mapster");
            StringAssert.Contains(result.Output, "AutoMapper");
            Assert.IsFalse(result.Output.Contains("Build Error", StringComparison.OrdinalIgnoreCase), result.Output);
            Assert.IsFalse(result.Output.Contains("Benchmarks with issues", StringComparison.OrdinalIgnoreCase), result.Output);
            Assert.IsFalse(result.Output.Contains("There are not any results runs", StringComparison.OrdinalIgnoreCase), result.Output);
        }

        private static ProcessResult RunDotnet(string arguments, string workingDirectory)
        {
            var result = TestProcess.Run("dotnet", arguments, workingDirectory, TimeSpan.FromSeconds(60));
            Assert.AreEqual(0, result.ExitCode, "dotnet " + arguments + Environment.NewLine + result.Output + Environment.NewLine + result.Error);
            return result;
        }

    }
}
