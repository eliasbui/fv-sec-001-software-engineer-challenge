using AdPerformance.Infrastructure.Csv;
using FluentAssertions;

namespace AdPerformance.Infrastructure.Tests;

public sealed class MemoryMappedAggregatorTests : IDisposable
{
    private readonly string _tempDir;

    public MemoryMappedAggregatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adperf_mmap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteCsv(string contents)
    {
        var path = Path.Combine(_tempDir, "input.csv");
        File.WriteAllText(path, contents);
        return path;
    }

    private const string Header = "campaign_id,date,impressions,clicks,spend,conversions";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task AggregatesAcrossWorkers(int workers)
    {
        var rows = new List<string> { Header };
        for (var i = 0; i < 10_000; i++)
        {
            var id = $"CMP{i % 5:000}";
            rows.Add($"{id},2025-01-01,100,10,1.00,1");
        }
        var path = WriteCsv(string.Join('\n', rows) + '\n');

        var agg = new MemoryMappedAggregator(path, workerCount: workers);
        var result = await agg.AggregateAsync();

        result.Should().HaveCount(5);
        foreach (var stats in result.Values)
        {
            stats.TotalImpressions.Should().Be(200_000);
            stats.TotalClicks.Should().Be(20_000);
            stats.TotalSpend.Should().Be(2_000m);
            stats.TotalConversions.Should().Be(2_000);
        }
        agg.BadRowCount.Should().Be(0);
    }

    [Fact]
    public async Task DifferentWorkerCounts_ProduceIdenticalTotals()
    {
        var rows = new List<string> { Header };
        var rng = new Random(Seed: 1337);
        for (var i = 0; i < 5_000; i++)
        {
            var id = $"CMP{rng.Next(20):000}";
            rows.Add($"{id},2025-{rng.Next(1, 13):00}-{rng.Next(1, 29):00},{rng.Next(1, 10_000)},{rng.Next(0, 500)},{rng.NextDouble() * 100:F2},{rng.Next(0, 100)}");
        }
        var path = WriteCsv(string.Join('\n', rows) + '\n');

        var a1 = await new MemoryMappedAggregator(path, workerCount: 1).AggregateAsync();
        var a8 = await new MemoryMappedAggregator(path, workerCount: 8).AggregateAsync();

        a1.Keys.Should().BeEquivalentTo(a8.Keys);
        foreach (var id in a1.Keys)
        {
            a1[id].TotalImpressions.Should().Be(a8[id].TotalImpressions);
            a1[id].TotalClicks.Should().Be(a8[id].TotalClicks);
            a1[id].TotalSpend.Should().Be(a8[id].TotalSpend);
            a1[id].TotalConversions.Should().Be(a8[id].TotalConversions);
        }
    }

    [Fact]
    public async Task Skips_MalformedRows()
    {
        var csv = $"""
            {Header}
            CMP001,2025-01-01,100,10,1.00,1
            CMP002,not-a-date,100,10,1.00,1
            CMP003,2025-01-01,-1,10,1.00,1
            CMP004,2025-01-01,100,10,1.00,1
            """;
        var path = WriteCsv(csv);

        var agg = new MemoryMappedAggregator(path, workerCount: 4);
        var result = await agg.AggregateAsync();

        result.Should().HaveCount(2);
        result.Should().ContainKey("CMP001");
        result.Should().ContainKey("CMP004");
        agg.BadRowCount.Should().Be(2);
    }

    [Fact]
    public async Task HandlesCrlfLineEndings()
    {
        var csv = $"{Header}\r\nCMP001,2025-01-01,100,10,1.00,1\r\nCMP002,2025-01-01,200,20,2.00,2\r\n";
        var path = WriteCsv(csv);

        var agg = new MemoryMappedAggregator(path, workerCount: 2);
        var result = await agg.AggregateAsync();

        result.Should().HaveCount(2);
        result["CMP001"].TotalImpressions.Should().Be(100);
        result["CMP002"].TotalImpressions.Should().Be(200);
    }

    [Fact]
    public async Task FileSmallerThanWorkerCount_StillCorrect()
    {
        // 2 data rows with 16 workers → most workers get empty ranges.
        var csv = $"{Header}\nCMP001,2025-01-01,100,10,1.00,1\nCMP002,2025-01-01,200,20,2.00,2\n";
        var path = WriteCsv(csv);

        var agg = new MemoryMappedAggregator(path, workerCount: 16);
        var result = await agg.AggregateAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        var rows = new List<string> { Header };
        for (var i = 0; i < 500_000; i++)
            rows.Add("CMP001,2025-01-01,1,1,1.00,1");
        var path = WriteCsv(string.Join('\n', rows) + '\n');

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var agg = new MemoryMappedAggregator(path, workerCount: 4);
        var act = async () => await agg.AggregateAsync(cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AutoWorkerCount_IsAtLeastOne()
    {
        var path = WriteCsv($"{Header}\nCMP001,2025-01-01,1,1,1.00,1\n");
        var agg = new MemoryMappedAggregator(path, workerCount: 0);
        agg.WorkerCount.Should().BeGreaterThanOrEqualTo(1);
        await agg.AggregateAsync();
    }

    [Fact]
    public async Task EmptyFile_ReturnsEmpty()
    {
        var path = WriteCsv(string.Empty);
        var agg = new MemoryMappedAggregator(path, workerCount: 4);
        var result = await agg.AggregateAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyHeader_ReturnsEmpty()
    {
        var path = WriteCsv(Header + "\n");
        var agg = new MemoryMappedAggregator(path, workerCount: 4);
        var result = await agg.AggregateAsync();
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 7)]
    public async Task RangePartitioning_NoRowLostOrDoubled(int rowsPerCampaign, int workers)
    {
        // Exercise the byte-range boundary alignment: every row must be
        // attributed to exactly one worker.
        var rows = new List<string> { Header };
        for (var i = 0; i < rowsPerCampaign * 100; i++)
        {
            var id = $"CMP{i % 100:000}";
            rows.Add($"{id},2025-01-01,1,1,1.00,1");
        }
        var path = WriteCsv(string.Join('\n', rows) + '\n');

        var result = await new MemoryMappedAggregator(path, workerCount: workers).AggregateAsync();

        result.Should().HaveCount(100);
        foreach (var stats in result.Values)
        {
            stats.TotalImpressions.Should().Be(rowsPerCampaign);
            stats.TotalClicks.Should().Be(rowsPerCampaign);
        }
    }
}
