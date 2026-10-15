using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class SymbolPublicationGateTests
    {
        [TestMethod]
        public void ReleaseWorkflowValidatesSymbolsBeforeConsumerValidationAndArtifactUpload()
        {
            var workflow = File.ReadAllText(Repository.Path(".github/workflows/ci.yml"));
            var validation = workflow.IndexOf("      - name: Validate package symbols", StringComparison.Ordinal);
            Assert.IsTrue(validation >= 0, "Release packages must pass symbol validation before promotion.");
            Assert.IsTrue(validation < workflow.IndexOf("      - name: Validate final package consumers", StringComparison.Ordinal));
            Assert.IsTrue(validation < workflow.IndexOf("      - name: Upload validated packages", StringComparison.Ordinal));
            var block = workflow.Substring(validation, workflow.IndexOf("      - name: Validate final package consumers", StringComparison.Ordinal) - validation);
            StringAssert.Contains(block, "Mammoth.LiteMapper.PackageValidation.csproj");
            StringAssert.Contains(block, "'Mammoth.LiteMapper.Abstractions', 'Mammoth.LiteMapper.Generator', 'Mammoth.LiteMapper'");
            StringAssert.Contains(block, "if ($LASTEXITCODE -ne 0)");
        }

        [TestMethod]
        public void ManualPublishingValidatesAllSymbolPairsEvenWhenBroadValidationIsSkipped()
        {
            var script = File.ReadAllText(Repository.Path("publish-nuget.ps1"));
            var validation = script.IndexOf("& dotnet run --project $symbolValidator", StringComparison.Ordinal);
            Assert.IsTrue(validation >= 0, "Symbol validation must be mandatory even with -SkipValidation.");
            var artifactCheck = script.IndexOf("Expected exactly", StringComparison.Ordinal);
            var publish = script.LastIndexOf("foreach ($packageId in $packageIds)", StringComparison.Ordinal);
            Assert.IsTrue(artifactCheck < validation && validation < publish);
            StringAssert.Contains(script, "Assert-NativeSuccess 'Validate package symbols'");
            StringAssert.Contains(script, "$symbolPairs += Join-Path $resolvedOutput \"$packageId.$version.snupkg\"");
        }

        [TestMethod]
        public void ManualPublishingStopsBeforePushWhenSymbolValidationFails()
        {
            var relativeOutput = Path.Combine("artifacts", "symbols-gate-" + Guid.NewGuid().ToString("N"));
            var output = Repository.Path(relativeOutput);
            Directory.CreateDirectory(output);
            var marker = Path.Combine(output, "push.marker");
            var command = @"
$ErrorActionPreference = 'Stop'
function dotnet {
    $global:LASTEXITCODE = 0
    $joined = $args -join ' '
    if ($joined -like '*gitversion*') {
        '{""SemVer"":""1.2.3"",""AssemblySemVer"":""1.2.3.0"",""AssemblySemFileVer"":""1.2.3.0"",""InformationalVersion"":""1.2.3""}'
    } elseif ($joined -like 'pack *') {
        $index = [Array]::IndexOf($args, '-o')
        foreach ($id in @('Mammoth.LiteMapper.Abstractions', 'Mammoth.LiteMapper.Generator', 'Mammoth.LiteMapper')) {
            foreach ($extension in @('nupkg', 'snupkg')) {
                Set-Content -LiteralPath (Join-Path $args[$index + 1] ($id + '.1.2.3.' + $extension)) -Value 'mock artifact'
            }
        }
    } elseif ($joined -like 'run *PackageValidation*') {
        $global:LASTEXITCODE = 23
    } elseif ($joined -like 'nuget push *') {
        Set-Content -LiteralPath '" + Escape(marker) + @"' -Value 'pushed'
    }
}
& '" + Escape(Repository.Path("publish-nuget.ps1")) + @"' -ApiKey 'local-mock-key' -Source local -OutputDirectory '" + Escape(relativeOutput) + @"' -SkipValidation -DryRun:$false
";
            try
            {
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                var result = TestProcess.Run("pwsh", "-NoProfile -NonInteractive -EncodedCommand " + encoded, Repository.Root, TimeSpan.FromMinutes(1));
                Assert.AreNotEqual(0, result.ExitCode, result.Output + result.Error);
                StringAssert.Contains(result.Error, "23");
                Assert.IsFalse(File.Exists(marker), "Invalid symbol packages reached NuGet push.");
            }
            finally
            {
                Directory.Delete(output, true);
            }
        }

        private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
