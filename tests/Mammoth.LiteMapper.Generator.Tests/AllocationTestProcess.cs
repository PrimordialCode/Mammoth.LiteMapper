using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    internal static class AllocationTestProcess
    {
        public static bool IsChild => AppContext.GetData("Mammoth.LiteMapper.AllocationTestHost") is true;

        public static bool Run(Type testClass, string methodName, params bool[] arguments)
        {
            if (IsChild)
            {
                Assert.AreEqual(false, GC.GetConfigurationVariables()["ConcurrentGC"],
                    "Allocation assertions must execute with background GC disabled in the child.");
                return false;
            }

            var assemblyPath = testClass.Assembly.Location;
            var directory = Path.GetDirectoryName(assemblyPath)!;
            var hostPath = Path.Combine(directory, "AllocationTestHost", "Mammoth.LiteMapper.AllocationTestHost.dll");
            Assert.IsTrue(File.Exists(hostPath), "Build the allocation-test host before running these tests: " + hostPath);

            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add(hostPath);
            process.StartInfo.ArgumentList.Add(assemblyPath);
            process.StartInfo.ArgumentList.Add(testClass.FullName!);
            process.StartInfo.ArgumentList.Add(methodName);
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument ? "true" : "false");

            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token))
                    .WaitAsync(deadline.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The child can exit between the status check and termination.
                }
                Assert.Fail("Allocation-test child timed out: " + testClass.FullName + "." + methodName);
            }

            var standardOutput = output.GetAwaiter().GetResult();
            var standardError = error.GetAwaiter().GetResult();
            Console.Write(standardOutput);
            Console.Error.Write(standardError);
            Assert.AreEqual(0, process.ExitCode,
                "Allocation-test child failed: " + testClass.FullName + "." + methodName +
                Environment.NewLine + standardOutput + standardError);
            return true;
        }
    }
}
