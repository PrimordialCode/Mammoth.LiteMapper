using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.IntegrationTests
{
    [TestClass]
    public sealed class Milestone1IntegrationTests
    {
        [TestMethod]
        public void PrimaryPackageProjectReferenceCompiles()
        {
            Assert.IsNotNull(Assembly.Load("Mammoth.LiteMapper"));
        }
    }
}
