# Ad Performance Aggregator

A memory-efficient .NET 10 CLI that processes large advertising CSV data (~1 GB), aggregates by campaign, and emits the top-10 campaigns by CTR (highest) and CPA (lowest).

## Highlights

- **1 GB / 26.8 M rows → ~0.6 s** (warm cache) with `MemoryMappedAggregator`, **~0.9 s** cold, vs. ~9 s single-threaded.
- **Memory does not scale with file size.** Managed heap stays at ~33 MB regardless of input size; the input is accessed through an OS-managed memory-mapped region that is shared across workers, not copied per worker.
- **Two aggregators, picked by `--workers`**:
  - `CampaignAggregator` (`--workers 1`) — single-threaded, CsvHelper-based streaming reader. Full RFC 4180 CSV support.
  - `MemoryMappedAggregator` (default, `--workers 0` or `>1`) — maps the file once, splits into N byte ranges, one worker per range. Each worker parses directly from `ReadOnlySpan<byte>` via `Utf8Parser`; zero copies, zero line-string allocations.
- **Clean architecture** — Core (domain, no third-party deps) → Infrastructure (I/O adapters) → CLI (composition root). Each layer unit-tested in isolation.
- **BenchmarkDotNet** project compares sequential vs memory-mapped at 1 M / 10 M rows and 1 / 4 / 8 workers.
- **71 tests** covering aggregation, ranking, CSV parsing (both CsvHelper and byte-level paths), output formatting, merge correctness, byte-range partitioning, path safety, and an end-to-end run asserting byte-for-byte output across 4 worker configurations.

## Requirements

- .NET 10 SDK (`10.0.100` or newer) — see `global.json`

## Quick start

```bash
# Build
dotnet build AdPerformance.sln -c Release

# Run
dotnet run --project src/AdPerformance.CLI -c Release -- \
  --input ./data/ad_data.csv \
  --output-dir ./results \
  --top-n 10 \
  --verbose
```

Outputs:

- `./results/top10_ctr.csv` — top N campaigns by highest CTR
- `./results/top10_cpa.csv` — top N campaigns by lowest CPA (excludes campaigns with zero conversions)

## CLI reference

| Flag                        | Default               | Description                                            |
|-----------------------------|-----------------------|--------------------------------------------------------|
| `-i`, `--input <path>`      | (required)            | Input CSV file.                                        |
| `-o`, `--output-dir <dir>`  | `./results`          | Directory to write result CSVs.                        |
| `-n`, `--top-n <int>`       | `10`                  | Ranking size for each output file.                     |
| `-w`, `--workers <int>`     | `0` (auto, N-1 cores) | `1` forces single-threaded path. `>1` uses parallel aggregator. |
| `-v`, `--verbose`           | off                   | Log progress every 1 M rows.                           |
| `-h`, `--help`              | —                     | Print help and exit.                                   |

### Exit codes

| Code | Meaning                                                    |
|------|------------------------------------------------------------|
| 0    | Success                                                    |
| 1    | Usage error (bad/missing arguments)                        |
| 2    | Input file not found                                       |
| 3    | Fatal I/O error (disk full, permission denied, etc.)       |
| 4    | No valid rows in the input CSV                             |
| 5    | Unhandled exception or cancellation                        |

## Input schema

Row order does not matter; rows are aggregated by `campaign_id`. Unknown or malformed rows are counted and skipped.

| Column        | Type    | Notes                               |
|---------------|---------|-------------------------------------|
| `campaign_id` | string  | Non-empty; used as aggregation key. |
| `date`        | string  | `YYYY-MM-DD`, ISO 8601 only.        |
| `impressions` | integer | Non-negative.                       |
| `clicks`      | integer | Non-negative; ≤ impressions.        |
| `spend`       | float   | Non-negative USD.                   |
| `conversions` | integer | Non-negative.                       |

## Output schema

Both files share the same 7-column format (UTF-8, no BOM, LF line endings, invariant culture):

```
campaign_id,total_impressions,total_clicks,total_spend,total_conversions,CTR,CPA
```

- `total_spend` — 2 decimal places
- `CTR` — 4 decimal places (`clicks / impressions`, 0 when impressions = 0)
- `CPA` — 2 decimal places; **blank** when conversions = 0

## Architecture

```
src/
  AdPerformance.Core/             ← domain + ranking, zero third-party deps
    Models/          AdRecord, CampaignStats (Add/Merge), CampaignResult
    Aggregation/     CampaignAggregator (single-threaded, streaming)
    Ranking/         TopNSelector (PriorityQueue-based)
    Abstractions/    IAdRecordSource, IResultSink
  AdPerformance.Infrastructure/   ← I/O adapters
    Csv/             StreamingCsvReader (CsvHelper, for IAdRecordSource)
                     MemoryMappedAggregator (mmap + byte-range workers)
                     ByteLineParser  (ReadOnlySpan<byte> → AdRecord)
                     AdRecordParser  (string[] → AdRecord, shared helper)
                     LineParser      (ReadOnlySpan<char> → AdRecord)
                     CsvResultWriter
    Validation/      RowValidator
  AdPerformance.CLI/              ← entry point, argument parsing, DI/logging
tests/
  AdPerformance.Core.Tests/
  AdPerformance.Infrastructure.Tests/
  AdPerformance.IntegrationTests/ ← end-to-end with exact expected outputs
benchmarks/
  AdPerformance.Benchmarks/       ← BenchmarkDotNet
```

