using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class WorkflowNativeFailureTests
    {
        public static IEnumerable<object[]> NativeFailures()
        {
            foreach (var trimmedExe in new[] { false, true })
                for (var position = 0; position <= 8; position++)
                    yield return new object[] { "pack-and-validate", "Validate final package consumers", position, 8, trimmedExe };
            foreach (var step in new[] { "Pack", "Publish exact packages" })
                for (var position = 0; position <= 3; position++)
                    yield return new object[] { step == "Pack" ? "pack-and-validate" : "publish", step, position, 3, true };
            foreach (var job in new[] { "build-and-test", "pack-and-validate" })
                for (var position = 0; position <= 1; position++)
                    yield return new object[] { job, "Determine version", position, 1, true };
            foreach (var step in new[] { "Validate solution paths", "Test (windows)" })
                for (var position = 0; position <= 2; position++)
                    yield return new object[] { "build-and-test", step, position, 2, true };
        }

        [TestMethod]
        [DynamicData(nameof(NativeFailures))]
        public void NativeFailureStopsTheActualWorkflowStepBeforePromotion(
            string job, string step, int failAt, int expectedCalls, bool trimmedExe)
        {
            var source = File.ReadAllText(Repository.Path(".github/workflows/ci.yml"));
            var jobMatch = Regex.Match(source, @"(?ms)^  " + Regex.Escape(job) + @":\r?\n(?<body>.*?)(?=^  [a-z][a-z0-9-]*:|\z)");
            Assert.IsTrue(jobMatch.Success, "Workflow job missing: " + job);
            var stepMatch = Regex.Match(jobMatch.Groups["body"].Value,
                @"(?ms)^      - name: " + Regex.Escape(step) + @"\r?\n(?:(?!^      - name:).)*?^        run: \|\r?\n(?<script>(?:(?:^          [^\r\n]*|^[ \t]*)\r?\n)+)");
            Assert.IsTrue(stepMatch.Success, "Workflow script missing: " + step);
            var script = Regex.Replace(stepMatch.Groups["script"].Value, @"(?m)^          ", "");
            var root = Path.Combine(Path.GetTempPath(), "litemapper-native-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var feed = Path.Combine(root, "packages");
                Directory.CreateDirectory(feed);
                foreach (var id in new[] { "Mammoth.LiteMapper.Abstractions", "Mammoth.LiteMapper.Generator", "Mammoth.LiteMapper" })
                    foreach (var extension in new[] { "nupkg", "snupkg" })
                        File.WriteAllText(Path.Combine(feed, id + ".1.2.3." + extension), "mock artifact");
                // Pre-existing outputs ensure artifact checks cannot accidentally hide a
                // missed native failure. Each failed validation must stop on its own.
                var consumer = Path.Combine(root, "litemapper-package-consumer-1.2.3");
                var mock = CopyFixture(Path.Combine(root, "mock"), "native-mock" + (OperatingSystem.IsWindows() ? ".exe" : ""));
                CopyFixture(Path.Combine(consumer, "aot"), "Consumer.exe");
                Directory.CreateDirectory(Path.Combine(consumer, "trimmed"));
                if (trimmedExe) CopyFixture(Path.Combine(consumer, "trimmed"), "Consumer.exe");
                CopyFixture(Path.Combine(root, "Microsoft Visual Studio", "Installer"), "vswhere.exe");
                script = Regex.Replace(script, @"\$\{\{\s*(?<key>[^}]+?)\s*\}\}", match => match.Groups["key"].Value switch
                {
                    "env.PACKAGE_OUTPUT" => Escape(feed),
                    "env.SOLUTION" => "Mammoth.LiteMapper.sln",
                    "github.ref_type" => "branch",
                    "github.ref_name" => "develop",
                    "github.ref" => "refs/heads/develop",
                    "github.sha" => new string('a', 40),
                    "steps.gitversion.outputs.semver" or "needs.pack-and-validate.outputs.semver" => "1.2.3",
                    "steps.gitversion.outputs.assembly_semver" or "steps.gitversion.outputs.assembly_file_semver" => "1.2.3.0",
                    "steps.gitversion.outputs.informational_version" => "1.2.3",
                    _ => throw new InvalidOperationException("Unbound workflow expression: " + match.Value)
                });
                var log = Path.Combine(root, "calls.txt");
                var promoted = Path.Combine(root, "promoted.txt");
                var command = $@"
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$env:LITEMAPPER_MOCK_LOG = '{Escape(log)}'
$env:LITEMAPPER_MOCK_FAIL_AT = '{failAt}'
$env:LITEMAPPER_MOCK_ROOT = '{Escape(root)}'
$env:RUNNER_TEMP = '{Escape(root)}'
$env:GITHUB_OUTPUT = '{Escape(Path.Combine(root, "outputs.txt"))}'
$env:NUGET_API_KEY = 'local-mock-key'
${{env:ProgramFiles(x86)}} = '{Escape(root)}'
function dotnet {{ & '{Escape(mock)}' release-mock @args }}
function cmd {{ & '{Escape(mock)}' release-mock windows-test @args }}
" + script + $@"
'promoted' | Set-Content -LiteralPath '{Escape(promoted)}'
if (Test-Path variable:LASTEXITCODE) {{ exit $LASTEXITCODE }}
";
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                var result = TestProcess.Run("pwsh", "-NoProfile -NonInteractive -EncodedCommand " + encoded, root, TimeSpan.FromMinutes(1));
                var calls = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
                var evidence = result.Output + result.Error + "\n" + string.Join("\n", calls);
                if (failAt == 0)
                {
                    Assert.AreEqual(0, result.ExitCode, evidence);
                    Assert.AreEqual(expectedCalls, calls.Length, evidence);
                    Assert.IsTrue(File.Exists(promoted), evidence);
                }
                else
                {
                    Assert.AreNotEqual(0, result.ExitCode, evidence);
                    Assert.AreEqual(failAt, calls.Length, "Later native commands ran after failure.\n" + evidence);
                    Assert.IsFalse(File.Exists(promoted), "A failed step reached artifact promotion.\n" + evidence);
                    StringAssert.Contains(result.Error, "23", "Failure must identify the native exit code.\n" + evidence);
                }
            }
            finally { Directory.Delete(root, true); }
        }

        private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        private static string CopyFixture(string directory, string name)
        {
            Directory.CreateDirectory(directory);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var fixture = Repository.Path("tests/Mammoth.LiteMapper.Packaging.Tests/ProcessFixture/bin/" + configuration + "/net10.0");
            const string assembly = "Mammoth.LiteMapper.ProcessFixture";
            foreach (var suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                File.Copy(Path.Combine(fixture, assembly + suffix), Path.Combine(directory, assembly + suffix));
            var executable = Path.Combine(directory, name);
            File.Copy(Path.Combine(fixture, assembly + (OperatingSystem.IsWindows() ? ".exe" : "")), executable);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return executable;
        }
    }
}
