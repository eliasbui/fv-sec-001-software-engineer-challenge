namespace AdPerformance.Core.Models;

/// <summary>
/// Mutable accumulator for a single <c>campaign_id</c>. Exactly one instance is
/// kept in memory per unique campaign during the streaming aggregation pass.
/// </summary>
public sealed class CampaignStats
{
    public string CampaignId { get; }

    public long TotalImpressions { get; private set; }
    public long TotalClicks { get; private set; }
    public decimal TotalSpend { get; private set; }
    public long TotalConversions { get; private set; }

    public CampaignStats(string campaignId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        CampaignId = campaignId;
    }

    public void Add(in AdRecord record)
    {
        TotalImpressions += record.Impressions;
        TotalClicks += record.Clicks;
        TotalSpend += record.Spend;
        TotalConversions += record.Conversions;
    }

    /// <summary>
    /// Fold the totals of another <see cref="CampaignStats"/> into this one.
    /// Used by the parallel aggregator when merging per-worker shards.
    /// Both operands must share the same <see cref="CampaignId"/>.
    /// </summary>
    public void Merge(CampaignStats other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!string.Equals(CampaignId, other.CampaignId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot merge stats for different campaigns ('{CampaignId}' vs '{other.CampaignId}').");
        }

        TotalImpressions += other.TotalImpressions;
        TotalClicks += other.TotalClicks;
        TotalSpend += other.TotalSpend;
        TotalConversions += other.TotalConversions;
    }

    /// <summary>
    /// Click-through rate. Returns 0 when <see cref="TotalImpressions"/> is 0
    /// (no division by zero).
    /// </summary>
    public double Ctr => TotalImpressions == 0
        ? 0d
        : (double)TotalClicks / TotalImpressions;

    /// <summary>
    /// Cost per acquisition. Returns <c>null</c> when
    /// <see cref="TotalConversions"/> is 0 so that callers can exclude or
    /// render the value as blank.
    /// </summary>
    public double? Cpa => TotalConversions == 0
        ? null
        : (double)(TotalSpend / TotalConversions);
}
