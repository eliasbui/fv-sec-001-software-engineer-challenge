using AdPerformance.Core.Aggregation;
using AdPerformance.Infrastructure.Csv;
using BenchmarkDotNet.Attributes;

namespace AdPerformance.Benchmarks;

/// <summary>
/// Measures throughput and allocation of the aggregation pipeline against
/// synthetic CSVs of varying size, comparing the single-threaded
/// <see cref="CampaignAggregator"/> to the memory-mapped, byte-range
/// <see cref="MemoryMappedAggregator"/> at different worker counts.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 1)]
public class AggregationBenchmarks
{
    private string _inputPath = string.Empty;

    [Params(1_000_000L, 10_000_000L)]
    public long RowCount { get; set; }

    [Params(1, 4, 8)]
    public int Workers { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _inputPath = Path.Combine(Path.GetTempPath(), $"adperf_bench_{RowCount}.csv");
        if (!File.Exists(_inputPath))
        {
            SyntheticCsv.Write(_inputPath, RowCount);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_inputPath)) File.Delete(_inputPath);
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Sequential()
    {
        if (Workers != 1) return 0; // only meaningful to run baseline once per RowCount
        var source = StreamingCsvReader.ForFile(_inputPath);
        var aggregator = new CampaignAggregator();
        var stats = await aggregator.AggregateAsync(source);
        return stats.Count;
    }

    [Benchmark]
    public async Task<int> MemoryMapped()
    {
        var aggregator = new MemoryMappedAggregator(_inputPath, workerCount: Workers);
        var stats = await aggregator.AggregateAsync();
        return stats.Count;
    }
}
