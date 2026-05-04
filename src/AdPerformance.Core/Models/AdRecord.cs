namespace AdPerformance.Core.Models;

/// <summary>
/// A single parsed row from the input CSV. Immutable value type to minimise
/// per-row allocations during streaming aggregation.
/// </summary>
public readonly record struct AdRecord(
    string CampaignId,
    DateOnly Date,
    long Impressions,
    long Clicks,
    decimal Spend,
    long Conversions);
