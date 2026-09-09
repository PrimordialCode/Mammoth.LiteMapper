using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Benchmarks
{
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    public class NestedCycleAllocationBenchmarks
    {
        private NestedSource nestedSource = null!;

        [GlobalSetup]
        public void Setup()
        {
            nestedSource = new NestedSource
            {
                Id = 42,
                Child = new NestedChildSource { Name = "Ada", Value = 7 },
            };
            AssertNestedEquivalent(ManualNested(), NestedMapper.Map(nestedSource));
            AssertNestedEquivalent(ManualNested(), ThrowOnCycleNestedMapper.Map(nestedSource));
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Nested")]
        public NestedTarget ManualNested() => new NestedTarget
        {
            Id = nestedSource.Id,
            Child = new NestedChildTarget { Name = nestedSource.Child.Name, Value = nestedSource.Child.Value },
        };

        [Benchmark]
        [BenchmarkCategory("Nested")]
        public NestedTarget LiteMapperNested() => NestedMapper.Map(nestedSource);

        [Benchmark]
        [BenchmarkCategory("Nested")]
        public NestedTarget LiteMapperThrowOnCycleNested() => ThrowOnCycleNestedMapper.Map(nestedSource);

        private static void AssertNestedEquivalent(NestedTarget expected, NestedTarget actual)
        {
            if (expected.Id != actual.Id || expected.Child.Name != actual.Child.Name || expected.Child.Value != actual.Child.Value)
            {
                throw new InvalidOperationException("Nested benchmark mappings are not equivalent.");
            }
        }

        public sealed class NestedSource { public int Id { get; set; } public NestedChildSource Child { get; set; } = null!; }
        public sealed class NestedChildSource { public string Name { get; set; } = string.Empty; public int Value { get; set; } }
        public sealed class NestedTarget { public int Id { get; set; } public NestedChildTarget Child { get; set; } = null!; }
        public sealed class NestedChildTarget { public string Name { get; set; } = string.Empty; public int Value { get; set; } }
        public sealed class RecursiveSource { public int Value { get; set; } public RecursiveSource? Child { get; set; } }
        public sealed class RecursiveTarget { public int Value { get; set; } public RecursiveTarget? Child { get; set; } }
    }

    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    public class RecursiveCycleAllocationBenchmarks
    {
        private NestedCycleAllocationBenchmarks.RecursiveSource recursiveSource = null!;

        [Params(1, 16, 128)]
        public int Depth { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            recursiveSource = CreateRecursiveSource(Depth);
            var expected = ManualRecursiveNoTracking();
            AssertEquivalent(expected, global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveNoTracking.Map(recursiveSource));
            AssertEquivalent(expected, ManualRecursiveWithTracking());
            AssertEquivalent(expected, global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveWithTracking.Map(recursiveSource));
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Recursive-None")]
        public NestedCycleAllocationBenchmarks.RecursiveTarget ManualRecursiveNoTracking() => MapRecursive(recursiveSource);

        [Benchmark]
        [BenchmarkCategory("Recursive-None")]
        public NestedCycleAllocationBenchmarks.RecursiveTarget LiteMapperRecursiveNoTracking() => global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveNoTracking.Map(recursiveSource);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Recursive-ThrowOnCycle")]
        public NestedCycleAllocationBenchmarks.RecursiveTarget ManualRecursiveWithTracking() => MapRecursive(recursiveSource, new HashSet<NestedCycleAllocationBenchmarks.RecursiveSource>());

        [Benchmark]
        [BenchmarkCategory("Recursive-ThrowOnCycle")]
        public NestedCycleAllocationBenchmarks.RecursiveTarget LiteMapperRecursiveWithTracking() => global::Mammoth.LiteMapper.Benchmarks.LiteMapperRecursiveWithTracking.Map(recursiveSource);

        private static NestedCycleAllocationBenchmarks.RecursiveTarget MapRecursive(NestedCycleAllocationBenchmarks.RecursiveSource source, HashSet<NestedCycleAllocationBenchmarks.RecursiveSource>? active = null)
        {
            if (active != null && !active.Add(source)) throw new InvalidOperationException("Recursive source cycle.");
            var result = new NestedCycleAllocationBenchmarks.RecursiveTarget { Value = source.Value, Child = source.Child == null ? null : MapRecursive(source.Child, active) };
            active?.Remove(source);
            return result;
        }

        private static NestedCycleAllocationBenchmarks.RecursiveSource CreateRecursiveSource(int depth)
        {
            var root = new NestedCycleAllocationBenchmarks.RecursiveSource { Value = 0 };
            var current = root;
            for (var index = 1; index < depth; index++)
            {
                current.Child = new NestedCycleAllocationBenchmarks.RecursiveSource { Value = index };
                current = current.Child;
            }
            return root;
        }

        private static void AssertEquivalent(NestedCycleAllocationBenchmarks.RecursiveTarget expected, NestedCycleAllocationBenchmarks.RecursiveTarget actual)
        {
            while (expected != null || actual != null)
            {
                if (expected == null || actual == null || expected.Value != actual.Value) throw new InvalidOperationException("Recursive benchmark mappings are not equivalent.");
                expected = expected.Child!;
                actual = actual.Child!;
            }
        }
    }

    [LiteMapper]
    public static partial class NestedMapper
    {
        public static partial NestedCycleAllocationBenchmarks.NestedTarget Map(NestedCycleAllocationBenchmarks.NestedSource source);
    }

    [LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial class ThrowOnCycleNestedMapper
    {
        public static partial NestedCycleAllocationBenchmarks.NestedTarget Map(NestedCycleAllocationBenchmarks.NestedSource source);
    }

    [LiteMapper]
    public static partial class LiteMapperRecursiveNoTracking
    {
        public static partial NestedCycleAllocationBenchmarks.RecursiveTarget Map(NestedCycleAllocationBenchmarks.RecursiveSource source);
    }

    [LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial class LiteMapperRecursiveWithTracking
    {
        public static partial NestedCycleAllocationBenchmarks.RecursiveTarget Map(NestedCycleAllocationBenchmarks.RecursiveSource source);
    }
}
