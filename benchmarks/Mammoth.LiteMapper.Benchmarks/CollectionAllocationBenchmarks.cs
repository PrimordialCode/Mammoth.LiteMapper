using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Mammoth.LiteMapper.Benchmarks
{
    // Compare allocation within each category against its manual baseline. Both paths
    // include the same source enumerator cost. Unknown-count materialization additionally
    // needs a growing temporary buffer and the final array, as permitted by section 23.3.
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    public class CollectionAllocationBenchmarks
    {
        private IReadOnlyCollection<int> knownCount = Array.Empty<int>();
        private IEnumerable<int> unknownCount = Array.Empty<int>();

        [Params(0, 16, 1024)]
        public int Count { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var values = Enumerable.Range(0, Count).ToArray();
            knownCount = values;
            unknownCount = Enumerate(values);

            if (!values.SequenceEqual(ManualKnownCount()) ||
                !values.SequenceEqual(LiteMapperKnownCount()) ||
                !values.SequenceEqual(ManualUnknownCount()) ||
                !values.SequenceEqual(LiteMapperUnknownCount()))
            {
                throw new InvalidOperationException("Collection benchmarks must produce the same ordered values.");
            }
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("KnownCount")]
        public int[] ManualKnownCount()
        {
            var result = new int[knownCount.Count];
            var index = 0;
            foreach (var value in knownCount)
            {
                result[index++] = value;
            }

            return result;
        }

        [Benchmark]
        [BenchmarkCategory("KnownCount")]
        public int[] LiteMapperKnownCount()
        {
            return CollectionAllocationMapper.MapKnownCount(knownCount);
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("UnknownCount")]
        public int[] ManualUnknownCount()
        {
            var buffer = new List<int>();
            foreach (var value in unknownCount)
            {
                buffer.Add(value);
            }

            return buffer.ToArray();
        }

        [Benchmark]
        [BenchmarkCategory("UnknownCount")]
        public int[] LiteMapperUnknownCount()
        {
            return CollectionAllocationMapper.MapUnknownCount(unknownCount);
        }

        private static IEnumerable<int> Enumerate(int[] values)
        {
            foreach (var value in values)
            {
                yield return value;
            }
        }
    }

    [LiteMapper]
    public static partial class CollectionAllocationMapper
    {
        public static partial int[] MapKnownCount(IReadOnlyCollection<int> source);

        public static partial int[] MapUnknownCount(IEnumerable<int> source);
    }
}