### Sequential data flow (`--workers 1`)

```
FileStream → StreamReader → CsvParser → StreamingCsvReader
  → CampaignAggregator → Dictionary<id, CampaignStats>
  → TopNSelector (CTR desc, CPA asc)
  → CsvResultWriter → FileStream
```

Full RFC 4180 CSV parsing via CsvHelper. Use this path for files with
quoted fields, embedded commas, or other CSV edge cases.

### Parallel data flow (`--workers 0|>1`) — memory-mapped

```
                    File (mmap'd once, OS manages paging)
                                 │
                shared virtual view of the whole file
                                 │
      ┌──────────────────────────┼──────────────────────────┐
      │                          │                          │
  worker 0 — range [0,L/N)  worker 1 — [L/N,2L/N)  …  worker k — [kL/N, L)
     │                        │                           │
  (skip header)          (align to next '\n')       (align, keep going
                                                     past range end to
                                                     finish last row)
     │                        │                           │
 shard dict ← ByteLineParser on ReadOnlySpan<byte> per row (Utf8Parser)
     │                        │                           │
     └──────────── Merge (single-threaded) ───────────────┘
                                 │
                         result Dictionary
```

The whole file is mapped once with `MemoryMappedFile.CreateFromFile`.
Workers compute a contiguous byte range `[start, end)`, align to the next
`\n` at or after `start`, and read through the first `\n` at or after
`end` (so the last row whose first byte sits in the range is consumed in
full). The mapping is read-only and shared — workers never copy or collide.

Parsing runs entirely on `ReadOnlySpan<byte>`:

- `ByteLineParser.TryParse` locates the 5 commas with a tight loop, slices
  the line into 6 field spans, and uses `System.Buffers.Text.Utf8Parser`
  for `long` / `decimal` — these are SIMD-optimised in recent .NET.
- Dates are parsed manually against the fixed `yyyy-MM-dd` layout; faster
  than `DateOnly.TryParseExact` on spans.
- Campaign-id interning uses
  `Dictionary<string,string>.GetAlternateLookup<ReadOnlySpan<char>>()` (added in
  .NET 9), so cache hits allocate **zero** strings. Only the first
  occurrence of each unique campaign_id ever allocates — about 50 strings
  total for the entire supplied 1 GB input.

There are **no locks** in the hot path: each worker owns its shard and
interner dictionary. Shards are merged single-threaded at the end using
`CampaignStats.Merge`.

## Memory profile

The crucial invariant: **managed heap usage is independent of file size**.
The only per-row allocation is one `CampaignStats` per unique `campaign_id`
per shard — which is bounded by the cardinality of `campaign_id`, not by
row count.

What scales with file size is the memory-mapped region (a virtual view),
not the managed heap. That virtual region is:

1. **Shared** across all workers — the mapping is mapped once, pointer
   views into it are free.
2. **OS-managed** — the kernel pages in the file lazily and evicts cold
   pages under memory pressure. No copy into user space.
3. **Single-copy worst case** — even if the entire file ends up resident,
   that is still 1 × file size, never N × file size.

Concretely, on the supplied 1 GB file with 9 workers:

- **Managed heap footprint**: ~33 MB (measured via `time -l`
  `peak memory footprint`). Does not change if you double the file size.
- **Max RSS**: ~1 GB (the shared mapping being fully paged in for the
  scan). This is one copy of the file, sitting in the OS page cache —
  reclaimable by the kernel at any time.

### Measured on the supplied 1 GB / 26.8 M row / 50-campaign dataset (M-series Mac, 10 cores, warm cache)

| `--workers` | Wall time | Aggregation only | User CPU | Managed heap | Speedup vs 1 |
|-------------|-----------|-------------------|----------|--------------|---------------|
| 1 (sequential, CsvHelper) | 10.25 s | 9.83 s  | 9.94 s  | 98 MB  | 1.00 × |
| 2              | 1.96 s  | 1.54 s  | 3.39 s  | 106 MB | 5.23 ×       |
| 4              | 1.22 s  | 0.87 s  | 3.59 s  | 104 MB | 8.40 ×       |
| 6              | 1.08 s  | 0.71 s  | 4.09 s  | 105 MB | 9.49 ×       |
| **9 (auto)**   | **0.93 s** | **0.58 s** | 4.60 s | 104 MB | **11.02 ×** |

