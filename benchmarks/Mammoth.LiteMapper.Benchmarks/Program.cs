using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Mapster;
using Mammoth.LiteMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mammoth.LiteMapper.Benchmarks
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }

    [MemoryDiagnoser]
    public class FlatObjectBenchmarks
    {
        private static readonly IMapper AutoMapperInstance = new MapperConfiguration(static cfg => cfg.CreateMap<FlatSource, FlatTarget>(), NullLoggerFactory.Instance).CreateMapper();
        private readonly FlatSource source = new FlatSource
        {
            Id = 42,
            Name = "Ada",
            Count = 7,
            IsActive = true,
        };

        [Benchmark(Baseline = true)]
        public FlatTarget Manual()
        {
            return new FlatTarget
            {
                Id = source.Id,
                Name = source.Name,
                Count = source.Count,
                IsActive = source.IsActive,
            };
        }

        [Benchmark]
        public FlatTarget LiteMapper()
        {
            return LiteMapperBenchmarkMapper.Map(source);
        }

        [Benchmark]
        public FlatTarget Mapperly()
        {
            return MapperlyBenchmarkMapper.Map(source);
        }

        [Benchmark]
        public FlatTarget Mapster()
        {
            return source.Adapt<FlatTarget>();
        }

        [Benchmark]
        public FlatTarget AutoMapper()
        {
            return AutoMapperInstance.Map<FlatTarget>(source);
        }
    }

    [LiteMapper]
    public static partial class LiteMapperBenchmarkMapper
    {
        public static partial FlatTarget Map(FlatSource source);
    }

    [Riok.Mapperly.Abstractions.Mapper]
    public static partial class MapperlyBenchmarkMapper
    {
        public static partial FlatTarget Map(FlatSource source);
    }

    public sealed class FlatSource
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public int Count { get; set; }

        public bool IsActive { get; set; }
    }

    public sealed class FlatTarget
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public int Count { get; set; }

        public bool IsActive { get; set; }
    }
}
