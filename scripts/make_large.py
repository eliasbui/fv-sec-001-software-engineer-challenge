#!/usr/bin/env python3
"""
Generate a larger ad-campaign CSV by replicating the body of ``ad_data.csv``.

The aggregator is deterministic in row order, so multiplying every row by ``N``
simply multiplies every aggregate by ``N`` while preserving CTR and CPA — ideal
for benchmarking throughput/memory at scale without changing expected results.

Usage
-----
    python3 scripts/make_large.py                         # default: 10x
    python3 scripts/make_large.py --multiplier 5          # 5x, ~5 GB
    python3 scripts/make_large.py --multiplier 50         # 50x, ~50 GB

Output file name follows the pattern ``ad_data_x{N}.csv`` in ``--output-dir``
(default: the same directory as the input).

Memory usage is constant (a single 4 MiB I/O buffer) regardless of ``N`` or the
input size — the script streams bytes straight from the source file to the
destination.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import time
from pathlib import Path


BUFFER_SIZE = 4 * 1024 * 1024  # 4 MiB, fits comfortably in L2 on modern CPUs


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Replicate ad_data.csv body N times for large-scale benchmarks.",
    )
    parser.add_argument(
        "--input",
        type=Path,
        default=Path("data/ad_data.csv"),
        help="Source CSV (default: data/ad_data.csv)",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=None,
        help="Output directory (default: same as input)",
    )
    parser.add_argument(
        "--multiplier",
        "-n",
        type=int,
        default=10,
        help="How many times to replicate the body (default: 10)",
    )
    parser.add_argument(
        "--output-name",
        type=str,
        default=None,
        help="Override output filename (default: ad_data_x{multiplier}.csv)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite the output file if it already exists.",
    )
    return parser.parse_args()


def human_mb(n: int) -> str:
    return f"{n / (1024 * 1024):.1f} MiB" if n < 1 << 30 else f"{n / (1024 ** 3):.2f} GiB"


def main() -> int:
    args = parse_args()

    if args.multiplier < 1:
        print("error: --multiplier must be >= 1", file=sys.stderr)
        return 2

    if not args.input.is_file():
        print(f"error: input not found: {args.input}", file=sys.stderr)
        return 2

    out_dir = args.output_dir or args.input.parent
    out_dir.mkdir(parents=True, exist_ok=True)

    out_name = args.output_name or f"ad_data_x{args.multiplier}.csv"
    out_path = out_dir / out_name

    if out_path.exists() and not args.force:
        print(
            f"error: output already exists: {out_path}\n"
            f"       pass --force to overwrite.",
            file=sys.stderr,
        )
        return 1

    input_size = args.input.stat().st_size
    print(
        f"source: {args.input}  ({human_mb(input_size)})\n"
        f"target: {out_path}\n"
        f"  copies: {args.multiplier}",
        file=sys.stderr,
    )

    # Locate the end of the header line so we can skip it on every replication.
    with args.input.open("rb") as src:
        header = src.readline()
        body_start = src.tell()

    if not header:
        print("error: source file is empty.", file=sys.stderr)
        return 3

    body_size = input_size - body_start
    expected_total = len(header) + body_size * args.multiplier
    print(
        f"  header: {len(header)} bytes, body: {human_mb(body_size)}\n"
        f"  expected output: {human_mb(expected_total)}",
        file=sys.stderr,
    )

    free_bytes = shutil.disk_usage(out_dir).free
    if free_bytes < expected_total + (256 * 1024 * 1024):  # 256 MiB safety margin
        print(
            f"error: not enough free space on {out_dir} "
            f"(need ~{human_mb(expected_total)}, have {human_mb(free_bytes)}).",
            file=sys.stderr,
        )
        return 4

    start = time.monotonic()
    bytes_written = 0

    with out_path.open("wb", buffering=0) as dst:
        dst.write(header)
        bytes_written += len(header)

        for copy in range(1, args.multiplier + 1):
            with args.input.open("rb", buffering=0) as src:
                src.seek(body_start)
                while True:
                    chunk = src.read(BUFFER_SIZE)
                    if not chunk:
                        break
                    dst.write(chunk)
                    bytes_written += len(chunk)
            elapsed = time.monotonic() - start
            rate = bytes_written / elapsed / (1024 ** 2) if elapsed else 0
            print(
                f"  [{copy}/{args.multiplier}] "
                f"{human_mb(bytes_written):>10} written  "
                f"({rate:,.0f} MiB/s, {elapsed:,.1f}s elapsed)",
                file=sys.stderr,
            )

    elapsed = time.monotonic() - start
    actual_size = out_path.stat().st_size
    print(
        f"\ndone  {human_mb(actual_size)}  "
        f"in {elapsed:,.1f}s  "
        f"({actual_size / elapsed / (1024 ** 2):,.0f} MiB/s)\n"
        f"  -> {out_path}",
        file=sys.stderr,
    )

    if actual_size != expected_total:
        print(
            f"warning: size mismatch "
            f"(expected {expected_total}, got {actual_size}).",
            file=sys.stderr,
        )
        return 5

    return 0


if __name__ == "__main__":
    sys.exit(main())
