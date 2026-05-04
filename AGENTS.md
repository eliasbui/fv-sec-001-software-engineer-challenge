# Agent instructions

## Project

Ad Performance Aggregator — a .NET 10 CLI that aggregates large ad-campaign
CSV data (~1 GB) by `campaign_id` and emits the top 10 by CTR and by CPA.

See `README.md` for the full feature set, architecture, and usage.

## Prompt log — always maintain

The user wants a running record of every prompt they issue on this project.

- **File**: `PROMPTS` in the repo root.
- **On every new user message**: before (or alongside) doing the requested
  work, append the user's message to `PROMPTS` under the current session
  heading.
- **First message in a new chat**: add a new heading at the bottom of
  `PROMPTS` of the form `## Session N — YYYY-MM-DD`, where `N` is the
  next integer after the last session in the file. Place the new input
  under this heading.
- **Do not** rewrite, reformat, or delete earlier entries — append-only.
- **Do not** omit anything the user said, including answers the user gave
  to clarifying questions asked via `ask_user_question`. Paraphrasing is OK
  if the raw answer is long; when paraphrasing, quote the verbatim answer
  too so the full text is preserved.

If `PROMPTS` ever goes missing, reconstruct the latest session from the
current conversation and continue from there.

## Build / test / run quick reference

```bash
dotnet build AdPerformance.sln -c Release        # build everything
dotnet test  AdPerformance.sln -c Release         # run all 63 tests
dotnet run --project src/AdPerformance.CLI -c Release -- \
  -i ./data/ad_data.csv -o ./output -w 0 -v       # full end-to-end run
```

## Verification expectations

Before declaring work complete:

- `dotnet build` and `dotnet test` must both succeed.
- If CSV parsing or output format changed, re-run the integration test
  that asserts byte-for-byte equality against the fixture expected
  outputs.
- If perf-sensitive code changed, re-run against `data/ad_data.csv` and
  update the measured-numbers table in `README.md`.

## Security

Snyk Code reports two standing findings (`csharp/PT`, `csharp/LogForging`)
on the CLI entry point. These are documented in `.snyk` and the Security
section of `README.md` as accepted — a developer CLI whose purpose is to
take user-supplied paths. Do not "fix" them by removing the path
arguments.
