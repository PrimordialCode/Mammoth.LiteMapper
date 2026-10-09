using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Basic
{
    public static class LazyCyclePathExample
    {
        public static void Run()
        {
            CheckFiniteDepthAndConverterRecovery();
            CheckDirectCycleAndRecovery();
            CheckIndirectCycle();
            CheckBridgedCollectionCycle();
            CheckSharedSiblingBranches();
        }

        private static void CheckFiniteDepthAndConverterRecovery()
        {
            var source = BuildDirectChain(256);
            var mapped = LazyCyclePathMapper.MapDirect(source);
            if (Count(mapped) != 256)
            {
                throw new InvalidOperationException("A finite recursive chain must map to its full depth.");
            }

            LazyCyclePathMapper.ThrowOnValue(256);
            try
            {
                LazyCyclePathMapper.MapDirect(source);
                throw new InvalidOperationException("The configured converter failure was not observed.");
            }
            catch (InvalidOperationException error) when (error.Message == "Expected converter failure.")
            {
            }

            if (Count(LazyCyclePathMapper.MapDirect(source)) != 256)
            {
                throw new InvalidOperationException("Mapping must succeed after a converter exception.");
            }
        }

        private static void CheckDirectCycleAndRecovery()
        {
            var source = new LazyDirectSource { Value = 7 };
            source.Next = source;
            ExpectCycle(
                () => LazyCyclePathMapper.MapDirect(source),
                typeof(LazyDirectSource),
                typeof(LazyDirectTarget),
                "MapDirect",
                "Next");

            source.Next = null;
            var mapped = LazyCyclePathMapper.MapDirect(source);
            if (mapped.Value != 7 || mapped.Next != null)
            {
                throw new InvalidOperationException("Mapping must succeed after a cycle exception.");
            }
        }

        private static void CheckIndirectCycle()
        {
            var source = new LazyIndirectASource();
            source.B = new LazyIndirectBSource { A = source };
            ExpectCycle(
                () => LazyCyclePathMapper.MapIndirect(source),
                typeof(LazyIndirectASource),
                typeof(LazyIndirectATarget),
                "MapIndirect",
                "B.A");
        }

        private static void CheckBridgedCollectionCycle()
        {
            var node = new LazyBridgeNodeSource { Value = 11 };
            node.Next = node;
            var source = new LazyBridgeRootSource
            {
                Envelope = new LazyBridgeEnvelopeSource
                {
                    Nodes = new[] { node },
                },
            };
            ExpectCycle(
                () => LazyCyclePathMapper.MapBridged(source),
                typeof(LazyBridgeNodeSource),
                typeof(LazyBridgeNodeTarget),
                "MapBridged",
                "Envelope.Nodes.Next");
        }

        private static void CheckSharedSiblingBranches()
        {
            var shared = new LazySiblingNodeSource
            {
                Value = 13,
                Next = new LazySiblingNodeSource { Value = 17 },
            };
            var mapped = LazyCyclePathMapper.MapSiblings(new LazySiblingRootSource
            {
                Left = shared,
                Right = shared,
            });
            if (mapped.Left.Value != 13 || mapped.Right.Next?.Value != 17 ||
                ReferenceEquals(mapped.Left, mapped.Right) ||
                ReferenceEquals(mapped.Left.Next, mapped.Right.Next))
            {
                throw new InvalidOperationException("Shared sibling branches must map independently without being treated as cycles.");
            }
        }

        private static LazyDirectSource BuildDirectChain(int depth)
        {
            var root = new LazyDirectSource { Value = 1 };
            var current = root;
            for (var value = 2; value <= depth; value++)
            {
                var next = new LazyDirectSource { Value = value };
                current.Next = next;
                current = next;
            }

            return root;
        }

        private static int Count(LazyDirectTarget source)
        {
            var count = 0;
            for (LazyDirectTarget? current = source; current != null; current = current.Next)
            {
                count++;
            }

            return count;
        }

        private static void ExpectCycle(
            Action map,
            Type sourceType,
            Type destinationType,
            string mappingMethod,
            string memberPath)
        {
            try
            {
                map();
            }
            catch (LiteMapperCycleException error) when (
                error.SourceType == sourceType &&
                error.DestinationType == destinationType &&
                error.MappingMethod == mappingMethod &&
                error.MemberPath == memberPath)
            {
                return;
            }

            throw new InvalidOperationException(
                "Cycle detection did not report the expected public mapping method and member path.");
        }
    }

    [LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial class LazyCyclePathMapper
    {
        private static int? throwOnValue;

        public static partial LazyDirectTarget MapDirect(LazyDirectSource source);

        public static partial LazyIndirectATarget MapIndirect(LazyIndirectASource source);

        public static partial LazyBridgeRootTarget MapBridged(LazyBridgeRootSource source);

        public static partial LazySiblingRootTarget MapSiblings(LazySiblingRootSource source);

        internal static void ThrowOnValue(int value) => throwOnValue = value;

        [MappingConverter]
        private static int MapValue(int value)
        {
            if (throwOnValue == value)
            {
                throwOnValue = null;
                throw new InvalidOperationException("Expected converter failure.");
            }

            return value;
        }
    }

    public sealed class LazyDirectSource
    {
        public int Value { get; set; }

        public LazyDirectSource? Next { get; set; }
    }

    public sealed class LazyDirectTarget
    {
        public int Value { get; set; }

        public LazyDirectTarget? Next { get; set; }
    }

    public sealed class LazyIndirectASource
    {
        public LazyIndirectBSource? B { get; set; }
    }

    public sealed class LazyIndirectBSource
    {
        public LazyIndirectASource? A { get; set; }
    }

    public sealed class LazyIndirectATarget
    {
        public LazyIndirectBTarget? B { get; set; }
    }

    public sealed class LazyIndirectBTarget
    {
        public LazyIndirectATarget? A { get; set; }
    }

    public sealed class LazyBridgeRootSource
    {
        public LazyBridgeEnvelopeSource Envelope { get; set; } = new LazyBridgeEnvelopeSource();
    }

    public sealed class LazyBridgeEnvelopeSource
    {
        public LazyBridgeNodeSource[] Nodes { get; set; } = new LazyBridgeNodeSource[0];
    }

    public sealed class LazyBridgeNodeSource
    {
        public int Value { get; set; }

        public LazyBridgeNodeSource? Next { get; set; }
    }

    public sealed class LazyBridgeRootTarget
    {
        public LazyBridgeEnvelopeTarget Envelope { get; set; } = new LazyBridgeEnvelopeTarget();
    }

    public sealed class LazyBridgeEnvelopeTarget
    {
        public List<LazyBridgeNodeTarget> Nodes { get; set; } = new List<LazyBridgeNodeTarget>();
    }

    public sealed class LazyBridgeNodeTarget
    {
        public int Value { get; set; }

        public LazyBridgeNodeTarget? Next { get; set; }
    }

    public sealed class LazySiblingRootSource
    {
        public LazySiblingNodeSource Left { get; set; } = new LazySiblingNodeSource();

        public LazySiblingNodeSource Right { get; set; } = new LazySiblingNodeSource();
    }

    public sealed class LazySiblingNodeSource
    {
        public int Value { get; set; }

        public LazySiblingNodeSource? Next { get; set; }
    }

    public sealed class LazySiblingRootTarget
    {
        public LazySiblingNodeTarget Left { get; set; } = new LazySiblingNodeTarget();

        public LazySiblingNodeTarget Right { get; set; } = new LazySiblingNodeTarget();
    }

    public sealed class LazySiblingNodeTarget
    {
        public int Value { get; set; }

        public LazySiblingNodeTarget? Next { get; set; }
    }
}
