using System.IO.MemoryMappedFiles;
using AdPerformance.Core.Aggregation;
using AdPerformance.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdPerformance.Infrastructure.Csv;

/// <summary>
/// Highest-throughput aggregator. Maps the input file into virtual memory
/// with <see cref="MemoryMappedFile"/>, splits the file into N contiguous
/// byte-ranges, and runs one worker per range. Each worker parses its slice
/// directly from the shared read-only mapping with no I/O syscalls after
/// setup, no copying, and no UTF-8 → char decoding in the hot path.
///
/// <para>Memory footprint is independent of file size:</para>
/// <list type="bullet">
///   <item>The mapped region is virtual — the OS pages in only the ranges
///         a worker actually reads, and evicts cold pages under pressure.
///         The resident-set size is bounded by the OS, not by the file
///         size.</item>
///   <item>The only per-worker heap is a <c>Dictionary&lt;string, CampaignStats&gt;</c>
///         sized to the number of unique campaigns (≪ rows).</item>
///   <item>No line strings are allocated. Parsing happens in-place on
///         <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</item>
/// </list>
///
/// <para>Correctness notes:</para>
/// <list type="bullet">
///   <item>Each worker is assigned a closed byte range <c>[start, end)</c>.
///         Except the first, it skips to the character just past the next
///         <c>'\n'</c> at or after <c>start</c> — the partial line that
///         started before <c>start</c> belongs to the previous worker.</item>
///   <item>Each worker keeps reading past <c>end</c> until it hits the
///         first <c>'\n'</c> at or after <c>end</c>, so that the last line
///         whose opening byte sits in <c>[start, end)</c> is always
///         consumed in full.</item>
///   <item>Worker 0 additionally skips the header row by advancing past the
///         first <c>'\n'</c> in the file.</item>
/// </list>
///
/// <para>Parallel output is byte-for-byte identical to the single-threaded
/// path — verified by integration tests on the supplied fixture and by
/// <c>diff</c> against the <c>--workers 1</c> run on the real 1 GB input.</para>
/// </summary>
public sealed class MemoryMappedAggregator
{
    private readonly string _filePath;
    private readonly int _workerCount;
    private readonly ILogger _logger;

    public MemoryMappedAggregator(string filePath, int workerCount = 0, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _workerCount = workerCount <= 0
            ? Math.Max(1, Environment.ProcessorCount - 1)
            : workerCount;
        _logger = logger ?? NullLogger.Instance;
    }

    public int WorkerCount => _workerCount;
    public long BadRowCount { get; private set; }

    public async Task<IReadOnlyDictionary<string, CampaignStats>> AggregateAsync(
        IProgress<AggregationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileSize = new FileInfo(_filePath).Length;
        if (fileSize == 0)
        {
            _logger.LogWarning("Input file is empty.");
            return new Dictionary<string, CampaignStats>(StringComparer.Ordinal);
        }

        // A single mapping with an accessor spanning the whole file. On a
        // 64-bit OS the virtual address space is effectively unlimited; the
        // physical pages are demand-loaded by the kernel.
        using var mmf = MemoryMappedFile.CreateFromFile(
            _filePath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);

        // Determine worker ranges across the whole file (worker 0 handles the
        // header-skip inside its loop).
        var ranges = ComputeRanges(fileSize, _workerCount);

        var workers = new Task<WorkerResult>[_workerCount];
        for (var i = 0; i < _workerCount; i++)
        {
            var (start, end) = ranges[i];
            var isFirst = i == 0;
            workers[i] = Task.Run(
                () => ProcessRange(mmf, fileSize, start, end, isFirst, cancellationToken),
                cancellationToken);
        }

        var results = await Task.WhenAll(workers).ConfigureAwait(false);

        long rowsProcessed = 0;
        foreach (var r in results) rowsProcessed += r.RowsProcessed;
        progress?.Report(new AggregationProgress(rowsProcessed, results.Sum(r => r.BadRowCount)));

        return MergeShards(results);
    }

    internal static (long Start, long End)[] ComputeRanges(long fileSize, int workerCount)
    {
        var ranges = new (long, long)[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            var start = (long)((double)i * fileSize / workerCount);
            var end = (long)((double)(i + 1) * fileSize / workerCount);
            ranges[i] = (start, end);
        }
        return ranges;
    }

