using System.IO;
using Mammoth.LiteMapper.ProcessFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class FixtureProcessObservationTests
    {
        [TestMethod]
        public void LinuxSnapshotPreservesIdentityAndLifecycleFieldsAfterTheCompleteCommandName()
        {
            var snapshot = FixtureProcessIdentity.ParseLinuxSnapshot(
                BuildStat("123", "fixture ) with (parentheses)", "S", "0", "7", "0"));

            Assert.AreEqual(123, snapshot.ProcessId);
            Assert.AreEqual(0, snapshot.ParentProcessId);
            Assert.AreEqual('S', snapshot.State);
            Assert.AreEqual(7, snapshot.ThreadCount);
            Assert.AreEqual(0L, snapshot.StartStamp);
            Assert.IsTrue(snapshot.IsAliveFor(123, 0L));
        }

        [TestMethod]
        [DataRow("Z")]
        [DataRow("X")]
        public void TerminalSingleThreadLinuxSnapshotsAreNotAlive(string state)
        {
            var snapshot = ParseSnapshot(state, "1");

            Assert.IsFalse(snapshot.IsAliveFor(123, 987654L), "A terminal process with no sibling threads cannot still execute fixture work.");
        }

        [TestMethod]
        [DataRow("Z")]
        [DataRow("X")]
        public void TerminalLeaderWithSiblingThreadsRemainsConservativelyAlive(string state)
        {
            var snapshot = ParseSnapshot(state, "2");

            Assert.IsTrue(snapshot.IsAliveFor(123, 987654L), "A terminal leader can retain sibling threads that still own inherited fixture handles.");
        }

        [TestMethod]
        [DataRow("R")]
        [DataRow("S")]
        [DataRow("D")]
        [DataRow("T")]
        [DataRow("t")]
        [DataRow("W")]
        [DataRow("x")]
        [DataRow("K")]
        [DataRow("P")]
        [DataRow("I")]
        public void NonterminalLinuxStatesRemainAlive(string state)
        {
            var snapshot = ParseSnapshot(state, "1");

            Assert.IsTrue(snapshot.IsAliveFor(123, 987654L), "Only uppercase Z and X provide confirmed terminal observations.");
        }

        [TestMethod]
        [DataRow("Z")]
        [DataRow("X")]
        public void ZeroThreadCountDoesNotConfirmTermination(string state)
        {
            var snapshot = ParseSnapshot(state, "0");

            Assert.IsTrue(snapshot.IsAliveFor(123, 987654L), "A zero thread count is an unconfirmed procfs observation, not proof of termination.");
        }

        [TestMethod]
        [DataRow(124, 987654L)]
        [DataRow(123, 987655L)]
        public void LinuxSnapshotCannotKeepAReusedIdentityAlive(int expectedProcessId, long expectedStartStamp)
        {
            var snapshot = ParseSnapshot("S", "1");

            Assert.IsFalse(snapshot.IsAliveFor(expectedProcessId, expectedStartStamp), "Both the process ID and kernel start token must still match the owned fixture identity.");
        }

        [TestMethod]
        [DataRow("123 fixture) S 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 1 21 987654 23")]
        [DataRow("123 (fixture S 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 1 21 987654 23")]
        [DataRow("123 (fixture) S 4 5")]
        public void IncompleteLinuxSnapshotStructureFailsLoudly(string stat)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => FixtureProcessIdentity.ParseLinuxSnapshot(stat));
        }

        [TestMethod]
        [DataRow("invalid", "S", "4", "1", "987654")]
        [DataRow("0", "S", "4", "1", "987654")]
        [DataRow("-1", "S", "4", "1", "987654")]
        [DataRow("123", "Q", "4", "1", "987654")]
        [DataRow("123", "SS", "4", "1", "987654")]
        [DataRow("123", "S", "invalid", "1", "987654")]
        [DataRow("123", "S", "-1", "1", "987654")]
        [DataRow("123", "S", "4", "invalid", "987654")]
        [DataRow("123", "S", "4", "-1", "987654")]
        [DataRow("123", "S", "4", "1", "invalid")]
        [DataRow("123", "S", "4", "1", "-1")]
        public void InvalidLinuxSnapshotFieldsFailLoudly(string processId, string state, string parentProcessId, string threadCount, string startStamp)
        {
            var stat = BuildStat(processId, "fixture", state, parentProcessId, threadCount, startStamp);

            Assert.ThrowsExactly<InvalidDataException>(() => FixtureProcessIdentity.ParseLinuxSnapshot(stat));
        }

        private static LinuxProcessSnapshot ParseSnapshot(string state, string threadCount)
        {
            return FixtureProcessIdentity.ParseLinuxSnapshot(BuildStat("123", "fixture", state, "4", threadCount, "987654"));
        }

        private static string BuildStat(string processId, string commandName, string state, string parentProcessId, string threadCount, string startStamp)
        {
            return processId + " (" + commandName + ") " + state + " " + parentProcessId +
                " 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 " + threadCount + " 21 " + startStamp + " 23";
        }
    }
}
