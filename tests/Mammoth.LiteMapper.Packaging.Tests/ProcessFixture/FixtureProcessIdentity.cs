using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Mammoth.LiteMapper.ProcessFixture
{
    public static class FixtureProcessIdentity
    {
        public static long GetStartStamp(Process process)
        {
            // .NET's Linux StartTime uses a wall-clock boot-time estimate cached
            // separately in each process. Compare the kernel token directly.
            if (OperatingSystem.IsLinux())
            {
                var statPath = process.Id == Environment.ProcessId ? "/proc/self/stat" :
                    "/proc/" + process.Id.ToString(CultureInfo.InvariantCulture) + "/stat";
                return ParseLinuxStartStamp(File.ReadAllText(statPath));
            }

            return process.StartTime.ToUniversalTime().Ticks;
        }

        public static long ParseLinuxStartStamp(string stat)
        {
            // comm (field 2) can contain spaces and ')' characters. Fields after
            // its final ')' begin at field 3; starttime is field 22 (index 19).
            var commandEnd = stat.LastIndexOf(')');
            if (commandEnd < 0)
            {
                throw new InvalidDataException("Fixture procfs record has no complete command name.");
            }

            var fields = stat.Substring(commandEnd + 1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 ||
                !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var startStamp))
            {
                throw new InvalidDataException("Fixture procfs record has no valid kernel start token.");
            }

            return startStamp;
        }
    }
}
