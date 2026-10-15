using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    internal static class TestProcess
    {
        private const int DiagnosticTailLength = 4096;
        private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Drains both pipes concurrently and bounds process exit and pipe closure together,
        /// including inherited handles that outlive the direct child during build validation.
        /// </summary>
        public static ProcessResult Run(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
        {
            var elapsed = Stopwatch.StartNew();
            using var deadline = new CancellationTokenSource(timeout);
            using var stopReading = new CancellationTokenSource();
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
            var output = new CapturedOutput();
            var error = new CapturedOutput();
            var completion = Task.WhenAll(
                output.ReadAsync(process.StandardOutput, stopReading.Token),
                error.ReadAsync(process.StandardError, stopReading.Token),
                process.WaitForExitAsync(stopReading.Token));
            try
            {
                completion.WaitAsync(deadline.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                string termination;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        termination = "Process-tree termination requested.";
                    }
                    else
                    {
                        termination = "Direct process already exited; descendant termination cannot be confirmed.";
                    }
                }
                catch (InvalidOperationException)
                {
                    termination = "Direct process exited before termination could be requested.";
                }
                catch (Exception exception) when (exception is Win32Exception or AggregateException)
                {
                    termination = "Process-tree termination failed: " + exception.Message;
                }

                string cleanup;
                try
                {
                    completion.WaitAsync(CleanupTimeout).GetAwaiter().GetResult();
                    cleanup = "Cleanup completed: direct process exited and redirected pipes closed.";
                }
                catch (TimeoutException)
                {
                    cleanup = "Cleanup incomplete after " + CleanupTimeout + ": process exit or redirected pipe closure was not confirmed.";
                }
                catch (Exception exception)
                {
                    cleanup = "Cleanup incomplete: " + exception.GetType().Name + ": " + exception.Message;
                }
                finally
                {
                    stopReading.Cancel();
                    // Reads canceled after the cleanup bound may finish after Run returns.
                    _ = completion.ContinueWith(task => { _ = task.Exception; },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }

                throw new TimeoutException(fileName + " " + arguments + " timed out after " + timeout +
                    " (elapsed " + elapsed.Elapsed + "). " + termination + Environment.NewLine + cleanup +
                    Environment.NewLine + "Standard output (last " + DiagnosticTailLength + " characters):" +
                    Environment.NewLine + output.GetText(tailOnly: true) +
                    Environment.NewLine + "Standard error (last " + DiagnosticTailLength + " characters):" +
                    Environment.NewLine + error.GetText(tailOnly: true));
            }

            return new ProcessResult(process.ExitCode, output.GetText(), error.GetText());
        }

        private sealed class CapturedOutput
        {
            private readonly StringBuilder text = new StringBuilder();

            public async Task ReadAsync(StreamReader reader, CancellationToken cancellationToken)
            {
                var buffer = new char[4096];
                try
                {
                    int count;
                    while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        lock (text)
                        {
                            text.Append(buffer, 0, count);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
                catch (IOException) when (cancellationToken.IsCancellationRequested) { }
            }

            public string GetText(bool tailOnly = false)
            {
                lock (text)
                {
                    var start = tailOnly ? Math.Max(0, text.Length - DiagnosticTailLength) : 0;
                    return text.ToString(start, text.Length - start);
                }
            }
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
