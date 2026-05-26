# Workflow: add-patch-update-benchmarks
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: active
issue: discussion
pr:
session: 1
phases-total: 1
phases-complete: 1
## Problem Statement
The Patch partial-update API (commit 8797127, PR #301) introduced runtime-variable column UPDATEs to Quarry: `Update().Set(T.Patch)` and `Set((ref T.Patch p) => { ... })`. None of the existing benchmarks exercise this scenario — `UpdateBenchmarks.cs` only covers single-column, fixed-shape updates.

Add a new benchmark file `src/Quarry.Benchmarks/Benchmarks/PatchUpdateBenchmarks.cs` that compares Quarry's Patch lambda form against Raw ADO.NET, Dapper, EF Core, and SqlKata when the SET column set is decided at runtime by caller flags. Two cardinalities per library (OneColumn, AllColumns) — 10 benchmarks total.

Baseline tests: 53 Patch-relevant tests pass (CrossDialectUpdateTests, PatchInfoTests). No pre-existing failures.
## Decisions
- 2026-05-26: **Scenario** = variable column set (Patch's headline use case). The fixed multi-column and cross-method scenarios were considered but rejected — the assignment-lambda overload already covers fixed shapes, and the cross-method scenario adds comparison noise without changing the conclusion.
- 2026-05-26: **File** = new `PatchUpdateBenchmarks.cs`, sibling to `UpdateBenchmarks.cs`. Keeps the fixed-update baseline clean and matches the repo's one-scenario-per-file convention.
- 2026-05-26: **Patch entry form** = lambda only — `Set((ref User.Patch p) => { if (flag) p.X = ...; })`. Value-form `Set(patch)` deferred — the lambda form is the most direct comparison vs. Dapper's StringBuilder and EF's conditional `SetProperty` story.
- 2026-05-26: **Column count** = two named benchmarks per library, `_OneColumn` and `_AllColumns` (no `[Params]`). Cleaner result table; AllColumns is where Patch's amortized SQL template earns its keep.
- 2026-05-26: **EF idiom** = load + mutate + `SaveChangesAsync` (idiomatic, 2 round-trips). The Expression-built `SetProperty` variant exists but isn't how real EF code looks for runtime-variable updates; honest comparison wins over technically-fairer-but-contrived. Documented in a code comment.
- 2026-05-26: **Touched columns** = `UserName`, `Email`, `IsActive`, `LastLogin`. Skips `CreatedAt` (semantically odd to mutate) even though it's in the Patch struct.
- 2026-05-26: **Flag state** = 4 `private static bool` fields set to `true` in `GlobalSetup`. Mirrors `ConditionalBranchBenchmarks` — JIT can't constant-fold field reads, but every iteration takes the same branch. Static to avoid the `UnsafeAccessor` source-gen bug `UpdateBenchmarks` already documents.
- 2026-05-26: **Baseline** = `Raw_OneColumn` (single `[Benchmark(Baseline=true)]`, repo convention). AllColumns rows report absolute times; readers compare same-library OneColumn vs. AllColumns for scaling. **Superseded 2026-05-26.**
- 2026-05-26 (revised): **Baseline = per-category.** `[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]` + `[CategoriesColumn]` on the class, `[BenchmarkCategory("OneColumn")]` / `[BenchmarkCategory("AllColumns")]` on every method, and `[Benchmark(Baseline=true)]` on BOTH `Raw_OneColumn` and `Raw_AllColumns`. Previous single-baseline design produced meaningless ratios (e.g. `Quarry_AllColumns` vs `Raw_OneColumn` — fundamentally different SQL shapes). Repo convention for single-scenario files still holds; this file has two scenarios in one class and needs the grouping.
- 2026-05-26: **Auto-capture confirmed.** `.github/workflows/benchmark.yml` runs `--filter '*'` on master after CI; `src/Quarry.Benchmarks/` is in the path filter (line 59). The Quarry trend graph at `Dtronix/Quarry-benchmarks` filters to methods starting with `Quarry_` (workflow line 179) — `Quarry_OneColumn` and `Quarry_AllColumns` match. No additional wiring needed.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-05-26 INTAKE | 2026-05-26 DESIGN | Created worktree, established baseline (53 Patch tests pass), recorded all design decisions from clarification dialog. Moving to DESIGN to confirm by reading source. |
| 2 | 2026-05-26 DESIGN | 2026-05-26 IMPLEMENT | Verified User.Patch struct emits 5 mask bits, schema matches, EF context shape OK. plan.md written (single phase). User approved plan. |
| 3 | 2026-05-26 IMPLEMENT | 2026-05-26 IMPLEMENT-done | Wrote PatchUpdateBenchmarks.cs (10 benchmarks). Build clean (0/0). Interceptor file emitted for both Quarry chains. Dry-run smoke test: all 10 ran successfully — Quarry_AllColumns 1.33× baseline vs Dapper 1.76×, SqlKata 2.85×, EF ~25× (load-mutate-save 2-trip cost). 53 Patch tests still pass after new call sites added. |
| 4 | 2026-05-26 REVIEW | 2026-05-26 REVIEW-fix | User flagged that comparing AllColumns against the OneColumn baseline is meaningless (different scenarios). Added BenchmarkCategory + GroupBenchmarksBy + CategoriesColumn; Raw_OneColumn and Raw_AllColumns each now own their group baseline. Verified by `--job short` run: per-group ratios render. Also confirmed CI auto-capture: workflow filter='*' picks up new file, Quarry_* prefix matches trend-graph filter. |
