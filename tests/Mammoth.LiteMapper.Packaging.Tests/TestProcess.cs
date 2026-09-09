using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    internal static class TestProcess
    {
        /// <summary>
        /// Drains both pipes concurrently and bounds process exit and pipe closure together,
        /// including inherited handles that outlive the direct child during build validation.
        /// </summary>
        public static ProcessResult Run(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            process.StartInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
            process.StartInfo.Environment["UseSharedCompilation"] = "false";
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
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The child may exit between the check and termination.
                }

                throw new TimeoutException(fileName + " " + arguments + " timed out after " + timeout + ".");
            }

            return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
    }

    internal sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output;
            Error = error;
        }

        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
    }
}
