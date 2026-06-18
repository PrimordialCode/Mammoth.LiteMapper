using System;
using System.Diagnostics;
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
            return new ProcessResult(output, error);
        }

        private sealed class ProcessResult
        {
            public ProcessResult(string output, string error)
            {
                Output = output;
                Error = error;
            }

            public string Output { get; }

            public string Error { get; }
        }
    }
}
