# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test

```bash
dotnet build AdPerformance.sln -c Release
dotnet test  AdPerformance.sln -c Release         # all tests
dotnet test --project tests/AdPerformance.Core.Tests -c Release  # single project
```

## Run

```bash
dotnet run --project src/AdPerformance.CLI -c Release -- \
  -i ./data/ad_data.csv -o ./output -w 0 -v
```

CLI flags: `-i/--input`, `-o/--output-dir`, `-n/--top-n`, `-w/--workers`, `-v/--verbose`. Use `--workers 1` for the single-threaded CsvHelper path (full RFC 4180 CSV support). Default (`--workers 0`) uses auto-detected worker count.

Exit codes: 0 success, 1 usage error, 2 input not found, 3 I/O error, 4 no valid rows, 5 unhandled.

## Architecture

```
src/
  AdPerformance.Core/          — Domain (no third-party deps)
    Models/         CampaignStats, AdRecord, CampaignResult
    Aggregation/   CampaignAggregator (sequential streaming)
    Ranking/       TopNSelector (PriorityQueue-based)
  AdPerformance.Infrastructure/  — I/O adapters
    Csv/           MemoryMappedAggregator (mmap + byte-range workers)
                   ByteLineParser (ReadOnlySpan<byte> parsing via Utf8Parser)
                   StreamingCsvReader (CsvHelper, for --workers 1)
                   CsvResultWriter
  AdPerformance.CLI/  — Entry point, argument parsing, DI/logging
```

Two aggregation paths:
- `--workers 1`: Stream via `StreamingCsvReader` → `CampaignAggregator`. Full RFC 4180 CSV support.
- `--workers 0|N`: `MemoryMappedAggregator` maps file once, splits into N byte ranges, one worker per range. Workers parse `ReadOnlySpan<byte>` directly with `Utf8Parser` and manual date parsing — zero string allocations in hot path.

## Performance Characteristics

- Managed heap is ~33 MB regardless of input file size. Memory-mapped region is OS-managed, shared across workers.
- Optimal worker count is `Environment.ProcessorCount - 1` (leave headroom for OS). 10-core Mac Mini: 9 workers fastest.
- `Dictionary<string,string>.GetAlternateLookup<ReadOnlySpan<char>>()` used for campaign_id interning — only first occurrence of each unique campaign_id allocates a string.
- `decimal` used for spend to avoid floating-point drift; converted to `double` only for output CTR/CPA.

## Security Posture

Snyk Code reports two accepted findings on the CLI entry point:
- `csharp/PT` (Medium) — intentional: developer-run CLI accepts user-supplied paths
- `csharp/LogForging` (Low) — mitigated via `PathSanitizer.ForLog` which strips CR/LF/Tab

Do not "fix" these by removing path arguments. Ignore rules are in `.snyk`.

## Verification

Before declaring work complete:
- `dotnet build` and `dotnet test` must both pass.
- If CSV parsing or output format changed, run integration test that asserts byte-for-byte equality against fixture expected outputs.
- If perf-sensitive code changed, re-run against `data/ad_data.csv` and update measured-numbers table in `README.md`.

## Project Conventions

- `.NET 10`, `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`
- All projects use `ImplicitUsings` and `AnalysisLevel=latest`
- Code style: prefix private fields with `_`, file-scoped namespaces, nullable reference types enforced
- Test projects: `AdPerformance.{Layer}.Tests` matching each src layer, plus `AdPerformance.IntegrationTests`