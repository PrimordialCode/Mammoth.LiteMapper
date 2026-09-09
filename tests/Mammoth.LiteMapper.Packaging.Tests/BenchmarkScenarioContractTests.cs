using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class BenchmarkScenarioContractTests
    {
        [TestMethod]
        public void NestedAndRecursiveBenchmarkScenariosHaveRequiredShapeAndSetupProof()
        {
            var path = Repository.Path("benchmarks/Mammoth.LiteMapper.Benchmarks/NestedCycleAllocationBenchmarks.cs");
            var source = File.ReadAllText(path);

            Assert.AreEqual(7, Count(source, "[Benchmark]") + Count(source, "[Benchmark(Baseline = true)]"), "Seven methods expand to 15 scenarios through the three recursive depths.");
            Assert.AreEqual(3, Count(source, "[Benchmark(Baseline = true)]"));
            Assert.AreEqual(3, Count(source, "[BenchmarkCategory(\"Nested\")]"));
            Assert.AreEqual(2, Count(source, "[BenchmarkCategory(\"Recursive-None\")]"));
            Assert.AreEqual(2, Count(source, "[BenchmarkCategory(\"Recursive-ThrowOnCycle\")]"));
            StringAssert.Contains(source, "[Params(1, 16, 128)]");
            StringAssert.Contains(source, "[GlobalSetup]");
            StringAssert.Contains(source, "AssertNestedEquivalent");
            StringAssert.Contains(source, "AssertEquivalent(expected, global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveNoTracking.Map(recursiveSource))");
            StringAssert.Contains(source, "global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveNoTracking.Map(recursiveSource)");
            StringAssert.Contains(source, "global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveWithTracking.Map(recursiveSource)");
            StringAssert.Contains(source, "HashSet<NestedCycleAllocationBenchmarks.RecursiveSource>");
            Assert.IsFalse(source.Contains("source.Child = source", StringComparison.Ordinal), "Finite recursive scenarios must not execute a cyclic graph.");
        }

        private static int Count(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
