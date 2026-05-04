using System.Globalization;
using System.Runtime.CompilerServices;
using AdPerformance.Core.Abstractions;
using AdPerformance.Core.Models;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdPerformance.Infrastructure.Csv;

/// <summary>
/// Streams <see cref="AdRecord"/> values from a CSV source using CsvHelper's
/// low-level <see cref="CsvParser"/>. Reads row-by-row — memory does not grow
/// with file size. Malformed or invalid rows are counted and skipped.
/// </summary>
public sealed class StreamingCsvReader : IAdRecordSource
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        BadDataFound = null,
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
        AllowComments = false,
        DetectColumnCountChanges = false,
    };

    private readonly Func<TextReader> _textReaderFactory;
    private readonly ILogger<StreamingCsvReader> _logger;
    private readonly Dictionary<string, string> _campaignIdInterner = new(StringComparer.Ordinal);
    private long _badRowCount;

    public long BadRowCount => Interlocked.Read(ref _badRowCount);

    public StreamingCsvReader(Func<TextReader> textReaderFactory, ILogger<StreamingCsvReader>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(textReaderFactory);
        _textReaderFactory = textReaderFactory;
        _logger = logger ?? NullLogger<StreamingCsvReader>.Instance;
    }

    /// <summary>
    /// Factory that opens a file with a large sequential-scan buffer — the
    /// combination best suited for single-pass reads of large files.
    /// </summary>
    public static StreamingCsvReader ForFile(string path, ILogger<StreamingCsvReader>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new StreamingCsvReader(
            () => new StreamReader(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1 << 16, FileOptions.SequentialScan),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1 << 16),
            logger);
    }

    public async IAsyncEnumerable<AdRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = _textReaderFactory();
        using var parser = new CsvParser(reader, CsvConfig);

        if (!await parser.ReadAsync().ConfigureAwait(false))
        {
            _logger.LogWarning("Input CSV is empty (no header row found).");
            yield break;
        }

        AdRecordParser.ValidateHeader(parser.Record);

        while (await parser.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!AdRecordParser.TryParse(parser.Record, _campaignIdInterner, out var record, out var reason))
            {
                Interlocked.Increment(ref _badRowCount);
                if (reason is not null)
                {
                    _logger.LogDebug("Row {Row}: {Reason}", parser.Row, reason);
                }
                continue;
            }

            yield return record;
        }
    }
}
