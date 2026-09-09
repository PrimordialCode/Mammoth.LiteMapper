using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class BenchmarkComparisonPolicyTests
    {
        [TestMethod]
        public void EquivalentResultsPassAndRecordComparedScenarioCount()
        {
            var result = Compare(Result(100, 1, 64), Result(101, 1, 64));
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            StringAssert.Contains(result.Output, "PASS: 1 benchmark scenarios compared");
        }

        [TestMethod]
        public void AllocationIncreaseBlocksRelease()
        {
            var result = Compare(Result(100, 1, 64), Result(100, 1, 65));
            Assert.AreNotEqual(0, result.ExitCode);
            StringAssert.Contains(result.Output + result.Error, "BLOCKED: allocation regression");
        }

        [TestMethod]
        public void SignificantThroughputLossUsesReviewAndBlockingThresholds()
        {
            var review = Compare(Result(100, 0.1, 64), Result(112, 0.1, 64));
            Assert.AreNotEqual(0, review.ExitCode);
            StringAssert.Contains(review.Output + review.Error, "REVIEW_REQUIRED: throughput loss");

            var blocked = Compare(Result(100, 0.1, 64), Result(130, 0.1, 64));
            Assert.AreNotEqual(0, blocked.ExitCode);
            StringAssert.Contains(blocked.Output + blocked.Error, "BLOCKED: throughput loss");
        }

        [TestMethod]
        public void OverlappingConfidenceIntervalsDoNotTriggerAThroughputGate()
        {
            var result = Compare(Result(100, 20, 64), Result(130, 20, 64));
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        }

        [TestMethod]
        public void MissingScenariosAndIncompatibleEnvironmentsFailLoudly()
        {
            var missing = Compare(Result(100, 1, 64), Result(100, 1, 64, id: "Other"));
            Assert.AreNotEqual(0, missing.ExitCode);
            StringAssert.Contains(missing.Output + missing.Error, "ERROR: benchmark scenario sets differ");

            var incompatible = Compare(Result(100, 1, 64), Result(100, 1, 64, cpu: "Other CPU"));
            Assert.AreNotEqual(0, incompatible.ExitCode);
            StringAssert.Contains(incompatible.Output + incompatible.Error, "ERROR: incompatible benchmark environment");
        }

        private static ProcessResult Compare(string baseline, string candidate)
        {
            var directory = Path.Combine(Path.GetTempPath(), "Mammoth.LiteMapper.BenchmarkPolicy", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var baselinePath = Path.Combine(directory, "baseline.json");
                var candidatePath = Path.Combine(directory, "candidate.json");
                File.WriteAllText(baselinePath, baseline);
                File.WriteAllText(candidatePath, candidate);
                var script = Repository.Path("benchmarks/compare-results.ps1");
                return TestProcess.Run("pwsh", "-NoProfile -File \"" + script + "\" -Baseline \"" + baselinePath + "\" -Candidate \"" + candidatePath + "\"", Repository.Root, TimeSpan.FromSeconds(20));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string Result(double mean, double standardError, long allocated, string id = "Flat.Map", string cpu = "Test CPU")
        {
            return "{\"metadata\":{\"runtime\":\"net10.0\",\"sdk\":\"10.0.400\",\"cpu\":\"" + cpu + "\",\"os\":\"Test OS\",\"configuration\":\"Medium\",\"mapperly\":\"1\",\"mapster\":\"1\",\"automapper\":\"1\",\"litemapper\":\"candidate\",\"sourceRevision\":\"revision\"},\"benchmarks\":[{\"id\":\"" + id + "\",\"meanNanoseconds\":" + mean.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"standardErrorNanoseconds\":" + standardError.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"allocatedBytes\":" + allocated + "}]}";
        }
    }
}
