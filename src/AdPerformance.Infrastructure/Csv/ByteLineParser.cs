using System.Buffers.Text;
using System.Text;
using AdPerformance.Core.Models;
using AdPerformance.Infrastructure.Validation;

namespace AdPerformance.Infrastructure.Csv;

/// <summary>
/// Zero-allocation, byte-level parser for the 6-column ad-performance CSV
/// schema. Used by <see cref="MemoryMappedAggregator"/> so worker tasks can
/// parse straight out of the memory-mapped file region with no UTF-8 → char
/// decoding and no per-field string allocations.
///
/// Integer / decimal fields go through <see cref="Utf8Parser"/> which is
/// SIMD-optimised in recent .NET. Dates are parsed manually against the
/// fixed <c>yyyy-MM-dd</c> layout — faster than <see cref="DateOnly.TryParseExact"/>
/// on spans.
///
/// The only allocation this parser performs is on an interner cache miss:
/// one <see cref="string"/> per unique <c>campaign_id</c>. With ~50 unique
/// campaigns, that means about 50 total string allocations for an entire 1 GB
/// file.
/// </summary>
internal static class ByteLineParser
{
    public const int ExpectedFields = 6;
    private const byte Comma = (byte)',';
    private const byte Dash = (byte)'-';
    private const byte Cr = (byte)'\r';

    public static bool TryParse(
        ReadOnlySpan<byte> line,
        Dictionary<string, string> interner,
        out AdRecord record,
        out string? reason)
    {
        record = default;

        // Tolerate a trailing CR from CRLF line endings.
        if (line.Length > 0 && line[^1] == Cr)
        {
            line = line[..^1];
        }

        // Locate the 5 commas that separate 6 fields.
        Span<int> commas = stackalloc int[ExpectedFields - 1];
        var found = 0;
        for (var i = 0; i < line.Length && found < commas.Length; i++)
        {
            if (line[i] == Comma) commas[found++] = i;
        }
        if (found != commas.Length)
        {
            reason = "unexpected column count";
            return false;
        }

        var campaignBytes = line[..commas[0]];
        var dateBytes = line[(commas[0] + 1)..commas[1]];
        var impBytes = line[(commas[1] + 1)..commas[2]];
        var clicksBytes = line[(commas[2] + 1)..commas[3]];
        var spendBytes = line[(commas[3] + 1)..commas[4]];
        var convBytes = line[(commas[4] + 1)..];

        if (campaignBytes.IsEmpty)
        {
            reason = "blank campaign_id";
            return false;
        }

        if (!TryParseIsoDate(dateBytes, out var date))
        {
            reason = "unparseable date";
            return false;
        }

        if (!Utf8Parser.TryParse(impBytes, out long impressions, out var impConsumed) || impConsumed != impBytes.Length ||
            !Utf8Parser.TryParse(clicksBytes, out long clicks, out var clicksConsumed) || clicksConsumed != clicksBytes.Length ||
            !Utf8Parser.TryParse(spendBytes, out decimal spend, out var spendConsumed) || spendConsumed != spendBytes.Length ||
            !Utf8Parser.TryParse(convBytes, out long conversions, out var convConsumed) || convConsumed != convBytes.Length)
        {
            reason = "unparseable numeric field";
            return false;
        }

        var campaignId = InternUtf8(interner, campaignBytes);
        record = new AdRecord(campaignId, date, impressions, clicks, spend, conversions);

        if (!RowValidator.TryValidate(in record, out reason))
        {
            record = default;
            return false;
        }

        return true;
    }

