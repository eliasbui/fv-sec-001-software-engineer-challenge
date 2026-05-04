using AdPerformance.Core.Abstractions;
using AdPerformance.Core.Models;

namespace AdPerformance.Core.Aggregation;

public interface ICampaignAggregator
{
    /// <summary>
    /// Consume the record stream and return an aggregated snapshot keyed by
    /// <c>campaign_id</c>.
    /// </summary>
    Task<IReadOnlyDictionary<string, CampaignStats>> AggregateAsync(
        IAdRecordSource source,
        IProgress<AggregationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct AggregationProgress(long RowsProcessed, long BadRowCount);