Managed heap stays flat around 100 MB across all worker counts: doubling
the workers does **not** double heap usage. This is the key property the
`MemoryMappedAggregator` design buys you over a naïve "load 1/N of the file
per worker" approach.

Published (self-contained) binary on warm cache hits **~0.6 s** end-to-end
because startup is faster than `dotnet run`.

Parallel output is **byte-for-byte identical** to sequential — verified by
integration tests (4 worker configurations) and by `diff` against the
`--workers 1` output on the real 1 GB input.

### Scaling to 10 GB

A helper script lives at `scripts/make_large.py` for building larger test
files by replicating the body of `ad_data.csv` N times (single header, no
data variation — aggregates scale by N, CTR/CPA stay identical):

```bash
python3 scripts/make_large.py --multiplier 10       # → data/ad_data_x10.csv (~10.4 GB, 268 M rows)
```

Results on the generated 10 GB file (9 workers, M-series Mac):

| Run | Wall time | Aggregation | Throughput | Managed heap | Max RSS |
|-----|-----------|-------------|-----------|--------------|---------|
| Cold cache  | 55.3 s | 55.1 s | ~180 MB/s (disk-bound) | **37 MB** | 3.7 GB |
| Subsequent  | 57–58 s | ~57 s  | same (file doesn't fit in cache) | **38 MB** | 3.7 GB |

Two things to notice:

- **Managed heap is still ~37 MB** — essentially unchanged from the 1 GB
  run. The "heap memory does not scale with file size" invariant holds at
  10× the input.
- **Wall time scales linearly with file size** because the dominant cost
  at this scale is SSD read bandwidth. CPU utilisation drops to ~50%
  (29 s user / 58 s wall) — workers are waiting on I/O, not the parser.
  Verified byte-for-byte: every aggregate in the 10× run is exactly 10×
  the 1× run.

## Testing

```bash
dotnet test -c Release
```

Covers:

- **Core unit tests** — aggregation, CTR/CPA edge cases (zero impressions, zero conversions), Top-N tie-breaking, cancellation.
- **Infrastructure unit tests** — malformed rows, UTF-8 BOM, CRLF/LF, negative values, clicks > impressions, campaign_id interning, missing header columns.
- **Integration tests** — end-to-end run on a 14-row fixture asserting byte-for-byte output against hand-computed expected CSVs.
- **PathSanitizer tests** — control-character rejection, log-safe stripping.

## Benchmarks

```bash
dotnet run --project benchmarks/AdPerformance.Benchmarks -c Release -- --filter "*"
```

Generates synthetic CSVs at 100 K, 1 M, and 10 M rows, then reports throughput and allocation with BenchmarkDotNet (includes `[MemoryDiagnoser]`).

## Publishing

Single-file, self-contained (recommended for distribution):

```bash
dotnet publish src/AdPerformance.CLI/AdPerformance.CLI.csproj \
  -c Release \
  -r osx-arm64 \
  -p:PublishSingleFile=true \
  -o ./publish
```

The binary `./publish/AdPerformance` is runnable with no installed runtime:

```bash
./publish/AdPerformance --input ./data/ad_data.csv --output-dir ./output
```

Replace `osx-arm64` with the target RID (`linux-x64`, `win-x64`, etc.).

## Security posture

Snyk Code reports two findings on the CLI entry point:

| ID                  | Severity | Status                                                            |
|---------------------|----------|-------------------------------------------------------------------|
| `csharp/PT`         | Medium   | Accepted — intended behaviour for a developer-run CLI (see below) |
| `csharp/LogForging` | Low      | Accepted — sanitised via `PathSanitizer.ForLog`                   |

The CLI's core purpose is accepting paths from the user. Defence-in-depth applied in `PathSanitizer`:

- Reject null bytes, control characters, and invalid path characters at parse time.
- Canonicalise with `Path.GetFullPath` so every downstream call sees one normalised form.
- Re-allocate the string to break argv reference aliasing.
- Strip CR/LF/Tab from any user-supplied text before logging.

Ignore rules are declared in `.snyk` with documented justification. There is no multi-tenant / server context; the tool runs in the user's own shell with their own credentials.

## Known limitations

- CSV dialect for the parallel path is "simple unquoted comma-separated UTF-8" — adequate for the supplied schema (no commas or newlines inside values) but not a general CSV parser. For files that need RFC 4180 compliance (quoted fields, escaped quotes, embedded commas) use `--workers 1` which routes through CsvHelper.
- `decimal` is used for spend to avoid floating-point drift when summing 26 M+ values; converted to `double` only for CPA/CTR output.
- The memory-mapped path requires a 64-bit OS (current mainstream); large-file support on 32-bit platforms would need the path to fall back to chunked `RandomAccess.Read`.
- `MemoryMappedAggregator` uses a single scoped `unsafe` block inside `ProcessRange` to acquire a raw `byte*` from the mapped view. The unsafe code is contained to one file and never escapes `AcquirePointer`/`ReleasePointer`.
