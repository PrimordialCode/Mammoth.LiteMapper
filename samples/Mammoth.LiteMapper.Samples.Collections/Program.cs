using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Collections
{
    internal static class Program
    {
        private static void Main()
        {
            var target = CollectionMapper.Map(new OrderSource
            {
                Id = 100,
                Lines = new[]
                {
                    new LineSource { Sku = "A", Quantity = 1 },
                    new LineSource { Sku = "B", Quantity = 2 },
                },
                Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Priority" },
                QuantitiesBySku = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["A"] = 1,
                    ["B"] = 2,
                },
            });

            if (target.Id != 100 ||
                target.Lines.Count != 2 ||
                target.Lines[1].Quantity != 2 ||
                !target.Tags.Contains("priority") ||
                target.QuantitiesBySku["B"] != 2)
            {
                throw new InvalidOperationException("Collection mapping failed.");
            }
        }
    }

    [LiteMapper]
    public static partial class CollectionMapper
    {
        public static partial OrderTarget Map(OrderSource source);
    }

    public sealed class OrderSource
    {
        public int Id { get; set; }

        public LineSource[] Lines { get; set; } = new LineSource[0];

        public HashSet<string> Tags { get; set; } = new HashSet<string>();

        public Dictionary<string, int> QuantitiesBySku { get; set; } = new Dictionary<string, int>();
    }

    public sealed class OrderTarget
    {
        public int Id { get; set; }

        public List<LineTarget> Lines { get; set; } = new List<LineTarget>();

        public HashSet<string> Tags { get; set; } = new HashSet<string>();

        public Dictionary<string, int> QuantitiesBySku { get; set; } = new Dictionary<string, int>();
    }

    public sealed class LineSource
    {
        public string? Sku { get; set; }

        public int Quantity { get; set; }
    }

    public sealed class LineTarget
    {
        public string? Sku { get; set; }

        public int Quantity { get; set; }
    }
}
