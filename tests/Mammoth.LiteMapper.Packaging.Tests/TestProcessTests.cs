using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class TestProcessTests
    {
        [TestMethod]
        public void ConcurrentDrainingHandlesStderrPressureBeforeStdoutCloses()
        {
            var result = RunFixture("pressure", TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, result.ExitCode, "Reading stdout first must not deadlock a child blocked on stderr.");
            Assert.AreEqual("complete", result.Output);
            Assert.AreEqual(1024 * 1024, result.Error.Length);
        }

        [TestMethod]
        [DataRow("wait")]
        [DataRow("hold")]
        public void DeadlineIncludesProcessExitAndInheritedPipeHandles(string mode)
        {
            var elapsed = Stopwatch.StartNew();
            Assert.ThrowsExactly<TimeoutException>(() => RunFixture(mode, TimeSpan.FromMilliseconds(500)));
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(3), "The deadline must bound pipe draining as well as waiting for the direct child.");
        }

        [TestMethod]
        public void DotnetChildrenDisableReusableBuildServers()
        {
            var result = RunFixture("environment", TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual("1:0", result.Output);
        }

        private static ProcessResult RunFixture(string mode, TimeSpan timeout)
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Mammoth.LiteMapper.ProcessFixture.dll");
            return TestProcess.Run("dotnet", "\"" + fixture + "\" " + mode, AppContext.BaseDirectory, timeout);
        }
    }
}
