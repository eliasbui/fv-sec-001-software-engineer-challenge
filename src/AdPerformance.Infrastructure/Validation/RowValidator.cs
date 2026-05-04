using AdPerformance.Core.Models;

namespace AdPerformance.Infrastructure.Validation;

/// <summary>
/// Lightweight sanity checks that run once per parsed row. Any violation turns
/// the row into a "bad row" which is counted and skipped by the reader.
/// </summary>
public static class RowValidator
{
    public static bool TryValidate(in AdRecord record, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(record.CampaignId))
        {
            reason = "campaign_id is blank";
            return false;
        }
        if (record.Impressions < 0)
        {
            reason = "impressions is negative";
            return false;
        }
        if (record.Clicks < 0)
        {
            reason = "clicks is negative";
            return false;
        }
        if (record.Spend < 0m)
        {
            reason = "spend is negative";
            return false;
        }
        if (record.Conversions < 0)
        {
            reason = "conversions is negative";
            return false;
        }
        if (record.Clicks > record.Impressions)
        {
            reason = "clicks exceeds impressions";
            return false;
        }

        reason = null;
        return true;
    }
}
