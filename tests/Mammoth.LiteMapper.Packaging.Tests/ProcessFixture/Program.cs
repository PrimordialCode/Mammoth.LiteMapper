using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
    case "environment":
        Console.Write(Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE") + ":" + Environment.GetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER"));
        break;
}
return 0;
