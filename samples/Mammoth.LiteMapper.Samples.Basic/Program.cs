using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Basic
{
    internal static class Program
    {
        private static void Main()
        {
            var staticTarget = StaticMapper.Map(new StaticSource
            {
                Name = "Ada",
                Children = new[]
                {
                    new ChildSource { Value = 1 },
                    new ChildSource { Value = 2 },
                },
            });
            if (staticTarget.Name != "Ada" || staticTarget.Children.Count != 2 || staticTarget.Children[1].Value != 2)
            {
                throw new InvalidOperationException("Static mapping failed.");
            }

            var instanceTarget = new InstanceMapper().Map(new InstanceSource { Id = 42 });
            if (instanceTarget.Id != 42)
            {
                throw new InvalidOperationException("Instance mapping failed.");
            }

            var node = new NodeSource();
            node.Next = node;
            try
            {
                CycleMapper.Map(node);
                throw new InvalidOperationException("Cycle detection failed.");
            }
            catch (LiteMapperCycleException)
            {
            }
        }
    }

    [LiteMapper]
    public static partial class StaticMapper
    {
        public static partial StaticTarget Map(StaticSource source);
    }

    [LiteMapper]
    public sealed partial class InstanceMapper
    {
        public partial InstanceTarget Map(InstanceSource source);
    }

    [LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial class CycleMapper
    {
        public static partial NodeTarget Map(NodeSource source);
    }

    public sealed class StaticSource
    {
        public string? Name { get; set; }

        public ChildSource[] Children { get; set; } = new ChildSource[0];
    }

    public sealed class StaticTarget
    {
        public string? Name { get; set; }

        public List<ChildTarget> Children { get; set; } = new List<ChildTarget>();
    }

    public sealed class ChildSource
    {
        public int Value { get; set; }
    }

    public sealed class ChildTarget
    {
        public int Value { get; set; }
    }

    public sealed class InstanceSource
    {
        public int Id { get; set; }
    }

    public sealed class InstanceTarget
    {
        public int Id { get; set; }
    }

    public sealed class NodeSource
    {
        public NodeSource? Next { get; set; }
    }

    public sealed class NodeTarget
    {
        public NodeTarget? Next { get; set; }
    }
}