    public static void ValidateHeaderBytes(ReadOnlySpan<byte> header)
    {
        if (header.Length > 0 && header[^1] == Cr) header = header[..^1];

        Span<int> commas = stackalloc int[ExpectedFields - 1];
        var found = 0;
        for (var i = 0; i < header.Length && found < commas.Length; i++)
        {
            if (header[i] == Comma) commas[found++] = i;
        }
        if (found != commas.Length)
        {
            throw new InvalidDataException(
                $"Expected {ExpectedFields} header columns, got {found + 1}.");
        }

        Check(header[..commas[0]], "campaign_id"u8, 0);
        Check(header[(commas[0] + 1)..commas[1]], "date"u8, 1);
        Check(header[(commas[1] + 1)..commas[2]], "impressions"u8, 2);
        Check(header[(commas[2] + 1)..commas[3]], "clicks"u8, 3);
        Check(header[(commas[3] + 1)..commas[4]], "spend"u8, 4);
        Check(header[(commas[4] + 1)..], "conversions"u8, 5);

        static void Check(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, int col)
        {
            // Case-insensitive ASCII match — header names are always ASCII.
            if (actual.Length != expected.Length)
            {
                throw new InvalidDataException(
                    $"Unexpected header at column {col}: expected '{Encoding.ASCII.GetString(expected)}', got '{Encoding.UTF8.GetString(actual)}'.");
            }
            for (var i = 0; i < actual.Length; i++)
            {
                var a = actual[i]; var e = expected[i];
                if (a >= (byte)'A' && a <= (byte)'Z') a = (byte)(a + 32);
                if (e >= (byte)'A' && e <= (byte)'Z') e = (byte)(e + 32);
                if (a != e)
                {
                    throw new InvalidDataException(
                        $"Unexpected header at column {col}: expected '{Encoding.ASCII.GetString(expected)}', got '{Encoding.UTF8.GetString(actual)}'.");
                }
            }
        }
    }

    /// <summary>
    /// Fast <c>yyyy-MM-dd</c> parser. Avoids the culture-aware machinery in
    /// <see cref="DateOnly.TryParseExact"/>.
    /// </summary>
    private static bool TryParseIsoDate(ReadOnlySpan<byte> span, out DateOnly date)
    {
        date = default;
        if (span.Length != 10 || span[4] != Dash || span[7] != Dash) return false;
        if (!Utf8Parser.TryParse(span[..4], out int year, out var yearConsumed) || yearConsumed != 4) return false;
        if (!Utf8Parser.TryParse(span[5..7], out int month, out var monthConsumed) || monthConsumed != 2) return false;
        if (!Utf8Parser.TryParse(span[8..10], out int day, out var dayConsumed) || dayConsumed != 2) return false;
        if ((uint)(month - 1) >= 12 || (uint)(day - 1) >= 31) return false;
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Intern a UTF-8 byte slice as a string, using
    /// <see cref="Dictionary{TKey, TValue}.AlternateLookup{TAlternateKey}"/>
    /// over <see cref="ReadOnlySpan{T}"/> of char so that cache hits allocate
    /// zero strings.
    /// </summary>
    private static string InternUtf8(Dictionary<string, string> interner, ReadOnlySpan<byte> bytes)
    {
        // Campaign IDs in the supplied data are ~6-10 ASCII chars. Allocate a
        // generous stack buffer; fall back to the heap only for pathologically
        // long values, which should never appear in a valid row.
        const int StackBufSize = 256;
        Span<char> charBuf = bytes.Length <= StackBufSize
            ? stackalloc char[StackBufSize]
            : new char[bytes.Length];

        int charCount;
        if (IsAsciiOnly(bytes))
        {
            for (var i = 0; i < bytes.Length; i++) charBuf[i] = (char)bytes[i];
            charCount = bytes.Length;
        }
        else
        {
            charCount = Encoding.UTF8.GetChars(bytes, charBuf);
        }

        var key = charBuf[..charCount];

        var lookup = interner.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(key, out _, out var existing))
        {
            return existing;
        }

        var allocated = new string(key);
        interner[allocated] = allocated;
        return allocated;
    }

    private static bool IsAsciiOnly(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] >= 0x80) return false;
        }
        return true;
    }
}
