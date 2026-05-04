namespace AdPerformance.Core.Models;

/// <summary>
/// Immutable projection of <see cref="CampaignStats"/> suitable for ranking
/// and output. <see cref="Cpa"/> is <c>null</c> when there are zero
/// conversions.
/// </summary>
public readonly record struct CampaignResult(
    string CampaignId,
    long TotalImpressions,
    long TotalClicks,
    decimal TotalSpend,
    long TotalConversions,
    double Ctr,
    double? Cpa)
{
    public static CampaignResult FromStats(CampaignStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new CampaignResult(
            stats.CampaignId,
            stats.TotalImpressions,
            stats.TotalClicks,
            stats.TotalSpend,
            stats.TotalConversions,
            stats.Ctr,
            stats.Cpa);
    }
}
