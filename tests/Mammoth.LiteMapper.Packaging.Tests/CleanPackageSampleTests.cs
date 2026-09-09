using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class CleanPackageSampleTests
    {
        [TestMethod]
        public void DocumentedSamplesCompileAndExecuteFromThePrimaryPackage()
        {
            var feed = Milestone14PackagingAndAotTests.CreateFeed();
            Milestone14PackagingAndAotTests.PackAll(feed);
            var buildDefaults = XDocument.Load(Repository.Path("Directory.Build.props"));

            foreach (var sample in new[] { "Basic", "Collections", "AspNetCore" })
            {
                var projectName = "Mammoth.LiteMapper.Samples." + sample;
                var sampleDirectory = Repository.Path("samples/" + projectName);
                var sampleProject = XDocument.Load(Path.Combine(sampleDirectory, projectName + ".csproj"));
                var directory = Path.Combine(Path.GetTempPath(), "MammothLiteMapperPackageSamples", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);

                // Execute the documented source unchanged, including its mapping assertions.
                File.Copy(Path.Combine(sampleDirectory, "Program.cs"), Path.Combine(directory, "Program.cs"));
                var properties = new XElement("PropertyGroup",
                    new XElement("OutputType", "Exe"),
                    new XElement("RestorePackagesPath", Path.Combine(directory, ".packages")));
                foreach (var propertyName in new[] { "TargetFramework", "Nullable", "ImplicitUsings", "LangVersion" })
                {
                    var property = sampleProject.Descendants(propertyName).FirstOrDefault() ??
                        buildDefaults.Descendants(propertyName).FirstOrDefault();
                    if (property != null)
                    {
                        properties.Add(new XElement(property));
                    }
                }

                var project = new XDocument(new XElement("Project",
                    new XAttribute("Sdk", sampleProject.Root!.Attribute("Sdk")!.Value),
                    properties,
                    new XElement("ItemGroup", new XElement("PackageReference",
                        new XAttribute("Include", "Mammoth.LiteMapper"),
                        new XAttribute("Version", "1.0.0")))));
                project.Save(Path.Combine(directory, projectName + ".csproj"));
                new XDocument(new XElement("configuration",
                    new XElement("packageSources",
                        new XElement("clear"),
                        new XElement("add", new XAttribute("key", "LocalPackages"), new XAttribute("value", feed)))))
                    .Save(Path.Combine(directory, "NuGet.Config"));

                Milestone14PackagingAndAotTests.RunDotnet("restore --no-cache --configfile NuGet.Config", directory);
                Milestone14PackagingAndAotTests.RunDotnet("build -c Release --no-restore", directory);
                var arguments = "run -c Release --no-build --no-restore" + (sample == "AspNetCore" ? " -- --smoke" : string.Empty);
                Milestone14PackagingAndAotTests.RunDotnet(arguments, directory);

                var framework = properties.Element("TargetFramework")!.Value;
                var output = Path.Combine(directory, "bin", "Release", framework);
                Assert.IsFalse(Directory.EnumerateFiles(output, "Mammoth.LiteMapper.Generator.dll", SearchOption.AllDirectories).Any(),
                    "The documented " + sample + " sample must not acquire a generator runtime dependency.");
                Assert.IsFalse(Directory.EnumerateFiles(output, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any(),
                    "The documented " + sample + " sample must not copy Roslyn to runtime output.");
            }
        }
    }
}
