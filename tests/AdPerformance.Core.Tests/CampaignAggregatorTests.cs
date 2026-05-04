using System.Runtime.CompilerServices;
using AdPerformance.Core.Abstractions;
using AdPerformance.Core.Aggregation;
using AdPerformance.Core.Models;
using FluentAssertions;

namespace AdPerformance.Core.Tests;

public sealed class CampaignAggregatorTests
{
    [Fact]
    public async Task Aggregate_GroupsByCampaignIdAndSums()
    {
        var records = new[]
        {
            new AdRecord("CMP001", new DateOnly(2025, 1, 1), 1000, 50, 10m, 5),
            new AdRecord("CMP002", new DateOnly(2025, 1, 1), 2000, 200, 40m, 20),
            new AdRecord("CMP001", new DateOnly(2025, 1, 2), 500, 25, 5m, 2),
        };
        var source = new InMemorySource(records);

        var result = await new CampaignAggregator().AggregateAsync(source);

        result.Should().HaveCount(2);
        result["CMP001"].TotalImpressions.Should().Be(1500);
        result["CMP001"].TotalClicks.Should().Be(75);
        result["CMP001"].TotalSpend.Should().Be(15m);
        result["CMP001"].TotalConversions.Should().Be(7);
        result["CMP002"].TotalImpressions.Should().Be(2000);
    }

    [Fact]
    public async Task Aggregate_EmptySource_ReturnsEmptyDictionary()
    {
        var source = new InMemorySource(Array.Empty<AdRecord>());
        var result = await new CampaignAggregator().AggregateAsync(source);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Aggregate_ReportsProgressWhenProvided()
    {
        var records = Enumerable.Range(0, 10)
            .Select(i => new AdRecord($"CMP{i % 2}", default, 1, 1, 1m, 1))
            .ToArray();
        var source = new InMemorySource(records, badRows: 3);

        var observed = new List<AggregationProgress>();
        var progress = new Progress<AggregationProgress>(observed.Add);

        await new CampaignAggregator().AggregateAsync(source, progress);

        // Final progress report is always emitted.
        // Give the progress handler a chance to flush.
        await Task.Delay(50);
        observed.Should().NotBeEmpty();
        observed[^1].RowsProcessed.Should().Be(10);
        observed[^1].BadRowCount.Should().Be(3);
    }

    [Fact]
    public async Task Aggregate_HonoursCancellation()
    {
        var source = new InMemorySource(Enumerable.Range(0, 100_000)
            .Select(_ => new AdRecord("CMP001", default, 1, 1, 1m, 1))
            .ToArray());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
            await new CampaignAggregator().AggregateAsync(source, cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class InMemorySource : IAdRecordSource
    {
        private readonly IReadOnlyList<AdRecord> _records;
        public long BadRowCount { get; }

        public InMemorySource(IReadOnlyList<AdRecord> records, long badRows = 0)
        {
            _records = records;
            BadRowCount = badRows;
        }

        public async IAsyncEnumerable<AdRecord> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var r in _records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return r;
                await Task.Yield();
            }
        }
    }
}
