using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Collections
{
    public static class PortableSetExample
    {
        public static void Run()
        {
            var array = new[] { 1, 2, 1 };
            var list = new List<int>(array);
            Check(PortableSetMapper.ArrayToHashSet(array), array);
            Check(PortableSetMapper.ArrayToSet(array), array);
            Check(PortableSetMapper.ListToHashSet(list), list);
            Check(PortableSetMapper.ListToSet(list), list);
#if !NETSTANDARD2_0
            Check(PortableSetMapper.ArrayToReadOnlySet(array), array);
            Check(PortableSetMapper.ListToReadOnlySet(list), list);
#endif
            var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
            CheckComparer(PortableSetMapper.CopyHashSet(source), source);
            CheckComparer(PortableSetMapper.CopySet(source), source);
#if !NETSTANDARD2_0
            CheckComparer(PortableSetMapper.CopyReadOnlySet(source), source);
#endif
            if (PortableSetMapper.ArrayToHashSet(Array.Empty<int>()).Count != 0)
            {
                throw new InvalidOperationException("An empty source must produce an empty set.");
            }
        }

        private static void Check(object mapped, object source)
        {
            if (!(mapped is HashSet<long> set) || !set.SetEquals(new long[] { 1, 2 }) ||
                ReferenceEquals(source, mapped) || !ReferenceEquals(set.Comparer, EqualityComparer<long>.Default))
            {
                throw new InvalidOperationException("Converted sets must preserve contents and use independent default-comparer collections.");
            }
        }

        private static void CheckComparer(object mapped, HashSet<string> source)
        {
            if (!(mapped is HashSet<string> set) || !set.Contains("a") ||
                ReferenceEquals(source, mapped) || !ReferenceEquals(set.Comparer, source.Comparer))
            {
                throw new InvalidOperationException("Compatible set copies must preserve the comparer without aliasing.");
            }

            set.Add("B");
            if (source.Count != 1 || set.Count != 2)
            {
                throw new InvalidOperationException("Changing a set copy must not change its source.");
            }
        }
    }

    [LiteMapper]
    public static partial class PortableSetMapper
    {
        public static partial HashSet<long> ArrayToHashSet(int[] source);
        public static partial ISet<long> ArrayToSet(int[] source);
        public static partial HashSet<long> ListToHashSet(List<int> source);
        public static partial ISet<long> ListToSet(List<int> source);
        public static partial HashSet<string> CopyHashSet(HashSet<string> source);
        public static partial ISet<string> CopySet(HashSet<string> source);
#if !NETSTANDARD2_0
        public static partial IReadOnlySet<long> ArrayToReadOnlySet(int[] source);
        public static partial IReadOnlySet<long> ListToReadOnlySet(List<int> source);
        public static partial IReadOnlySet<string> CopyReadOnlySet(HashSet<string> source);
#endif
    }
}
