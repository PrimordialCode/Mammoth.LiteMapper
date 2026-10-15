using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Mammoth.LiteMapper.ProcessFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class TestProcessTests
    {
        [TestMethod]
        [DataRow("fixture")]
        [DataRow("fixture with spaces")]
        [DataRow("fixture ) with (parentheses)")]
        public void LinuxFixtureIdentityUsesKernelStartTicksAfterTheCompleteCommandName(string commandName)
        {
            var stat = "123 (" + commandName + ") S 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 987654 23";
            Assert.AreEqual(987654L, FixtureProcessIdentity.ParseLinuxStartStamp(stat), "Ownership must use field 22 directly, independent of wall-clock boot-time estimates.");
        }

        [TestMethod]
        [DataRow("123 missing-command S 4 5")]
        [DataRow("123 (fixture) S 4 5")]
        [DataRow("123 (fixture) S 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 invalid 23")]
        [DataRow("123 (fixture) S 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 -1 23")]
        public void MalformedLinuxFixtureIdentityFailsLoudly(string stat)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => FixtureProcessIdentity.ParseLinuxStartStamp(stat));
        }

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
        public void TimeoutReportsBoundedOutputTailsAndTerminatesTheDirectProcess()
        {
            var identityFile = CreateIdentityFilePath();
            var timeout = TimeSpan.FromSeconds(2);
            var elapsed = Stopwatch.StartNew();
            try
            {
                var exception = Assert.ThrowsExactly<TimeoutException>(() => RunFixture("timeout-output", timeout, identityFile));

                StringAssert.Contains(exception.Message, timeout.ToString());
                Assert.IsTrue(exception.Message.Contains("elapsed", StringComparison.OrdinalIgnoreCase));
                StringAssert.Contains(exception.Message, "Cleanup completed");
                StringAssert.Contains(exception.Message, "stdout-tail-marker");
                StringAssert.Contains(exception.Message, "stderr-tail-marker");
                Assert.IsFalse(exception.Message.Contains("stdout-head-marker", StringComparison.Ordinal));
                Assert.IsFalse(exception.Message.Contains("stderr-head-marker", StringComparison.Ordinal));
                Assert.IsTrue(exception.Message.Length <= 9000, "Diagnostic output must retain bounded tails instead of embedding complete redirected streams.");
                Assert.IsTrue(File.Exists(identityFile), "Fixture startup must be distinguished from an empty launch failure.");
                Assert.IsFalse(IsOwnedFixtureProcessAlive(identityFile), "The timed-out direct process must be gone before Run returns.");
                Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Timeout cleanup must remain bounded.");
            }
            finally
            {
                CleanupOwnedFixtureProcess(identityFile);
            }
        }

        [TestMethod]
        public void TimeoutTerminatesTheProcessTreeWhileTheDirectProcessIsAlive()
        {
            var parentIdentityFile = CreateIdentityFilePath();
            var childIdentityFile = CreateIdentityFilePath();
            var timeout = TimeSpan.FromSeconds(4);
            var elapsed = Stopwatch.StartNew();
            try
            {
                var exception = Assert.ThrowsExactly<TimeoutException>(() => RunFixture("tree-timeout", timeout, parentIdentityFile, childIdentityFile));

                StringAssert.Contains(exception.Message, "Cleanup completed");
                StringAssert.Contains(exception.Message, "tree-parent-stdout-ready");
                StringAssert.Contains(exception.Message, "tree-parent-stderr-ready");
                StringAssert.Contains(exception.Message, "tree-child-stdout-ready");
                StringAssert.Contains(exception.Message, "tree-child-stderr-ready");
                Assert.IsTrue(File.Exists(parentIdentityFile), "The direct fixture process must report that it started.");
                Assert.IsTrue(File.Exists(childIdentityFile), "The descendant fixture process must report that it started.");
                Assert.IsFalse(IsOwnedFixtureProcessAlive(parentIdentityFile), "The direct fixture process must be terminated before Run returns.");
                Assert.IsFalse(IsOwnedFixtureProcessAlive(childIdentityFile), "The direct process tree must be terminated before Run returns.");
                Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(6), "Process-tree cleanup must remain bounded.");
            }
            finally
            {
                CleanupOwnedFixtureProcess(childIdentityFile);
                CleanupOwnedFixtureProcess(parentIdentityFile);
            }
        }

        [TestMethod]
        public void TimeoutAfterDirectChildExitReportsIncompleteCleanupAndReturnsBoundedly()
        {
            var childIdentityFile = CreateIdentityFilePath();
            var timeout = TimeSpan.FromSeconds(4);
            var elapsed = Stopwatch.StartNew();
            try
            {
                var exception = Assert.ThrowsExactly<TimeoutException>(() => RunFixture("orphan-pipe", timeout, childIdentityFile));

                StringAssert.Contains(exception.Message, timeout.ToString());
                Assert.IsTrue(exception.Message.Contains("elapsed", StringComparison.OrdinalIgnoreCase));
                StringAssert.Contains(exception.Message, "Cleanup incomplete");
                StringAssert.Contains(exception.Message, "Direct process already exited");
                StringAssert.Contains(exception.Message, "orphan-parent-stdout-exited");
                StringAssert.Contains(exception.Message, "orphan-parent-stderr-exited");
                StringAssert.Contains(exception.Message, "orphan-child-stdout-ready");
                StringAssert.Contains(exception.Message, "orphan-child-stderr-ready");
                Assert.IsTrue(File.Exists(childIdentityFile), "The pipe-holding descendant must report that it started.");
                Assert.IsTrue(IsOwnedFixtureProcessAlive(childIdentityFile), "An exited direct parent leaves no portable process-tree handle for terminating its orphaned descendant.");
                Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(6), "Orphaned inherited pipes must not make Run wait for the fixture watchdog.");
            }
            finally
            {
                CleanupOwnedFixtureProcess(childIdentityFile);
            }
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
            return RunFixture(mode, timeout, Array.Empty<string>());
        }

        private static ProcessResult RunFixture(string mode, TimeSpan timeout, params string[] arguments)
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Mammoth.LiteMapper.ProcessFixture.dll");
            var command = "\"" + fixture + "\" " + mode;
            foreach (var argument in arguments)
            {
                command += " \"" + argument.Replace("\"", "\\\"") + "\"";
            }

            return TestProcess.Run("dotnet", command, AppContext.BaseDirectory, timeout);
        }

        private static string CreateIdentityFilePath()
        {
            return Path.Combine(Path.GetTempPath(), "litemapper-process-" + Guid.NewGuid().ToString("N") + ".pid");
        }

        private static bool IsOwnedFixtureProcessAlive(string identityFile)
        {
            using var process = OpenOwnedFixtureProcess(identityFile);
            return process != null && !process.HasExited;
        }

        private static void CleanupOwnedFixtureProcess(string identityFile)
        {
            using var process = OpenOwnedFixtureProcess(identityFile);
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                Assert.IsTrue(process.WaitForExit(3000), "The owned fixture process did not exit during test cleanup.");
            }

            if (File.Exists(identityFile))
            {
                File.Delete(identityFile);
            }
        }

        private static Process? OpenOwnedFixtureProcess(string identityFile)
        {
            if (!File.Exists(identityFile))
            {
                return null;
            }

            var identity = File.ReadAllText(identityFile).Split('|');
            if (identity.Length != 2 ||
                !int.TryParse(identity[0], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
                !long.TryParse(identity[1], NumberStyles.None, CultureInfo.InvariantCulture, out var startStamp))
            {
                throw new AssertFailedException("Fixture process identity was malformed.");
            }

            Process? process = null;
            try
            {
                process = Process.GetProcessById(processId);
                if (FixtureProcessIdentity.GetStartStamp(process) != startStamp)
                {
                    process.Dispose();
                    return null;
                }

                return process;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
                exception is FileNotFoundException || exception is DirectoryNotFoundException)
            {
                process?.Dispose();
                return null;
            }
            catch
            {
                process?.Dispose();
                throw;
            }
        }
    }
}
