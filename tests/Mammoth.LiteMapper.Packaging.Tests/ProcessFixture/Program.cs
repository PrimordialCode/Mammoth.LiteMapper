using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
