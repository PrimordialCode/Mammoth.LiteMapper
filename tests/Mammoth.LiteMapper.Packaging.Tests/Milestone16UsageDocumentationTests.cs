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
            StringAssert.Contains(usage, "LITEMAPPER0001");
            StringAssert.Contains(usage, "LITEMAPPER7005");
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
