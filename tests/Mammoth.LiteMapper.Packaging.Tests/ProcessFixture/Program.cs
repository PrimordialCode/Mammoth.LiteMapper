using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mammoth.LiteMapper.ProcessFixture;

// Release-script tests launch this native process instead of any real .NET or
// consumer operation. Returning a real process exit exercises LASTEXITCODE.
var isVswhere = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "vswhere";
if ((args.Length > 0 && args[0] == "release-mock") ||
    (isVswhere && Environment.GetEnvironmentVariable("LITEMAPPER_MOCK_LOG") != null) ||
    (args.Length == 0 && Environment.GetEnvironmentVariable("LITEMAPPER_MOCK_LOG") != null))
{
    var log = Environment.GetEnvironmentVariable("LITEMAPPER_MOCK_LOG")!;
    var operation = isVswhere ? "vswhere " + string.Join(" ", args) :
        args.Length == 0 ? "consumer " + new DirectoryInfo(AppContext.BaseDirectory).Name : string.Join(" ", args.Skip(1));
    File.AppendAllText(log, operation + Environment.NewLine);
    var position = File.ReadAllLines(log).Length;
    if (operation.StartsWith("gitversion", StringComparison.Ordinal))
        Console.WriteLine("{\"SemVer\":\"1.2.3\",\"AssemblySemVer\":\"1.2.3.0\",\"AssemblySemFileVer\":\"1.2.3.0\",\"InformationalVersion\":\"1.2.3\"}");
    if (operation.StartsWith("vswhere", StringComparison.Ordinal))
        Console.WriteLine(Environment.GetEnvironmentVariable("LITEMAPPER_MOCK_ROOT"));
    return position == int.Parse(Environment.GetEnvironmentVariable("LITEMAPPER_MOCK_FAIL_AT")!) ? 23 : 0;
}

_ = Task.Run(async () => { await Task.Delay(10000); Environment.Exit(99); });
switch (args[0])
{
    case "pressure":
        Console.Error.Write(new string('e', 1024 * 1024));
        Console.Out.Write("complete");
        break;
    case "wait":
        Thread.Sleep(4000);
        break;
    case "hold":
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("wait");
        using (var child = Process.Start(start)) { }
        Console.Out.Write("parent-exited");
        break;
    case "timeout-output":
        WriteIdentity(args[1]);
        Console.Out.Write("stdout-head-marker|" + new string('o', 8192) + "|stdout-tail-marker");
        Console.Error.Write("stderr-head-marker|" + new string('e', 8192) + "|stderr-tail-marker");
        Console.Out.Flush();
        Console.Error.Flush();
        Thread.Sleep(8000);
        break;
    case "tree-timeout":
        WriteIdentity(args[1]);
        using (var child = StartFixture("tree-child", args[2]))
        {
            WaitForIdentity(args[2], child);
            Console.Out.Write("tree-parent-stdout-ready");
            Console.Error.Write("tree-parent-stderr-ready");
            Console.Out.Flush();
            Console.Error.Flush();
            Thread.Sleep(8000);
        }
        break;
    case "tree-child":
        WriteIdentity(args[1]);
        Console.Out.Write("tree-child-stdout-ready");
        Console.Error.Write("tree-child-stderr-ready");
        Console.Out.Flush();
        Console.Error.Flush();
        Thread.Sleep(8000);
        break;
    case "orphan-pipe":
        using (var child = StartFixture("orphan-pipe-holder", args[1]))
        {
            WaitForIdentity(args[1], child);
        }
        Console.Out.Write("orphan-parent-stdout-exited");
        Console.Error.Write("orphan-parent-stderr-exited");
        break;
    case "orphan-pipe-holder":
        WriteIdentity(args[1]);
        Console.Out.Write("orphan-child-stdout-ready");
        Console.Error.Write("orphan-child-stderr-ready");
        Console.Out.Flush();
        Console.Error.Flush();
        Thread.Sleep(8000);
        break;
    case "environment":
        Console.Write(Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE") + ":" + Environment.GetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER"));
        break;
}
return 0;

static Process StartFixture(string mode, string identityFile)
{
    // Redirecting stdin explicitly passes the inherited stdout/stderr handles on
    // Windows even though the fixture runs without a console window.
    var start = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
    };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(mode);
    start.ArgumentList.Add(identityFile);
    return Process.Start(start)!;
}

static void WriteIdentity(string path)
{
    using var process = Process.GetCurrentProcess();
    File.WriteAllText(path, Environment.ProcessId + "|" + FixtureProcessIdentity.GetStartStamp(process));
}

static void WaitForIdentity(string path, Process child)
{
    var deadline = Stopwatch.StartNew();
    while (!File.Exists(path) && !child.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(3))
    {
        Thread.Sleep(10);
    }
}
