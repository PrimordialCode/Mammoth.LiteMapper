using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Mammoth.LiteMapper.AllocationTestHost
{
    internal static class Program
    {
        private const int InvalidHostConfiguration = 2;
        private const int InvalidArguments = 3;
        private const int InvocationFailed = 4;

        private static int Main(string[] args)
        {
            if (!TryParseArguments(args, out var assemblyPath, out var typeName, out var methodName, out var arguments))
                return InvalidArguments;

            if (!GC.GetConfigurationVariables().TryGetValue("ConcurrentGC", out var configuredValue) ||
                configuredValue is not bool concurrentGc || concurrentGc)
            {
                Console.Error.WriteLine("AllocationTestHost: ConcurrentGC must be configured as false.");
                return InvalidHostConfiguration;
            }

            Console.WriteLine("AllocationTestHost: ConcurrentGC=False");

            var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
            AssemblyLoadContext.Default.Resolving += (_, assemblyName) => ResolveDependency(assemblyDirectory, assemblyName);

            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                var type = assembly.GetType(typeName, throwOnError: true, ignoreCase: false)!;
                var parameterTypes = new Type[arguments.Length];
                Array.Fill(parameterTypes, typeof(bool));
                var method = type.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
                    binder: null,
                    types: parameterTypes,
                    modifiers: null);

                if (method == null)
                    throw new MissingMethodException(type.FullName, methodName);
                if (method.ReturnType != typeof(void))
                    throw new InvalidOperationException("Allocation-test methods must be synchronous and return void.");

                object? instance = null;
                if (!method.IsStatic)
                    instance = Activator.CreateInstance(type, nonPublic: true) ??
                        throw new InvalidOperationException("The test type could not be constructed.");

                AppContext.SetData("Mammoth.LiteMapper.AllocationTestHost", true);
                method.Invoke(instance, arguments);
                return 0;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                Console.Error.WriteLine(exception.InnerException);
                return InvocationFailed;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return InvocationFailed;
            }
        }

        private static bool TryParseArguments(
            string[] args,
            out string assemblyPath,
            out string typeName,
            out string methodName,
            out object?[] arguments)
        {
            assemblyPath = string.Empty;
            typeName = string.Empty;
            methodName = string.Empty;
            arguments = Array.Empty<object?>();

            if (args.Length < 3 || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[2]))
            {
                WriteUsage();
                return false;
            }

            try
            {
                assemblyPath = Path.GetFullPath(args[0]);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Console.Error.WriteLine("AllocationTestHost: invalid test assembly path: " + exception.Message);
                return false;
            }

            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine("AllocationTestHost: test assembly does not exist: " + assemblyPath);
                return false;
            }

            typeName = args[1];
            methodName = args[2];
            arguments = new object?[args.Length - 3];
            for (var index = 3; index < args.Length; index++)
            {
                if (args[index] == "true")
                    arguments[index - 3] = true;
                else if (args[index] == "false")
                    arguments[index - 3] = false;
                else
                {
                    Console.Error.WriteLine("AllocationTestHost: boolean arguments must be lowercase true or false.");
                    return false;
                }
            }

            return true;
        }

        private static Assembly? ResolveDependency(string assemblyDirectory, AssemblyName assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName.Name))
                return null;

            var dependencyPath = Path.Combine(assemblyDirectory, assemblyName.Name + ".dll");
            return File.Exists(dependencyPath)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath)
                : null;
        }

        private static void WriteUsage()
        {
            Console.Error.WriteLine(
                "Usage: dotnet Mammoth.LiteMapper.AllocationTestHost.dll " +
                "<test-assembly-path> <test-type-full-name> <test-method-name> [true|false ...]");
        }
    }
}
