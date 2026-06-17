using System;
using System.IO;

namespace Mammoth.LiteMapper.Generator.Tests
{
    internal static class Repository
    {
        internal static string Root
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, "SPECIFICATION.md")))
                {
                    directory = directory.Parent;
                }

                if (directory == null)
                {
                    throw new DirectoryNotFoundException("Could not find repository root.");
                }

                return directory.FullName;
            }
        }

        internal static string Path(string relativePath)
        {
            return System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }
    }
}
