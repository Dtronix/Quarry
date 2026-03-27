#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BENCH_DIR="$ROOT_DIR/src/Quarry.Benchmarks"
RESULTS_DIR="$ROOT_DIR/docs/articles/benchmark-results"

echo "Building benchmarks in Release mode..."
dotnet build "$BENCH_DIR" -c Release --no-restore

echo "Running benchmarks..."
dotnet run --project "$BENCH_DIR" -c Release -- --filter '*' --artifacts "$RESULTS_DIR"

echo ""
echo "Results written to: $RESULTS_DIR"
echo "Copy relevant tables into docs/articles/benchmarks.md"
