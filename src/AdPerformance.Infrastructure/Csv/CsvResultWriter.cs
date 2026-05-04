using System.Globalization;
using System.Text;
using AdPerformance.Core.Abstractions;
using AdPerformance.Core.Models;

namespace AdPerformance.Infrastructure.Csv;

/// <summary>
/// Writes <see cref="CampaignResult"/> rows to a CSV file with the 7-column
/// schema: campaign_id,total_impressions,total_clicks,total_spend,
/// total_conversions,CTR,CPA.
/// </summary>
public sealed class CsvResultWriter : IResultSink
{
    public const string HeaderLine =
        "campaign_id,total_impressions,total_clicks,total_spend,total_conversions,CTR,CPA";

    /// <summary>Numeric formats used for the three derived columns.</summary>
    private const string SpendFormat = "F2";
    private const string CtrFormat = "F4";
    private const string CpaFormat = "F2";

    /// <summary>BOM-less UTF-8 so consumers like Excel-on-macOS don't see garbage.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _outputDirectory;

    public CsvResultWriter(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _outputDirectory = outputDirectory;
    }

    public async Task WriteAsync(
        string fileName,
        IReadOnlyList<CampaignResult> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(rows);

        Directory.CreateDirectory(_outputDirectory);
        var path = Path.Combine(_outputDirectory, fileName);

        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 15, useAsync: true);
        await using var writer = new StreamWriter(stream, Utf8NoBom);

        await writer.WriteLineAsync(HeaderLine.AsMemory(), cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatRow(row).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string FormatRow(in CampaignResult row)
    {
        // campaign_id can't contain a comma by design, so we don't quote it.
        var cpaString = row.Cpa is { } cpa
            ? cpa.ToString(CpaFormat, CultureInfo.InvariantCulture)
            : string.Empty;

        return string.Create(CultureInfo.InvariantCulture,
            $"{row.CampaignId},{row.TotalImpressions},{row.TotalClicks},{row.TotalSpend.ToString(SpendFormat, CultureInfo.InvariantCulture)},{row.TotalConversions},{row.Ctr.ToString(CtrFormat, CultureInfo.InvariantCulture)},{cpaString}");
    }
}
