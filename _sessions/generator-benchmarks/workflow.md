# Workflow: generator-benchmarks

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
session: 1
phases-total: 7
phases-complete: 2

## Problem Statement
Add benchmarks for the QuarryGenerator source-generation pipeline. The runtime benchmarks in `Quarry.Benchmarks` measure already-generated code; we have no signal on the generator's own cost.

Approach:
- Host new benchmarks **inside the existing `Quarry.Benchmarks` project** (no new project) so output flows through the existing `BenchmarkConfig`, BenchmarkDotNet JSON exporter, `Quarry.Benchmarks.Reporter` HTML renderer, and `benchmark.yml` publish pipeline unchanged.
- Use a **frozen corpus** of representative call sites curated from cross-dialect tests, embedded as resources. The corpus is intentionally version-bumped (v1, v2, ...) — adding tests does not drift the numbers.
- **Skip incremental-update benchmarks** (no IDE hot-loop concern; build-only workflow).
- Single dialect (PostgreSQL) for v1.

Baseline test run (2026-04-29): 3385 tests passing across Quarry.Tests (3056), Quarry.Migration.Tests (201), Quarry.Analyzers.Tests (128). 0 failures. No pre-existing failures to exclude.

## Decisions
- **2026-04-29** — Host generator benchmarks inside existing `Quarry.Benchmarks` project (not a new project). Reason: `--filter '*'` already discovers all benchmark classes, BenchmarkDotNet JSON output already flows to the existing reporter and gh-pages publish pipeline, no duplicate CI plumbing.
- **2026-04-29** — Skip incremental/per-keystroke benchmarks. Reason: Quarry generation is a build-time concern in this project; no IDE hot-loop is in scope.
- **2026-04-29** — Use a frozen, embedded corpus rather than the live cross-dialect test files. Reason: prevent benchmark drift as the test suite grows; corpus is version-bumped deliberately.
- **2026-04-29** — Single dialect (PostgreSQL) for v1. Reason: cleaner headline numbers; per-dialect `[Params]` can be added later if a per-dialect regression actually shows up.
- **2026-04-29** — All generator benchmark methods will be `Quarry_*`-prefixed. Reason: `benchmark.yml:181` filters published time-series data via `select(.Method | startswith("Quarry_"))`. Comparison-library benchmarks (Raw_/Dapper_/EfCore_/SqlKata_) are intentionally excluded from the tracked dataset; generator benchmarks have no comparison library, so they should follow Quarry_* convention to land in the tracked series automatically.
- **2026-04-29** — No `benchmark.yml` workflow changes required. `--filter '*'` discovers new benchmarks; path triggers already cover `src/Quarry.Benchmarks/` and `src/Quarry.Generator/`; Reporter is generic over class types.
- **2026-04-29** — Add `Microsoft.CodeAnalysis.CSharp` as a PackageReference to `Quarry.Benchmarks.csproj`. Reason: `Quarry.Generator` privatizes it via `PrivateAssets="all"`, so consumers cannot transitively use `CSharpCompilation`/`CSharpGeneratorDriver`. `MSB3277` is already suppressed in this csproj for the same Roslyn-vs-BenchmarkDotNet version mismatch.
- **2026-04-29** — Hybrid corpus shape: small handwritten fixture (one PostgreSQL context + 5 schemas: User/Order/OrderItem/Product/Address) plus query snippets ported near-verbatim from cross-dialect tests. Snippets reference fixture types whose names match `Quarry.Tests.Samples` so porting is mechanical.
- **2026-04-29** — v1 ships three benchmark classes inside `Quarry.Benchmarks/Generator/`:
  - `GeneratorColdCompileBenchmarks` — single fixed Medium corpus, headline number.
  - `GeneratorThroughputBenchmarks` — three discrete `[Benchmark]` methods (Quarry_Throughput_Small/Medium/Large) over hand-curated corpora of 10 / 50 / 200 query call sites. Separate methods (not `[Params]`) so each gets its own gh-pages time-series.
  - `GeneratorPipelineSplitBenchmarks` — three `[Benchmark]` methods over cumulative corpora: SchemaOnly (contexts+schemas), PlusQueries (+~50 queries), PlusMigrations (+~10 migrations). Cost differences attribute to Pipeline 2 (Interceptors) and Pipeline 3 (Migrations); SchemaOnly = Pipeline 1 baseline.
- **2026-04-29** — All benchmark methods prefixed `Quarry_*` so they land in the published gh-pages time-series.
- **2026-04-29** — Corpora live under `src/Quarry.Benchmarks/Corpora/v1/` as `.cs.txt` files (extension prevents the bench project itself from compiling them) and are loaded as embedded resources at `[GlobalSetup]`. Layout: `Corpora/v1/Fixture/*` (shared), `Corpora/v1/Throughput/{Small,Medium,Large}.cs.txt`, `Corpora/v1/PipelineSplit/{SchemaOnly,PlusQueries,PlusMigrations}.cs.txt`.
- **2026-04-29** — Verified `scripts/benchmark-pages/` and `Quarry.Benchmarks.Reporter` require **no changes**. Both are fully data-driven over BenchmarkDotNet output:
  - `Reporter/Program.cs:24,151,158` groups by `Type` (FQN of bench class) into a `SortedDictionary` and renders one `<section>` + sidebar entry per type, with anchor from `AnchorFor(type)` (lowercased letters/digits, everything else → `-`).
  - `dashboard.html:129,316` iterates `data.entries` and renders one chart per unique `bench.name` (FQN). `deriveSectionAnchor` (line 122-126) extracts the second-to-last token (class name) and applies the same `anchorFor` algorithm — chart click navigates to `runs/<date>-<sha>.html#<class-anchor>` which matches the Reporter's section anchor exactly.
  - `runs.html` is just a list of runs from `runs.json` — no per-method awareness.
  - `landing.html` static text mentions baselines (Raw/EF/Dapper/SqlKata) — still accurate; per-run reports continue to include those rows even though they're not in the tracked dashboard series.
  - Result: as long as new benchmarks land with class names ending in `*Benchmarks` (consistent with existing convention) and `Quarry_*` method names, they appear automatically with click-through navigation working.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-29 INTAKE | DESIGN (in progress) | Created branch+worktree, baseline tests green (3385/0), workflow.md initialized, scope confirmed: generator benchmarks inside existing Quarry.Benchmarks, frozen corpus, no incremental, single dialect v1. |