    private static unsafe WorkerResult ProcessRange(
        MemoryMappedFile mmf,
        long fileSize,
        long rangeStart,
        long rangeEnd,
        bool isFirst,
        CancellationToken ct)
    {
        // Each worker takes its own view accessor. Views are cheap wrappers
        // around the shared mapping; they do not duplicate the underlying
        // physical pages.
        using var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);
        byte* basePtr = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

        try
        {
            // Account for views that may be offset from the file start
            // (Windows aligns views to an allocation-granularity boundary).
            var viewOffset = accessor.PointerOffset;
            byte* filePtr = basePtr + viewOffset;

            long readStart = rangeStart;

            // Worker 0 must also skip the header row.
            if (isFirst)
            {
                readStart = FindNewline(filePtr, 0, fileSize);
                if (readStart < 0) return WorkerResult.Empty;
                readStart++;
            }
            // Workers > 0 advance to the character just past the next '\n'.
            else
            {
                // If start == 0 already sits at a line boundary we still
                // skip, because the first line is the header which was
                // consumed by worker 0.
                var firstNewline = FindNewline(filePtr, rangeStart, fileSize);
                if (firstNewline < 0)
                {
                    // No newline in or after our range — nothing to process.
                    return WorkerResult.Empty;
                }
                readStart = firstNewline + 1;
            }

            // Extend the range out to the first newline at or after rangeEnd
            // so we fully consume the last line that begins inside our range.
            long readEnd;
            if (rangeEnd >= fileSize)
            {
                readEnd = fileSize;
            }
            else
            {
                var boundary = FindNewline(filePtr, rangeEnd, fileSize);
                readEnd = boundary < 0 ? fileSize : boundary;
            }

            if (readStart >= readEnd)
            {
                return WorkerResult.Empty;
            }

            var shard = new Dictionary<string, CampaignStats>(capacity: 128, StringComparer.Ordinal);
            var interner = new Dictionary<string, string>(StringComparer.Ordinal);
            long rows = 0;
            long bad = 0;

            long pos = readStart;
            while (pos < readEnd)
            {
                if ((rows & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested();

                var lineEnd = FindNewline(filePtr, pos, readEnd);
                if (lineEnd < 0) lineEnd = readEnd;

                var lineLen = (int)(lineEnd - pos);
                if (lineLen > 0)
                {
                    var line = new ReadOnlySpan<byte>(filePtr + pos, lineLen);
                    if (ByteLineParser.TryParse(line, interner, out var record, out _))
                    {
                        if (!shard.TryGetValue(record.CampaignId, out var bucket))
                        {
                            bucket = new CampaignStats(record.CampaignId);
                            shard[record.CampaignId] = bucket;
                        }
                        bucket.Add(in record);
                        rows++;
                    }
                    else
                    {
                        bad++;
                    }
                }

                pos = lineEnd + 1;
            }

            return new WorkerResult(shard, rows, bad);
        }
        finally
        {
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    /// <summary>
    /// Return the file offset of the next <c>\n</c> at or after
    /// <paramref name="from"/>, or <c>-1</c> if none. Uses
    /// <see cref="Span{T}.IndexOf(T)"/> on 2 GB chunks for SIMD-accelerated
    /// newline search on runtimes that support it.
    /// </summary>
    private static unsafe long FindNewline(byte* ptr, long from, long exclusiveEnd)
    {
        const int MaxChunk = int.MaxValue;
        var pos = from;
        while (pos < exclusiveEnd)
        {
            var remaining = exclusiveEnd - pos;
            var chunkLen = (int)Math.Min(remaining, MaxChunk);
            var span = new ReadOnlySpan<byte>(ptr + pos, chunkLen);
            var idx = span.IndexOf((byte)'\n');
            if (idx >= 0) return pos + idx;
            pos += chunkLen;
        }
        return -1;
    }

    private Dictionary<string, CampaignStats> MergeShards(WorkerResult[] results)
    {
        var merged = new Dictionary<string, CampaignStats>(StringComparer.Ordinal);
        long badTotal = 0;

        foreach (var result in results)
        {
            badTotal += result.BadRowCount;
            if (result.Shard is null) continue;
            foreach (var (key, stats) in result.Shard)
            {
                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = stats;
                }
                else
                {
                    existing.Merge(stats);
                }
            }
        }

        BadRowCount = badTotal;
        return merged;
    }

    private readonly record struct WorkerResult(
        Dictionary<string, CampaignStats>? Shard,
        long RowsProcessed,
        long BadRowCount)
    {
        public static WorkerResult Empty { get; } = new(null, 0, 0);
    }
}
