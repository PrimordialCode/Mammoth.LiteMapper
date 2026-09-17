using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class ReleaseQualityContractTests
    {
        private static readonly Regex SemVer = new Regex(
            "^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?<pre>-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        [TestMethod]
        [DataRow("1.0.0", "stable")]
        [DataRow("1.2.3-alpha.1", "alpha")]
        [DataRow("2.0.0-beta.4", "beta")]
        public void BareSemVerTagsResolveToTheirReleaseChannel(string tag, string channel)
        {
            Assert.AreEqual(channel, ResolveChannel(tag));
        }

        [TestMethod]
        [DataRow("1.2.3", "1.2.3", true)]
        [DataRow("1.2.3-alpha.1", "1.2.3-alpha.1", true)]
        [DataRow("1.2.3-beta.1", "1.2.3-alpha.1", false)]
        [DataRow("1.2.4", "1.2.3", false)]
        public void TagMustMatchTheVersionBeingPublished(string tag, string version, bool expected)
        {
            Assert.AreEqual(expected, string.Equals(tag, version, StringComparison.Ordinal));
        }

        [TestMethod]
        public void ReleaseBranchesUseManualDeploymentForExplicitTags()
        {
            var config = File.ReadAllText(Path.Combine(Repository.Root, "GitVersion.yml"));

            Assert.IsTrue(
                Regex.IsMatch(
                    config,
                    @"(?ms)^  release:\r?\n    regex: \^release\[/-\]\r?\n    mode: ManualDeployment\r?\n    increment: None.*\r?\n    prevent-increment:\r?\n      when-current-commit-tagged: true\r?\n"),
                "Release branches must preserve explicitly tagged prerelease versions.");
        }

        [TestMethod]
        [DataRow("v1.0.0")]
        [DataRow("1.0")]
        [DataRow("1.0.0+©")]
        [DataRow("refs/tags/1.0.0")]
        public void InvalidOrNonBareTagsAreRejected(string tag)
        {
            Assert.IsFalse(TryResolveChannel(tag, out _), tag);
        }

        [TestMethod]
        public void DryRunExecutesWithoutNuGetPushAndLeavesExistingPackageUntouched()
        {
            var relativeOutput = Path.Combine("artifacts", "release-quality-" + Guid.NewGuid().ToString("N"));
            var output = Path.Combine(Repository.Root, relativeOutput);
            Directory.CreateDirectory(output);
            var sentinel = Path.Combine(output, "sentinel.txt");
            File.WriteAllText(sentinel, "must remain");
            try
            {
                var command = @"
function dotnet {
    $CommandArgs = $args
    $global:LASTEXITCODE = 0
    $joined = $CommandArgs -join ' '
    if ($joined -like '*gitversion*') {
        '{""SemVer"":""1.2.3-beta.1"",""AssemblySemVer"":""1.2.3.0"",""AssemblySemFileVer"":""1.2.3.0"",""InformationalVersion"":""1.2.3-beta.1""}'
        return
    }
    if ($joined -like 'pack *') {
        $index = [Array]::IndexOf($CommandArgs, '-o')
        $destination = $CommandArgs[$index + 1]
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        foreach ($id in @('Mammoth.LiteMapper.Abstractions', 'Mammoth.LiteMapper.Generator', 'Mammoth.LiteMapper')) {
            Set-Content -Path (Join-Path $destination ($id + '.1.2.3-beta.1.nupkg')) -Value $id
            Set-Content -Path (Join-Path $destination ($id + '.1.2.3-beta.1.snupkg')) -Value $id
        }
        return
    }
    if ($joined -like '*nuget push*') { Set-Content -Path (Join-Path (Get-Location) 'push.marker') -Value pushed }
}
& '" + Path.Combine(Repository.Root, "publish-nuget.ps1") + @"' -Source local -OutputDirectory '" + relativeOutput.Replace("'", "''") + @"' -SkipValidation -DryRun
";
                var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
                var result = TestProcess.Run("pwsh", "-NoProfile -EncodedCommand " + encoded, Repository.Root, TimeSpan.FromMinutes(1));
                Assert.AreEqual(0, result.ExitCode, result.Output + Environment.NewLine + result.Error);
                Assert.IsFalse(File.Exists(Path.Combine(Repository.Root, "push.marker")), "Dry-run invoked NuGet push.");
                Assert.AreEqual("must remain", File.ReadAllText(sentinel));
            }
            finally
            {
                if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
                var marker = Path.Combine(Repository.Root, "push.marker");
                if (File.Exists(marker)) File.Delete(marker);
            }
        }

        [TestMethod]
        public void ReleaseScriptPublishesExactSymbolsAndDoesNotSkipDuplicates()
        {
            var script = File.ReadAllText(Path.Combine(Repository.Root, "publish-nuget.ps1"));
            StringAssert.Contains(script, "'.snupkg'");
            StringAssert.Contains(script, "Expected exactly");
            Assert.IsFalse(script.Contains("--skip-duplicate", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void ReleaseArtifactsMustContainAllThreeVersionedPackages()
        {
            var feed = Milestone14PackagingAndAotTests.CreateFeed();
            foreach (var id in new[] { "Mammoth.LiteMapper.Abstractions", "Mammoth.LiteMapper.Generator", "Mammoth.LiteMapper" })
            {
                File.WriteAllText(Path.Combine(feed, id + ".1.0.0.nupkg"), id);
            }

            var packages = Directory.GetFiles(feed, "Mammoth.LiteMapper*.1.0.0.nupkg")
                .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(3, packages.Length);
            CollectionAssert.AreEquivalent(
                new[] { "Mammoth.LiteMapper.Abstractions.1.0.0.nupkg", "Mammoth.LiteMapper.Generator.1.0.0.nupkg", "Mammoth.LiteMapper.1.0.0.nupkg" },
                packages.Select(Path.GetFileName).ToArray());
        }

        [TestMethod]
        public void FinalPackageConsumerRestoresWindowsRuntimeBeforeNoRestorePublish()
        {
            var workflow = File.ReadAllText(Path.Combine(
                Repository.Root, ".github", "workflows", "ci.yml"));

            StringAssert.Contains(
                workflow,
                "dotnet restore (Join-Path $consumer 'Consumer.csproj') --configfile (Join-Path $consumer 'NuGet.Config') --no-cache -r win-x64");
            StringAssert.Contains(
                workflow,
                "dotnet publish (Join-Path $consumer 'Consumer.csproj') -c Release --no-restore -r win-x64 -p:PublishTrimmed=true");
            StringAssert.Contains(
                workflow,
                "dotnet publish (Join-Path $consumer 'Consumer.csproj') -c Release --no-restore -r win-x64 -p:PublishAot=true");
        }

        [TestMethod]
        public void FinalPackageConsumerDiscoversTheNativeAotExecutable()
        {
            var workflow = File.ReadAllText(Path.Combine(
                Repository.Root, ".github", "workflows", "ci.yml"));

            StringAssert.Contains(
                workflow,
                "$aotExecutables = @(Get-ChildItem -LiteralPath $aot -File -Recurse -Filter '*.exe')");
            StringAssert.Contains(workflow, "if ($aotExecutables.Count -ne 1)");
            StringAssert.Contains(workflow, "& $aotExecutables[0].FullName");
            Assert.IsFalse(workflow.Contains(
                "& (Join-Path $aot 'Consumer.exe')",
                StringComparison.Ordinal));
        }

        [TestMethod]
        public void ArtifactActionsUseNode24CompatibleReleases()
        {
            var workflow = File.ReadAllText(Path.Combine(
                Repository.Root, ".github", "workflows", "ci.yml"));

            StringAssert.Contains(workflow, "actions/upload-artifact@v6");
            StringAssert.Contains(workflow, "actions/download-artifact@v7");
            Assert.IsFalse(workflow.Contains("actions/upload-artifact@v4", StringComparison.Ordinal));
            Assert.IsFalse(workflow.Contains("actions/download-artifact@v4", StringComparison.Ordinal));
        }

        private static string ResolveChannel(string tag)
        {
            Assert.IsTrue(TryResolveChannel(tag, out var channel), tag);
            return channel!;
        }

        private static bool TryResolveChannel(string tag, out string? channel)
        {
            var match = SemVer.Match(tag);
            if (!match.Success)
            {
                channel = null;
                return false;
            }

            channel = match.Groups["pre"].Success
                ? match.Groups["pre"].Value.StartsWith("-alpha", StringComparison.Ordinal) ? "alpha" : "beta"
                : "stable";
            return true;
        }
    }
}
