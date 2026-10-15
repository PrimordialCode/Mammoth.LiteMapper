using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Mammoth.LiteMapper.PackageValidation
{
    public static class SymbolPackageValidator
    {
        private static readonly HashSet<string> AllowedSymbolExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdb",
            ".nuspec",
            ".xml",
            ".psmdcp",
            ".rels",
            ".p7s"
        };

        /// <summary>
        /// Validates that every PDB supplied by a symbol package matches a DLL or EXE at the same
        /// archive-relative path. A primary package may contain additional assemblies without PDBs.
        /// </summary>
        public static IReadOnlyList<string> Validate(string packagePath, string symbolPackagePath)
        {
            var errors = new List<string>();

            if (!packagePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                packagePath.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(packagePath + ": expected a .nupkg package path.");
            }

            if (!symbolPackagePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(symbolPackagePath + ": expected a .snupkg symbol package path.");
            }

            if (errors.Count != 0)
            {
                return errors;
            }

            try
            {
                using var package = ZipFile.OpenRead(packagePath);
                using var symbolPackage = ZipFile.OpenRead(symbolPackagePath);
                ValidateArchives(packagePath, package, symbolPackagePath, symbolPackage, errors);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidDataException ||
                exception is BadImageFormatException ||
                exception is CryptographicException ||
                exception is XmlException ||
                exception is ArgumentException ||
                exception is UnauthorizedAccessException)
            {
                errors.Add(symbolPackagePath + ": validation failed: " + exception.Message);
            }

            return errors;
        }

        private static void ValidateArchives(
            string packagePath,
            ZipArchive package,
            string symbolPackagePath,
            ZipArchive symbolPackage,
            List<string> errors)
        {
            var packageIdentity = ReadIdentity(packagePath, package, false, errors);
            var symbolIdentity = ReadIdentity(symbolPackagePath, symbolPackage, true, errors);

            if (packageIdentity != null && symbolIdentity != null)
            {
                if (!string.Equals(packageIdentity.Value.Id, symbolIdentity.Value.Id, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(symbolPackagePath + ": package ID '" + symbolIdentity.Value.Id + "' does not match '" + packageIdentity.Value.Id + "'.");
                }

                if (!string.Equals(packageIdentity.Value.Version, symbolIdentity.Value.Version, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(symbolPackagePath + ": package version '" + symbolIdentity.Value.Version + "' does not match '" + packageIdentity.Value.Version + "'.");
                }
            }

            foreach (var entry in symbolPackage.Entries.Where(static entry => !IsDirectory(entry)))
            {
                var extension = Path.GetExtension(entry.FullName);
                if (!AllowedSymbolExtensions.Contains(extension))
                {
                    errors.Add(symbolPackagePath + ": entry '" + entry.FullName + "' has unsupported extension '" + extension + "'.");
                }
            }

            var packageEntries = package.Entries
                .Where(static entry => !IsDirectory(entry))
                .ToDictionary(static entry => NormalizePath(entry.FullName), StringComparer.OrdinalIgnoreCase);
            var pdbEntries = symbolPackage.Entries
                .Where(static entry => !IsDirectory(entry) && entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (pdbEntries.Length == 0)
            {
                errors.Add(symbolPackagePath + ": symbol package does not contain a PDB.");
                return;
            }

            foreach (var pdbEntry in pdbEntries)
            {
                var pdbPath = NormalizePath(pdbEntry.FullName);
                var stem = pdbPath.Substring(0, pdbPath.Length - 4);
                if (!packageEntries.TryGetValue(stem + ".dll", out var peEntry) &&
                    !packageEntries.TryGetValue(stem + ".exe", out peEntry))
                {
                    errors.Add(symbolPackagePath + ": PDB '" + pdbEntry.FullName + "' has no DLL or EXE at the same archive-relative path in " + packagePath + ".");
                    continue;
                }

                ValidatePdb(symbolPackagePath, pdbEntry, peEntry, errors);
            }
        }

        private static (string Id, string Version)? ReadIdentity(
            string archivePath,
            ZipArchive archive,
            bool requireSymbolsPackage,
            List<string> errors)
        {
            var nuspecs = archive.Entries
                .Where(static entry => !IsDirectory(entry) && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (nuspecs.Length != 1)
            {
                errors.Add(archivePath + ": expected exactly one nuspec, found " + nuspecs.Length + ".");
                return null;
            }

            using var stream = nuspecs[0].Open();
            var document = XDocument.Load(stream, LoadOptions.None);
            var metadata = document.Descendants().FirstOrDefault(static element => element.Name.LocalName == "metadata");
            var id = metadata?.Elements().FirstOrDefault(static element => element.Name.LocalName == "id")?.Value.Trim();
            var version = metadata?.Elements().FirstOrDefault(static element => element.Name.LocalName == "version")?.Value.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version))
            {
                errors.Add(archivePath + ": nuspec must contain non-empty id and version metadata.");
                return null;
            }

            if (requireSymbolsPackage)
            {
                var isSymbolsPackage = metadata!
                    .Descendants()
                    .Any(static element =>
                        element.Name.LocalName == "packageType" &&
                        string.Equals((string?)element.Attribute("name"), "SymbolsPackage", StringComparison.OrdinalIgnoreCase));
                if (!isSymbolsPackage)
                {
                    errors.Add(archivePath + ": nuspec must declare packageType name=" + '"' + "SymbolsPackage" + '"' + ".");
                }
            }

            return (id, version);
        }

        private static void ValidatePdb(
            string symbolPackagePath,
            ZipArchiveEntry pdbEntry,
            ZipArchiveEntry peEntry,
            List<string> errors)
        {
            var pdbBytes = ReadEntry(pdbEntry);
            if (pdbBytes.Length < 4 ||
                pdbBytes[0] != (byte)'B' ||
                pdbBytes[1] != (byte)'S' ||
                pdbBytes[2] != (byte)'J' ||
                pdbBytes[3] != (byte)'B')
            {
                errors.Add(symbolPackagePath + ": PDB '" + pdbEntry.FullName + "' is not portable.");
                return;
            }

            var peBytes = ReadEntry(peEntry);
            using var peStream = new MemoryStream(peBytes, false);
            using var peReader = new PEReader(peStream);
            MetadataReaderProvider? pdbProvider = null;
            try
            {
                var associated = peReader.TryOpenAssociatedPortablePdb(
                    peEntry.FullName,
                    _ => new MemoryStream(pdbBytes, false),
                    out pdbProvider,
                    out var associatedPdbPath);
                if (!associated || pdbProvider == null || associatedPdbPath == null)
                {
                    errors.Add(symbolPackagePath + ": PDB '" + pdbEntry.FullName + "' does not match the PE identity for '" + peEntry.FullName + "'.");
                    return;
                }

                var checksumEntries = peReader.ReadDebugDirectory()
                    .Where(static entry => entry.Type == DebugDirectoryEntryType.PdbChecksum)
                    .Select(peReader.ReadPdbChecksumDebugDirectoryData)
                    .ToArray();
                if (checksumEntries.Length == 0)
                {
                    errors.Add(symbolPackagePath + ": PE '" + peEntry.FullName + "' does not contain a compiler PDB checksum.");
                    return;
                }

                var pdbReader = pdbProvider.GetMetadataReader();
                var idOffset = pdbReader.DebugMetadataHeader!.IdStartOffset;
                if (idOffset < 0 || idOffset > pdbBytes.Length - 20)
                {
                    errors.Add(symbolPackagePath + ": PDB '" + pdbEntry.FullName + "' has an invalid portable PDB ID offset.");
                    return;
                }

                foreach (var checksumEntry in checksumEntries)
                {
                    using var hash = IncrementalHash.CreateHash(new HashAlgorithmName(checksumEntry.AlgorithmName));
                    hash.AppendData(pdbBytes, 0, idOffset);
                    hash.AppendData(new byte[20]);
                    hash.AppendData(pdbBytes, idOffset + 20, pdbBytes.Length - idOffset - 20);
                    if (checksumEntry.Checksum.SequenceEqual(hash.GetHashAndReset()))
                    {
                        return;
                    }
                }

                errors.Add(symbolPackagePath + ": PDB '" + pdbEntry.FullName + "' does not match the compiler checksum in '" + peEntry.FullName + "'.");
            }
            finally
            {
                pdbProvider?.Dispose();
            }
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static bool IsDirectory(ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
