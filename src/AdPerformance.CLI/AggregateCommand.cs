using System.Diagnostics;
using AdPerformance.Core.Aggregation;
using AdPerformance.Core.Models;
using AdPerformance.Core.Ranking;
using AdPerformance.Infrastructure.Csv;
using Microsoft.Extensions.Logging;

namespace AdPerformance.CLI;

/// <summary>
/// Orchestrates read → aggregate → rank → write. Produces two CSV files:
/// <c>top10_ctr.csv</c> and <c>top10_cpa.csv</c>. Chooses the sequential or
/// parallel aggregator based on <see cref="CliOptions.Workers"/>.
/// </summary>
public sealed class AggregateCommand
{
    private readonly ILogger<AggregateCommand> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public AggregateCommand(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AggregateCommand>();
    }

    public async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(options.InputPath))
        {
            _logger.LogError("Input file not found: {Path}", PathSanitizer.ForLog(options.InputPath));
            return ExitCodes.InputMissing;
        }

        var sw = Stopwatch.StartNew();

        IProgress<AggregationProgress>? progress = options.Verbose
            ? new Progress<AggregationProgress>(p =>
                _logger.LogInformation(
                    "Processed {Rows:N0} rows ({Bad:N0} bad)",
                    p.RowsProcessed, p.BadRowCount))
            : null;

        IReadOnlyDictionary<string, CampaignStats> stats;
        long badRows;

        try
        {
            if (options.Workers == 1)
            {
                (stats, badRows) = await RunSequentialAsync(options, progress, cancellationToken);
            }
            else
            {
                (stats, badRows) = await RunParallelAsync(options, progress, cancellationToken);
            }
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Input CSV is malformed: {Message}", ex.Message);
            return ExitCodes.FatalIo;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error while reading input");
            return ExitCodes.FatalIo;
        }

        _logger.LogInformation(
            "Aggregation complete: {Unique:N0} campaigns, {Bad:N0} bad rows, {Elapsed}",
            stats.Count, badRows, sw.Elapsed);

        if (stats.Count == 0)
        {
            _logger.LogError("No valid rows found in input.");
            return ExitCodes.AllRowsInvalid;
        }

        var results = stats.Values.Select(CampaignResult.FromStats).ToArray();
        var topCtr = TopNSelector.TopByCtrDescending(results, options.TopN);
        var topCpa = TopNSelector.TopByCpaAscending(results, options.TopN);

        var writer = new CsvResultWriter(options.OutputDirectory);
        try
        {
            await writer.WriteAsync("top10_ctr.csv", topCtr, cancellationToken);
            await writer.WriteAsync("top10_cpa.csv", topCpa, cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error while writing output");
            return ExitCodes.FatalIo;
        }

        sw.Stop();
        _logger.LogInformation(
            "Wrote {CtrCount} CTR and {CpaCount} CPA rows to {Dir} in {Elapsed}",
            topCtr.Count, topCpa.Count, PathSanitizer.ForLog(options.OutputDirectory), sw.Elapsed);

        return ExitCodes.Success;
    }

    private async Task<(IReadOnlyDictionary<string, CampaignStats>, long)> RunSequentialAsync(
        CliOptions options,
        IProgress<AggregationProgress>? progress,
        CancellationToken ct)
    {
        _logger.LogInformation("Running single-threaded aggregator.");
        var source = StreamingCsvReader.ForFile(
            options.InputPath,
            _loggerFactory.CreateLogger<StreamingCsvReader>());
        var aggregator = new CampaignAggregator();
        var stats = await aggregator.AggregateAsync(source, progress, ct);
        return (stats, source.BadRowCount);
    }

    private async Task<(IReadOnlyDictionary<string, CampaignStats>, long)> RunParallelAsync(
        CliOptions options,
        IProgress<AggregationProgress>? progress,
        CancellationToken ct)
    {
        var aggregator = new MemoryMappedAggregator(
            options.InputPath,
            workerCount: options.Workers,
            logger: _loggerFactory.CreateLogger<MemoryMappedAggregator>());

        _logger.LogInformation(
            "Running memory-mapped aggregator with {Workers} workers (file-range parallelism).",
            aggregator.WorkerCount);

        var stats = await aggregator.AggregateAsync(progress, ct);
        return (stats, aggregator.BadRowCount);
    }
}
