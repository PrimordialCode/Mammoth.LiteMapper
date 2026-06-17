using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class Milestone1GeneratorProjectTests
    {
        [TestMethod]
        public void GeneratorProjectKeepsRoslynPrivate()
        {
            var project = XDocument.Load(Repository.Path("src/Mammoth.LiteMapper.Generator/Mammoth.LiteMapper.Generator.csproj"));
            var references = project.Descendants("PackageReference").ToArray();

            Assert.AreEqual(1, references.Count(r => (string?)r.Attribute("Include") == "Microsoft.CodeAnalysis.CSharp"));
            Assert.IsTrue(references.Any(r =>
                (string?)r.Attribute("Include") == "Microsoft.CodeAnalysis.CSharp" &&
                (string?)r.Attribute("PrivateAssets") == "all"));
        }
    }
}
