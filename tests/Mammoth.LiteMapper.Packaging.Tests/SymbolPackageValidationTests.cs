using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Mammoth.LiteMapper.PackageValidation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class SymbolPackageValidationTests
    {
        [TestMethod]
        public void MatchingPortableSymbolsAreAccepted()
        {
            using var fixture = SymbolPackageFixture.Create(pdbPath: "LIB/NET10.0/mammoth.litemapper.packaging.tests.PDB");

            AssertValid(fixture);
        }

        [TestMethod]
        public void SymbolPackageMustContainAPdb()
        {
            using var fixture = SymbolPackageFixture.Create(includePdb: false);

            AssertInvalid(fixture, "does not contain a PDB");
        }

        [TestMethod]
        public void AdditionalAssembliesDoNotRequireDuplicateSymbols()
        {
            using var fixture = SymbolPackageFixture.Create(addAssemblyWithoutSymbols: true);

            AssertValid(fixture);
        }

        [TestMethod]
        public void PdbMustUseTheAssemblyArchiveRelativePath()
        {
            using var fixture = SymbolPackageFixture.Create(pdbPath: "symbols/Mammoth.LiteMapper.Packaging.Tests.pdb");

            AssertInvalid(fixture, "same archive-relative path");
        }

        [TestMethod]
        public void PdbIdentityMustMatchTheAssembly()
        {
            using var fixture = SymbolPackageFixture.Create(pdbMutation: ChangePortablePdbIdentity);

            AssertInvalid(fixture, "does not match the PE identity");
        }

        [TestMethod]
        public void PdbChecksumMustMatchEvenWhenTheIdentityMatches()
        {
            using var fixture = SymbolPackageFixture.Create(pdbMutation: AppendByteWithoutChangingIdentity);

            AssertInvalid(fixture, "does not match the compiler checksum");
        }

        [TestMethod]
        public void PdbMustBePortable()
        {
            using var fixture = SymbolPackageFixture.Create(pdbMutation: RemovePortableSignature);

            AssertInvalid(fixture, "is not portable");
        }

        [TestMethod]
        public void AssemblyMustContainACompilerPdbChecksum()
        {
            using var fixture = SymbolPackageFixture.Create(peMutation: RemovePdbChecksumEntry);

            AssertInvalid(fixture, "does not contain a compiler PDB checksum");
        }

        [TestMethod]
        [DataRow("Other.Package", "1.2.3", "package ID")]
        [DataRow("Test.Package", "1.2.4", "package version")]
        public void SymbolIdentityMustMatchThePrimaryPackage(string symbolId, string symbolVersion, string expectedError)
        {
            using var fixture = SymbolPackageFixture.Create(symbolId: symbolId, symbolVersion: symbolVersion);

            AssertInvalid(fixture, expectedError);
        }

        [TestMethod]
        public void SymbolNuspecMustDeclareSymbolsPackage()
        {
            using var fixture = SymbolPackageFixture.Create(includeSymbolsPackageType: false);

            AssertInvalid(fixture, "SymbolsPackage");
        }

        [TestMethod]
        public void SymbolPackageRejectsUnsupportedEntryExtensions()
        {
            using var fixture = SymbolPackageFixture.Create(addUnsupportedEntry: true);

            AssertInvalid(fixture, "unsupported extension");
        }

        [TestMethod]
        public void ShippingSymbolPackagesMatchTheirBinaries()
        {
            var feed = Milestone14PackagingAndAotTests.CreateFeed();
            Milestone14PackagingAndAotTests.PackAll(feed);

            foreach (var packageId in new[]
            {
                "Mammoth.LiteMapper.Abstractions",
                "Mammoth.LiteMapper.Generator",
                "Mammoth.LiteMapper"
            })
            {
                var package = Path.Combine(feed, packageId + ".1.0.0.nupkg");
                var symbols = Path.Combine(feed, packageId + ".1.0.0.snupkg");
                var errors = SymbolPackageValidator.Validate(package, symbols);
                Assert.AreEqual(0, errors.Count, packageId + Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            AssertSymbolEntry(feed, "Mammoth.LiteMapper.Abstractions", "lib/netstandard2.0/Mammoth.LiteMapper.Abstractions.pdb");
            AssertSymbolEntry(feed, "Mammoth.LiteMapper.Generator", "analyzers/dotnet/cs/Mammoth.LiteMapper.Generator.pdb");
            AssertSymbolEntry(feed, "Mammoth.LiteMapper", "lib/netstandard2.0/Mammoth.LiteMapper.pdb");
        }

        private static void AssertSymbolEntry(string feed, string packageId, string entryPath)
        {
            using var archive = ZipFile.OpenRead(Path.Combine(feed, packageId + ".1.0.0.snupkg"));
            Assert.IsTrue(
                archive.Entries.Any(entry => string.Equals(entry.FullName, entryPath, StringComparison.OrdinalIgnoreCase)),
                packageId + " is missing " + entryPath + ".");
        }

        private static void AssertValid(SymbolPackageFixture fixture)
        {
            var errors = SymbolPackageValidator.Validate(fixture.PackagePath, fixture.SymbolPackagePath);
            Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
        }

        private static void AssertInvalid(SymbolPackageFixture fixture, string expectedError)
        {
            var errors = SymbolPackageValidator.Validate(fixture.PackagePath, fixture.SymbolPackagePath);
            Assert.AreNotEqual(0, errors.Count);
            StringAssert.Contains(string.Join(Environment.NewLine, errors), expectedError);
        }

        private static byte[] ChangePortablePdbIdentity(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.LeaveOpen);
            var offset = provider.GetMetadataReader().DebugMetadataHeader!.IdStartOffset;
            var changed = bytes.ToArray();
            changed[offset] ^= 0x01;
            return changed;
        }

        private static byte[] AppendByteWithoutChangingIdentity(byte[] bytes)
        {
            var changed = new byte[bytes.Length + 1];
            bytes.CopyTo(changed, 0);
            changed[changed.Length - 1] = 0x01;
            return changed;
        }

        private static byte[] RemovePortableSignature(byte[] bytes)
        {
            var changed = bytes.ToArray();
            changed[0] = 0;
            return changed;
        }

        private static byte[] RemovePdbChecksumEntry(byte[] bytes)
        {
            var changed = bytes.ToArray();
            using var stream = new MemoryStream(changed, true);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var directory = reader.PEHeaders.PEHeader!.DebugTableDirectory;
            var section = reader.PEHeaders.SectionHeaders.Single(header =>
                directory.RelativeVirtualAddress >= header.VirtualAddress &&
                directory.RelativeVirtualAddress < header.VirtualAddress + Math.Max(header.VirtualSize, header.SizeOfRawData));
            var tableOffset = section.PointerToRawData + directory.RelativeVirtualAddress - section.VirtualAddress;
            const int debugDirectoryEntrySize = 28;
            var checksumType = (int)DebugDirectoryEntryType.PdbChecksum;

            for (var offset = tableOffset; offset < tableOffset + directory.Size; offset += debugDirectoryEntrySize)
            {
                if (BitConverter.ToInt32(changed, offset + 12) == checksumType)
                {
                    Array.Clear(changed, offset + 12, sizeof(int));
                    return changed;
                }
            }

            Assert.Fail("The packaging test assembly does not contain a compiler PDB checksum entry.");
            return changed;
        }

        private sealed class SymbolPackageFixture : IDisposable
        {
            private SymbolPackageFixture(string directory)
            {
                Directory = directory;
                PackagePath = Path.Combine(directory, "Test.Package.1.2.3.nupkg");
                SymbolPackagePath = Path.Combine(directory, "Test.Package.1.2.3.snupkg");
            }

            internal string Directory { get; }

            internal string PackagePath { get; }

            internal string SymbolPackagePath { get; }

            internal static SymbolPackageFixture Create(
                string symbolId = "Test.Package",
                string symbolVersion = "1.2.3",
                string pdbPath = "lib/net10.0/Mammoth.LiteMapper.Packaging.Tests.pdb",
                Func<byte[], byte[]>? pdbMutation = null,
                Func<byte[], byte[]>? peMutation = null,
                bool includeSymbolsPackageType = true,
                bool includePdb = true,
                bool addAssemblyWithoutSymbols = false,
                bool addUnsupportedEntry = false)
            {
                var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperSymbolTests", Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(directory);
                var fixture = new SymbolPackageFixture(directory);
                var assemblyPath = typeof(SymbolPackageValidationTests).Assembly.Location;
                var pdbFilePath = Path.ChangeExtension(assemblyPath, ".pdb");
                var peBytes = File.ReadAllBytes(assemblyPath);
                var pdbBytes = File.ReadAllBytes(pdbFilePath);
                if (peMutation != null)
                {
                    peBytes = peMutation(peBytes);
                }

                if (pdbMutation != null)
                {
                    pdbBytes = pdbMutation(pdbBytes);
                }

                using (var package = ZipFile.Open(fixture.PackagePath, ZipArchiveMode.Create))
                {
                    WriteText(package, "Test.Package.nuspec", Nuspec("Test.Package", "1.2.3", false));
                    WriteBytes(package, "lib/net10.0/Mammoth.LiteMapper.Packaging.Tests.dll", peBytes);
                    if (addAssemblyWithoutSymbols)
                    {
                        WriteBytes(package, "analyzers/dotnet/cs/Additional.dll", peBytes);
                    }
                }

                using (var symbols = ZipFile.Open(fixture.SymbolPackagePath, ZipArchiveMode.Create))
                {
                    WriteText(symbols, "Test.Package.nuspec", Nuspec(symbolId, symbolVersion, includeSymbolsPackageType));
                    if (includePdb)
                    {
                        WriteBytes(symbols, pdbPath, pdbBytes);
                    }

                    if (addUnsupportedEntry)
                    {
                        WriteText(symbols, "content/readme.txt", "unsupported");
                    }
                }

                return fixture;
            }

            public void Dispose()
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.Delete(Directory, true);
                }
            }

            private static string Nuspec(string id, string version, bool symbolsPackage)
            {
                var packageType = symbolsPackage
                    ? "<packageTypes><packageType name=\"SymbolsPackage\" /></packageTypes>"
                    : string.Empty;
                return "<?xml version=\"1.0\"?><package><metadata><id>" + id + "</id><version>" + version + "</version>" + packageType + "</metadata></package>";
            }

            private static void WriteText(ZipArchive archive, string path, string value)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(value);
            }

            private static void WriteBytes(ZipArchive archive, string path, byte[] value)
            {
                using var stream = archive.CreateEntry(path).Open();
                stream.Write(value, 0, value.Length);
            }
        }
    }
}
