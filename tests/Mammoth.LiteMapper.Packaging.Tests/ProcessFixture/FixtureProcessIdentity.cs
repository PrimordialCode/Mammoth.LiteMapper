using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Mammoth.LiteMapper.ProcessFixture
{
    public static class FixtureProcessIdentity
    {
        public static LinuxProcessSnapshot ParseLinuxSnapshot(string stat)
        {
            // comm (field 2) can contain spaces and ')' characters. Fields after
            // its final ')' begin at field 3; starttime is field 22 (index 19).
            var commandStart = stat.IndexOf('(');
            var commandEnd = stat.LastIndexOf(')');
            if (commandStart <= 0 || commandEnd < commandStart)
            {
                throw new InvalidDataException("Fixture procfs record has no complete command name.");
            }

            var fields = stat.Substring(commandEnd + 1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 ||
                !int.TryParse(stat.Substring(0, commandStart).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId <= 0 ||
                fields[0].Length != 1 || "RSDZTtWXxKPI".IndexOf(fields[0][0]) < 0 ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId) ||
                !int.TryParse(fields[17], NumberStyles.None, CultureInfo.InvariantCulture, out var threadCount) ||
                !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var startStamp))
            {
                throw new InvalidDataException("Fixture procfs record has invalid identity or lifecycle fields.");
            }

            return new LinuxProcessSnapshot(processId, parentProcessId, fields[0][0], threadCount, startStamp);
        }

        public static LinuxProcessSnapshot ReadLinuxSnapshot(int processId)
        {
            var statPath = processId == Environment.ProcessId ? "/proc/self/stat" :
                "/proc/" + processId.ToString(CultureInfo.InvariantCulture) + "/stat";
            return ParseLinuxSnapshot(File.ReadAllText(statPath));
        }

        public static long GetStartStamp(Process process)
        {
            // .NET's Linux StartTime uses a wall-clock boot-time estimate cached
            // separately in each process. Compare the kernel token directly.
            if (OperatingSystem.IsLinux())
            {
                return ReadLinuxSnapshot(process.Id).StartStamp;
            }

            return process.StartTime.ToUniversalTime().Ticks;
        }

        public static long ParseLinuxStartStamp(string stat)
        {
            return ParseLinuxSnapshot(stat).StartStamp;
        }
    }

    public readonly struct LinuxProcessSnapshot
    {
        internal LinuxProcessSnapshot(int processId, int parentProcessId, char state, int threadCount, long startStamp)
        {
            ProcessId = processId;
            ParentProcessId = parentProcessId;
            State = state;
            ThreadCount = threadCount;
            StartStamp = startStamp;
        }

        public int ProcessId { get; }
        public int ParentProcessId { get; }
        public char State { get; }
        public int ThreadCount { get; }
        public long StartStamp { get; }

        public bool IsAliveFor(int expectedProcessId, long expectedStartStamp)
        {
            // A zombie group leader may still have executable sibling threads.
            // Zero threads is an unconfirmed teardown snapshot, not a safe pass.
            return ProcessId == expectedProcessId && StartStamp == expectedStartStamp &&
                !((State == 'Z' || State == 'X') && ThreadCount == 1);
        }

        public override string ToString()
        {
            return FormattableString.Invariant($"PID {ProcessId} PPID {ParentProcessId} state {State} threads {ThreadCount} start {StartStamp}");
        }
    }
}
