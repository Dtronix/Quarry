## Summary
- Adds `src/Quarry.Benchmarks/Benchmarks/PatchUpdateBenchmarks.cs` — a new benchmark file comparing the Patch partial-update API (PR #301) against Raw ADO.NET, Dapper, EF Core, and SqlKata for the variable-column UPDATE scenario.
- 10 benchmarks total: 5 libraries × 2 cardinalities (`OneColumn`, `AllColumns`), each cardinality with its own `Raw_*` baseline.

## Reason for Change

`UpdateBenchmarks.cs` already covers fixed single-column UPDATE. The Patch API's headline use case is the *variable-column* case — the SET column list is decided at runtime by caller flags rather than at the call site. The other `Set` overloads (assignment lambda, entity initializer) both fix the column set at compile time and can't express this without resorting to multiple `if`-guarded chains. There was no benchmark exercising this scenario, so the cost story for runtime-variable updates wasn't documented anywhere.

## Impact

Per-category results from a `--job short` run:

**AllColumns** (baseline = `Raw_AllColumns`):
| Method | Ratio (time) | Alloc Ratio |
|---|---:|---:|
| Quarry_AllColumns  | 1.11×  | **0.95×** |
| Dapper_AllColumns  | 1.48×  | 1.99× |
| SqlKata_AllColumns | 2.19×  | 6.07× |
| EfCore_AllColumns  | 21.51× | 31.00× |

**OneColumn** (baseline = `Raw_OneColumn`):
| Method | Ratio (time) | Alloc Ratio |
|---|---:|---:|
| Quarry_OneColumn   | 1.12×  | 1.55× |
| Dapper_OneColumn   | 1.23×  | 1.60× |
| SqlKata_OneColumn  | 2.55×  | 11.81× |
| EfCore_OneColumn   | 26.29× | 77.22× |

The standout: `Quarry_AllColumns` allocates **5% less** than hand-rolled `Raw_AllColumns` — Quarry's prerendered SET-fragment table + mask lookup beats `StringBuilder` + per-call parameter list growth.

## Plan items implemented as specified

- Lambda-form Patch only: `Set((ref User.Patch p) => { if (flag) p.X = ...; })`. Value-form deferred.
- Two named methods per library: `_OneColumn` and `_AllColumns` (no `[Params]` matrix).
- Columns touched: `UserName`, `Email`, `IsActive`, `LastLogin`. Skips `CreatedAt` (semantically odd to mutate).
- 4 flag fields as `private static bool`, all set to `true` in `GlobalSetup` — mirrors `ConditionalBranchBenchmarks` (JIT can't constant-fold field reads, but every iteration takes the same branch).
- Static fields workaround for source-generator `UnsafeAccessor` bug (matches `UpdateBenchmarks.cs:19-21`).
- EF idiom: `FirstAsync` + property mutation + `SaveChangesAsync` (2 round-trips). Code comment explains this is the realistic EF pattern — `ExecuteUpdate`'s `SetProperty` chain is fixed at compile time without hand-built `Expression<>` trees, which nobody writes in production.
- Raw/Dapper `AllColumns` use `StringBuilder` + `first` flag for comma separators; SqlKata uses `Dictionary<string, object>` + `AsUpdate(dict)`.

## Deviations from plan implemented

- **Per-category baselines** — the original plan called for a single `[Benchmark(Baseline=true)]` on `Raw_OneColumn` per "repo convention." That convention fits single-scenario benchmark classes; this file has two scenarios, so the single baseline produced meaningless cross-scenario ratios (`Quarry_AllColumns` vs `Raw_OneColumn`). Fixed with `[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]` + `[CategoriesColumn]` on the class, `[BenchmarkCategory("OneColumn")]` / `[BenchmarkCategory("AllColumns")]` on every method, and a second `[Benchmark(Baseline=true)]` on `Raw_AllColumns`. Verified by `--job short` run.

## Gaps in original plan implemented

- None beyond the per-category baseline deviation above.

## Migration Steps

None. Purely additive — no Quarry library code touched, no existing benchmarks changed, no infrastructure modified.

## Performance Considerations

This *is* the performance work. The new benchmarks will be auto-captured by `.github/workflows/benchmark.yml` on the next post-merge CI run (path filter at `benchmark.yml:59` includes `src/Quarry.Benchmarks/`; `--filter '*'` picks up every benchmark). The trend-graph publisher at `benchmark.yml:179` filters published series to methods matching `Quarry_*` — both `Quarry_OneColumn` and `Quarry_AllColumns` match, so they'll appear in the `Dtronix/Quarry-benchmarks` gh-pages trend data automatically after merge. No additional wiring required.

## Security Considerations

The dynamic SQL assembly in `Raw_AllColumns` / `Dapper_AllColumns` would be unsafe if copy-pasted into production code that accepted user-supplied column names. Here it's safe because: (a) the column-name strings being `Append`ed are hardcoded literals; (b) all values are parameterized via `AddWithValue` / `DynamicParameters.Add`; (c) this is benchmark code against an in-memory SQLite DB, never user-facing. The pattern is benchmark convention, not a transferable template.

## Breaking Changes

- Consumer-facing: none.
- Internal: none. No changes to Quarry library code, `Quarry.Benchmarks.csproj`, `BenchmarkBase.cs`, or any existing benchmark file. Three commits, all in this PR, all additive.
