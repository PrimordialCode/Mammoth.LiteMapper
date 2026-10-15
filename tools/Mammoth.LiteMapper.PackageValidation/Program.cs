using System;
using System.Collections.Generic;

namespace Mammoth.LiteMapper.PackageValidation
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args.Length % 2 != 0)
            {
                Console.Error.WriteLine("Usage: Mammoth.LiteMapper.PackageValidation <package.nupkg> <symbols.snupkg> [<package.nupkg> <symbols.snupkg> ...]");
                return 2;
            }

            var errors = new List<string>();
            for (var i = 0; i < args.Length; i += 2)
            {
                foreach (var error in SymbolPackageValidator.Validate(args[i], args[i + 1]))
                {
                    errors.Add(error);
                }
            }

            if (errors.Count != 0)
            {
                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                return 1;
            }

            Console.WriteLine("Validated " + (args.Length / 2) + " symbol package pair(s).");
            return 0;
        }
    }
}
