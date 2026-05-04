using System.Globalization;
using AdPerformance.Core.Models;
using AdPerformance.Infrastructure.Validation;

namespace AdPerformance.Infrastructure.Csv;

/// <summary>
/// Zero-allocation line parser for the well-known 6-column ad-performance CSV
/// schema. Unlike <see cref="AdRecordParser"/> (which works from a tokenised
/// <c>string[]</c>), this parser works directly on a raw line
/// <see cref="ReadOnlySpan{T}"/> so callers don't need to allocate per-field
/// strings. Campaign IDs are interned via
/// <see cref="Dictionary{TKey, TValue}.AlternateLookup{TAlternateKey}"/> so
/// hits allocate no strings at all.
/// </summary>
internal static class LineParser
{
    public const int ExpectedFields = 6;

    public static bool TryParse(
        ReadOnlySpan<char> line,
        Dictionary<string, string> interner,
        out AdRecord record,
        out string? reason)
    {
        record = default;

        // Find the five commas that separate the six fields. We only expect
        // unquoted simple CSV — any malformed row is rejected.
        Span<int> commas = stackalloc int[ExpectedFields - 1];
        var found = 0;
        for (var i = 0; i < line.Length && found < commas.Length; i++)
        {
            if (line[i] == ',') commas[found++] = i;
        }

        if (found != commas.Length)
        {
            reason = "unexpected column count";
            return false;
        }

        var campaignSpan = line[..commas[0]].Trim();
        var dateSpan = line[(commas[0] + 1)..commas[1]].Trim();
        var impSpan = line[(commas[1] + 1)..commas[2]].Trim();
        var clicksSpan = line[(commas[2] + 1)..commas[3]].Trim();
        var spendSpan = line[(commas[3] + 1)..commas[4]].Trim();
        var convSpan = line[(commas[4] + 1)..].Trim();

        if (campaignSpan.IsEmpty || campaignSpan.IsWhiteSpace())
        {
            reason = "blank campaign_id";
            return false;
        }

        if (!DateOnly.TryParseExact(dateSpan, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            reason = "unparseable date";
            return false;
        }

        if (!long.TryParse(impSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var impressions) ||
            !long.TryParse(clicksSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clicks) ||
            !decimal.TryParse(spendSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var spend) ||
            !long.TryParse(convSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var conversions))
        {
            reason = "unparseable numeric field";
            return false;
        }

        var campaignId = Intern(interner, campaignSpan);
        record = new AdRecord(campaignId, date, impressions, clicks, spend, conversions);

        if (!RowValidator.TryValidate(in record, out reason))
        {
            record = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Intern the campaign_id string using .NET 9+ alternate-key lookup so
    /// that cache hits perform no allocations.
    /// </summary>
    private static string Intern(Dictionary<string, string> interner, ReadOnlySpan<char> value)
    {
        var lookup = interner.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(value, out _, out var existing))
        {
            return existing;
        }

        var allocated = new string(value);
        interner[allocated] = allocated;
        return allocated;
    }

    public static void ValidateHeaderLine(ReadOnlySpan<char> header)
    {
        Span<int> commas = stackalloc int[ExpectedFields - 1];
        var found = 0;
        for (var i = 0; i < header.Length && found < commas.Length; i++)
        {
            if (header[i] == ',') commas[found++] = i;
        }
        if (found != commas.Length)
        {
            throw new InvalidDataException(
                $"Expected {ExpectedFields} header columns, got {found + 1}.");
        }

        Expect(header[..commas[0]].Trim(), "campaign_id", 0);
        Expect(header[(commas[0] + 1)..commas[1]].Trim(), "date", 1);
        Expect(header[(commas[1] + 1)..commas[2]].Trim(), "impressions", 2);
        Expect(header[(commas[2] + 1)..commas[3]].Trim(), "clicks", 3);
        Expect(header[(commas[3] + 1)..commas[4]].Trim(), "spend", 4);
        Expect(header[(commas[4] + 1)..].Trim(), "conversions", 5);

        static void Expect(ReadOnlySpan<char> actual, string expected, int col)
        {
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unexpected header at column {col}: expected '{expected}', got '{actual}'.");
            }
        }
    }
}
