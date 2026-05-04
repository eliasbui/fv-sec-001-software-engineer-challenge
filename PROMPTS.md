# User Prompts — Ad Performance Aggregator DEVIN CLI + OPUS 4.7 MAX + supper-power-kit + gsd-kit + Andrej Karpathy Skill

A running log of every user input in this project across chat sessions: the
original request, answers to clarifying questions, iteration feedback, and
any follow-ups.

**Convention**: new prompts from future chat sessions are appended at the
bottom under the next numbered heading. Do not rewrite or reformat earlier
entries — add, don't edit.

---

## Session 1 — 2026-05-04

### 1. Initial request — build the CLI

CSV Schema:

| Column        | Type    | Description                   |
|---------------|---------|-------------------------------|
| `campaign_id` | string  | Campaign ID                   |
| `date`        | string  | Date in `YYYY-MM-DD` format   |
| `impressions` | integer | Number of impressions         |
| `clicks`      | integer | Number of clicks              |
| `spend`       | float   | Advertising cost (USD)        |
| `conversions` | integer | Number of conversions         |

Interview requirements:

1. Aggregate data by `campaign_id`. For each `campaign_id`, compute:
   - `total_impressions`
   - `total_clicks`
   - `total_spend`
   - `total_conversions`
   - CTR = `total_clicks / total_impressions`
   - CPA = `total_spend / total_conversions`
   - If `conversions = 0`, ignore or return null for CPA
2. Generate two result CSV files:
   - `top10_ctr.csv` — top 10 campaigns with highest CTR
   - `top10_cpa.csv` — top 10 campaigns with lowest CPA, excluding campaigns with zero conversions
3. The input file is large, around 1 GB, so the solution must be
   memory-efficient.

> "I need to complete a Software Engineer Challenge named 'Ad Performance
> Aggregator'. The task is to build a CLI application that processes a
> large CSV file around ~1 GB. I'm familiar with C# better — please help me
> break down the implementation plan using .NET 10 latest console app,
> focusing on clean architecture, streaming CSV processing, performance,
> error handling, tests, and README.md documentation. Publish the program
> so it's runnable via CLI."

### 2. Answers to clarifying questions

**Output CSV schema** — full aggregate row (not minimal two-column).
Expected format for `top10_ctr.csv`:

```
campaign_id   total_impressions   total_clicks   total_spend   total_conversions   CTR      CPA
CMP042        125000              6250           12500.50      625                 0.0500   20.00
CMP015        340000              15300          30600.25      1530                0.0450   20.00
CMP008        890000              35600          71200.75      3560                0.0400   20.00
CMP023        445000              15575          31150.00      1557                0.0350   20.00
CMP031        670000              20100          40200.50      2010                0.0300   20.00
```

Expected format for `top10_cpa.csv` (excludes campaigns with zero
conversions):

```
campaign_id   total_impressions   total_clicks   total_spend   total_conversions   CTR      CPA
CMP007        450000              13500          13500.00      1350                0.0300   10.00
CMP019        780000              23400          23400.00      2340                0.0300   10.00
CMP033        290000              8700           10440.00      870                 0.0300   12.00
CMP012        560000              16800          21840.00      1680                0.0300   13.00
CMP025        320000              9600           13440.00      960                 0.0300   14.00
```

**Perf ambition** — "Simple first, optimize if needed."
Build clean single-threaded streaming, measure on the real 1 GB file, only
add parallelism if runtime misses target.

**Test stack** — the tests required are:

- A. Top 10 campaigns with the highest CTR — output as CSV format.
- B. Top 10 campaigns with the lowest CPA — output as CSV format, excluding
  campaigns with zero conversions.

Also requested: "add benchmark for test good performance and memory
optimization."

### 3. Performance feedback — go parallel

After the first end-to-end run (~9.3 s on the real 1 GB file):
./publish/AdPerformance -i ./data/ad_data.csv -o ./output                                                                    zsh  00:16:55 
00:26:40 info: AdPerformance.CLI.AggregateCommand[0] Running parallel aggregator with 9 workers, batch size 65,536.
00:26:44 info: AdPerformance.CLI.AggregateCommand[0] Aggregation complete: 50 campaigns, 0 bad rows, 00:00:04.2741448
00:26:44 info: AdPerformance.CLI.AggregateCommand[0] Wrote 10 CTR and 10 CPA rows to /Volumes/Code/interview/fv-sec-001-software-engineer-challenge/output in 00:00:04.2875987
> "Slow, I think split file into chunk like 50k-100k rows each and run
> parallel would help. Each chunk process independently then merge at end."

### 4. Export request

> "In this chat session, I want to export my input, the answers to your
> questions, to the file @PROMPTS. This includes all of it. Later, when
> I have more chats, please remember to include this input as well."

### 5. Push for ~1 s runtime, bounded memory

After verifying `--workers 12` finished in ~4 s on the 1 GB file:
./publish/AdPerformance -i ./data/ad_data.csv -o ./output -w 24                                                              zsh  00:16:25 
00:16:51 info: AdPerformance.CLI.AggregateCommand[0] Running parallel aggregator with 24 workers, batch size 65,536.
00:16:55 info: AdPerformance.CLI.AggregateCommand[0] Aggregation complete: 50 campaigns, 0 bad rows, 00:00:04.0247432
00:16:55 info: AdPerformance.CLI.AggregateCommand[0] Wrote 10 CTR and 10 CPA rows to /Volumes/Code/interview/fv-sec-001-software-engineer-challenge/output in 00:00:04.0372030
> "It seems better now. Is there a way to do it in about 1 second? And not
> be affected by large files x N times the size of the data file."

### 6. Generate a larger test file

After the 1 GB run landed at ~0.6–0.9 s (`MemoryMappedAggregator`, 9 workers):

> "If possible, please create a larger file for me than ad_data.csv with
> the format ad_data_{x_N}.csv. Tools like Python 3 should be allowed to
> create this file. I need a 10GB file."

### 7. Why does 10 workers run slower than 9 on a 10-core machine?

Observed on a 10-core Mac Mini:

```
./publish/AdPerformance -i ./data/ad_data.csv -o ./output -w 9
  Aggregation complete: 50 campaigns, 0 bad rows, 00:00:00.7601607

./publish/AdPerformance -i ./data/ad_data.csv -o ./output -w 10
  Aggregation complete: 50 campaigns, 0 bad rows, 00:00:02.6409078
```

> "I wonder why 10 takes more time than 9. I'm using a Mac Mini 10 Core.
> Please explain it to me."

### 8. Why 10GB ~60s

>"Why is my 10GB file slower than my 1GB file?
>My 1GB file only takes about 0.7 seconds to load, while the >10GB file takes 60 seconds."

### 9. Final check

>Please write your code carefully. i wanna expect:

>Correct results — output must match expected values precisely
>Clean, readable code — meaningful names, consistent style, no dead code or commented-out blocks
>Error handling — handle missing files, malformed rows, and edge cases gracefully
>Performance awareness — the input is ~1GB; my solution must be memory-efficient
>Tests — include tests to verify my solution's correctness
>Documented decisions — briefly explain non-obvious choices in my README
