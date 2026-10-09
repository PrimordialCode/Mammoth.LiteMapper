using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Collections
{
    public static class DictionaryComparerExample
    {
        public static void Run()
        {
            var numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 7 };
            CheckWidened(DictionaryComparerMapper.WidenToDictionary(numbers), numbers);
            CheckWidened(DictionaryComparerMapper.WidenToInterface(numbers), numbers);
            CheckWidened(DictionaryComparerMapper.WidenToReadOnlyDictionary(numbers), numbers);

            var values = new Dictionary<string, DictionaryValueSource>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = new DictionaryValueSource { Value = 7 },
            };
            CheckStructural(DictionaryComparerMapper.MapToDictionary(values), values);
            CheckStructural(DictionaryComparerMapper.MapToInterface(values), values);
            CheckStructural(DictionaryComparerMapper.MapToReadOnlyDictionary(values), values);

            CheckConvertedKeys(ConvertedDictionaryKeyMapper.ToDictionary);
            CheckConvertedKeys(ConvertedDictionaryKeyMapper.ToInterface);
            CheckConvertedKeys(ConvertedDictionaryKeyMapper.ToReadOnlyDictionary);
        }

        private static void CheckWidened(object mapped, Dictionary<string, int> source)
        {
            if (!(mapped is Dictionary<string, long> dictionary) || dictionary.Count != 1 ||
                !dictionary.TryGetValue("a", out var value) || value != 7 ||
                ReferenceEquals(source, mapped) || !ReferenceEquals(dictionary.Comparer, source.Comparer))
            {
                throw new InvalidOperationException("Widening dictionary values must preserve the key comparer without aliasing.");
            }

            dictionary["A"] = 9;
            dictionary.Add("B", 2);
            if (source.Count != 1 || source["A"] != 7 || dictionary.Count != 2)
            {
                throw new InvalidOperationException("Changing a widened dictionary must not change its source.");
            }
        }

        private static void CheckStructural(object mapped, Dictionary<string, DictionaryValueSource> source)
        {
            if (!(mapped is Dictionary<string, DictionaryValueTarget> dictionary) || dictionary.Count != 1 ||
                !dictionary.TryGetValue("a", out var value) || value.Value != 7 ||
                ReferenceEquals(source, mapped) || ReferenceEquals(source["A"], value) ||
                !ReferenceEquals(dictionary.Comparer, source.Comparer))
            {
                throw new InvalidOperationException("Structural dictionary values must preserve the key comparer without aliasing.");
            }

            value.Value = 9;
            dictionary.Add("B", new DictionaryValueTarget { Value = 2 });
            if (source.Count != 1 || source["A"].Value != 7 || dictionary.Count != 2)
            {
                throw new InvalidOperationException("Changing a mapped dictionary or value must not change its source.");
            }
        }

        private static void CheckConvertedKeys(Func<Dictionary<string, int>, object> map)
        {
            var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 7 };
            var mapped = map(source);
            if (!(mapped is Dictionary<string, int> dictionary) || dictionary.Count != 1 ||
                !dictionary.TryGetValue("a", out var value) || value != 7 || dictionary.ContainsKey("A") ||
                ReferenceEquals(source, mapped) || !ReferenceEquals(dictionary.Comparer, EqualityComparer<string>.Default))
            {
                throw new InvalidOperationException("Same-type converted keys must use the destination default comparer.");
            }

            dictionary.Add("A", 9);
            if (source.Count != 1 || source["A"] != 7 || dictionary.Count != 2)
            {
                throw new InvalidOperationException("Converted keys must allow distinct default-comparer keys without changing the source.");
            }

            var colliding = new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 1, ["a"] = 2 };
            try
            {
                map(colliding);
            }
            catch (ArgumentException)
            {
                return;
            }

            throw new InvalidOperationException("Converted dictionary key collisions must throw using Add semantics.");
        }
    }

    [LiteMapper]
    public static partial class DictionaryComparerMapper
    {
        public static partial Dictionary<string, long> WidenToDictionary(Dictionary<string, int> source);
        public static partial IDictionary<string, long> WidenToInterface(Dictionary<string, int> source);
        public static partial IReadOnlyDictionary<string, long> WidenToReadOnlyDictionary(Dictionary<string, int> source);
        public static partial Dictionary<string, DictionaryValueTarget> MapToDictionary(Dictionary<string, DictionaryValueSource> source);
        public static partial IDictionary<string, DictionaryValueTarget> MapToInterface(Dictionary<string, DictionaryValueSource> source);
        public static partial IReadOnlyDictionary<string, DictionaryValueTarget> MapToReadOnlyDictionary(Dictionary<string, DictionaryValueSource> source);
    }

    [LiteMapper]
    public static partial class ConvertedDictionaryKeyMapper
    {
        public static partial Dictionary<string, int> ToDictionary(Dictionary<string, int> source);
        public static partial IDictionary<string, int> ToInterface(Dictionary<string, int> source);
        public static partial IReadOnlyDictionary<string, int> ToReadOnlyDictionary(Dictionary<string, int> source);

        [MappingConverter]
        private static string NormalizeKey(string key) => key.ToLowerInvariant();
    }

    public sealed class DictionaryValueSource
    {
        public int Value { get; set; }
    }

    public sealed class DictionaryValueTarget
    {
        public long Value { get; set; }
    }
}
