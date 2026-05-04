using AdPerformance.Core.Abstractions;
using AdPerformance.Core.Models;

namespace AdPerformance.Core.Aggregation;

/// <summary>
/// Single-pass, in-memory aggregator. Memory is bounded by the number of
/// unique <c>campaign_id</c> values, independent of the input row count.
/// </summary>
public sealed class CampaignAggregator : ICampaignAggregator
{
    private const long ProgressReportInterval = 1_000_000;

    public async Task<IReadOnlyDictionary<string, CampaignStats>> AggregateAsync(
        IAdRecordSource source,
        IProgress<AggregationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var stats = new Dictionary<string, CampaignStats>(capacity: 128, StringComparer.Ordinal);
        long rowsProcessed = 0;

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stats.TryGetValue(record.CampaignId, out var bucket))
            {
                bucket = new CampaignStats(record.CampaignId);
                stats[record.CampaignId] = bucket;
            }
            bucket.Add(in record);

            rowsProcessed++;
            if (progress is not null && rowsProcessed % ProgressReportInterval == 0)
            {
                progress.Report(new AggregationProgress(rowsProcessed, source.BadRowCount));
            }
        }

        progress?.Report(new AggregationProgress(rowsProcessed, source.BadRowCount));
        return stats;
    }
}
