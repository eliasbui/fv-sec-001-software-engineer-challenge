using System.Globalization;
using AdPerformance.Core.Models;
using AdPerformance.Infrastructure.Validation;

namespace AdPerformance.Infrastructure.Csv;

/// <summary>
/// Stateless field → <see cref="AdRecord"/> parsing. Pulled out so both the
/// single-threaded <see cref="StreamingCsvReader"/> and the multi-threaded
/// <see cref="ParallelCsvAggregator"/> can share one implementation.
/// </summary>
internal static class AdRecordParser
{
    public const int ColCampaignId = 0;
    public const int ColDate = 1;
    public const int ColImpressions = 2;
    public const int ColClicks = 3;
    public const int ColSpend = 4;
    public const int ColConversions = 5;
    public const int ExpectedColumns = 6;

    /// <summary>
    /// Parse a tokenised CSV row into an <see cref="AdRecord"/>. The
    /// <paramref name="interner"/> dictionary is mutated to pool
    /// <c>campaign_id</c> strings — pass a per-thread instance in parallel
    /// contexts to avoid locking.
    /// </summary>
    public static bool TryParse(
        string[]? fields,
        Dictionary<string, string> interner,
        out AdRecord record,
        out string? reason)
    {
        record = default;
        if (fields is null || fields.Length < ExpectedColumns)
        {
            reason = "unexpected column count";
            return false;
        }

        var campaignIdRaw = fields[ColCampaignId];
        if (string.IsNullOrWhiteSpace(campaignIdRaw))
        {
            reason = "blank campaign_id";
            return false;
        }

        if (!DateOnly.TryParseExact(fields[ColDate], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            reason = "unparseable date";
            return false;
        }

        if (!long.TryParse(fields[ColImpressions], NumberStyles.Integer, CultureInfo.InvariantCulture, out var impressions) ||
            !long.TryParse(fields[ColClicks], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clicks) ||
            !decimal.TryParse(fields[ColSpend], NumberStyles.Float, CultureInfo.InvariantCulture, out var spend) ||
            !long.TryParse(fields[ColConversions], NumberStyles.Integer, CultureInfo.InvariantCulture, out var conversions))
        {
            reason = "unparseable numeric field";
            return false;
        }

        var campaignId = Intern(interner, campaignIdRaw);
        record = new AdRecord(campaignId, date, impressions, clicks, spend, conversions);

        if (!RowValidator.TryValidate(in record, out reason))
        {
            record = default;
            return false;
        }

        return true;
    }

    public static void ValidateHeader(string[]? header)
    {
        if (header is null || header.Length < ExpectedColumns)
        {
            throw new InvalidDataException(
                $"Expected at least {ExpectedColumns} header columns, got {header?.Length ?? 0}.");
        }

        Expect(header[ColCampaignId], "campaign_id", ColCampaignId);
        Expect(header[ColDate], "date", ColDate);
        Expect(header[ColImpressions], "impressions", ColImpressions);
        Expect(header[ColClicks], "clicks", ColClicks);
        Expect(header[ColSpend], "spend", ColSpend);
        Expect(header[ColConversions], "conversions", ColConversions);

        static void Expect(string actual, string expected, int col)
        {
            if (!string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unexpected header at column {col}: expected '{expected}', got '{actual}'.");
            }
        }
    }

    private static string Intern(Dictionary<string, string> interner, string value)
    {
        if (interner.TryGetValue(value, out var existing)) return existing;
        interner[value] = value;
        return value;
    }
}
