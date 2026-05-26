# Code Review: PatchUpdateBenchmarks

## Classifications

| # | Class | Rec | Sev | Section | Finding | Action Taken |
|---|---|---|---|---|---|---|
| 1 | D | D | Low | Plan Compliance | All recorded decisions implemented faithfully | — |
| 2 | D | D | Low | Plan Compliance | Algorithm sketches transcribed without scope creep | — |
| 3 | D | D | Low | Correctness | IterationCleanup reset matches seed exactly | — |
| 4 | D | D | Low | Correctness | Quarry mask=0 throws if all flags toggled false (unreachable under GlobalSetup) | — |
| 5 | D | D | Low | Correctness | Raw/Dapper AllColumns malformed SQL if all flags false (unreachable) | — |
| 6 | D | D | Low | Correctness | EF FirstAsync + SaveChangesAsync return shapes match peer libraries | — |
| 7 | D | D | Low | Correctness | Comma-separator dance correct in Raw_AllColumns/Dapper_AllColumns | — |
| 8 | D | D | Low | Correctness | Parameter naming consistent within each library | — |
| 9 | D | D | Low | Correctness | IsActive int vs bool binding matches per-library idiom in InsertSingleBenchmarks | — |
| 10 | D | D | Low | Correctness | Sync IterationCleanup matches UpdateBenchmarks style | — |
| 11 | D | D | Low | Test Quality | Plan's "no benchmark unit tests" claim verified | — |
| 12 | D | D | Low | Test Quality | Build + dry-run + existing-Patch-tests verification path adequate | — |
| 13 | D | D | Low | Codebase Consistency | `Library_Cardinality` naming vs `Library_Scenario` — cosmetic; class name carries scenario | — |
| 14 | D | D | Low | Codebase Consistency | Using directives trimmed appropriately (no SqliteConnection/Compiler refs) | — |
| 15 | D | D | Low | Codebase Consistency | Static-field workaround applied uniformly with cross-ref to UpdateBenchmarks | — |
| 16 | D | D | Low | Codebase Consistency | IterationSetup/Cleanup shape matches UpdateBenchmarks exactly | — |
| 17 | D | D | Low | Codebase Consistency | Baseline placement on Raw_OneColumn matches repo convention | — |
| 18 | D | D | Low | Codebase Consistency | Comment density and tone match UpdateBenchmarks | — |
| 19 | D | D | Low | Integration | Diff purely additive — single source file + two session docs | — |
| 20 | B | B | Medium | Plan Compliance | Plan specified single `[Benchmark(Baseline=true)]` on `Raw_OneColumn`. With two scenarios in one class, `Quarry_AllColumns` got ratio'd against `Raw_OneColumn` — meaningless (different SQL shapes). Plan itself had the gap; agent reviewed code-against-plan faithfully but didn't flag the plan defect. | Fixed in 46ae9db: added `[GroupBenchmarksBy(ByCategory)]` + `[CategoriesColumn]` + `[BenchmarkCategory]` on every method + second `[Benchmark(Baseline=true)]` on `Raw_AllColumns`. Verified per-group ratios via `--job short`. Decision updated in workflow.md. |
| 21 | D | D | Low | Plan Compliance | Auto-capture verified: `.github/workflows/benchmark.yml` filter='*' picks up new file; trend graph filters to `Quarry_*` prefix which matches both new methods. No additional wiring needed. | Recorded in workflow.md Decisions. |

Single-file additive change (`src/Quarry.Benchmarks/Benchmarks/PatchUpdateBenchmarks.cs`, 244 lines, 10 benchmarks). Reviewed against `plan.md`, the Decisions section of `workflow.md`, sibling benchmarks (`UpdateBenchmarks.cs`, `ConditionalBranchBenchmarks.cs`, `InsertSingleBenchmarks.cs`), supporting infrastructure (`DatabaseSetup.cs`, `EfBenchContext.cs`, `Entities.cs`, `BenchmarkBase.cs`), and the Patch API surface (`UpdateBuilderPatchExtensions.cs`, `docs/articles/modifications.md`).

## Plan Compliance

| Finding | Severity | Why It Matters |
|---|---|---|
| Implementation hews to every recorded decision: lambda-only Patch form, two named methods per library (`_OneColumn`/`_AllColumns`), columns restricted to `UserName/Email/IsActive/LastLogin` (no `CreatedAt`), four flags as `private static bool` initialized in `GlobalSetup`, EF idiom as load-mutate-`SaveChangesAsync`, single new file at the planned path. | Low | Verifies no drift between approved design and code that was written. |
| Plan algorithm sketches transcribed faithfully — Raw/Dapper StringBuilder + `first` flag, SqlKata `Dictionary<string, object>`, Quarry `Set((ref User.Patch p) => …)`. No scope creep (no extra benchmarks, no extra cardinalities, no `[Params]` introduction). | Low | Confirms execution stayed inside the scoped phase boundary. |
| **Plan-level gap (user-caught, fixed):** Original plan specified a single `[Benchmark(Baseline=true)]` on `Raw_OneColumn` as "repo convention." That convention fits single-scenario benchmark classes (every existing file follows it). This file has *two* scenarios in one class, so the single baseline produced meaningless ratios — `Quarry_AllColumns` was being compared against `Raw_OneColumn`, fundamentally different SQL shapes. Fixed in commit 46ae9db with `[GroupBenchmarksBy(ByCategory)]` + `[CategoriesColumn]` + per-method `[BenchmarkCategory]` + a second `Baseline=true` on `Raw_AllColumns`. Verified by `--job short` run: AllColumns group ratios are now interpretable (Quarry_AllColumns at 1.11× time / 0.95× alloc vs Raw_AllColumns). | Medium | Without per-category baselines, the most important number in the file (Patch's allocation efficiency vs hand-rolled raw at AllColumns) was hidden behind a misleading cross-scenario ratio. |
| **Auto-capture verified (user-asked):** `.github/workflows/benchmark.yml` runs on master after CI, filter `'*'` includes the new file, and the trend-graph publishing step filters to methods matching `Quarry_*` (workflow line 179) — `Quarry_OneColumn` and `Quarry_AllColumns` will both appear in `Dtronix/Quarry-benchmarks` gh-pages trend data after merge. No additional wiring required. | Low | Confirms the new benchmarks feed the historical trend system that tracks Quarry-vs-baseline drift over time. |

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| The `IterationCleanup` reset SQL matches the seed exactly: `User001` / `user001@example.com` / `IsActive = 1` / `LastLogin = NULL`. Cross-checked against `DatabaseSetup.cs:51-54` for `i=1` — `User{1:D3}` → `User001`, `user{1:D3}@example.com` → `user001@example.com`, `i % 5 == 0` is false so Email is set (not NULL), `i % 10 != 0` is true so `IsActive = 1`, `LastLogin` is never seeded so `NULL`. Reset is correct. | Low | Without a sound reset the second iteration measures a different scenario than the first — silent contamination of results. |
| Quarry_OneColumn and Quarry_AllColumns wrap the conditional **inside** the `Set` lambda. If any future change set `_setName=false` for all four flags, the Patch chain would execute with mask=0 and throw `InvalidOperationException` per `modifications.md:148` ("Sending a Patch where no setters fired… throws"). Other libraries' OneColumn variants early-return `0` instead. Today flags are always `true` in `GlobalSetup`, so this never fires — but the asymmetric error shape is worth knowing if anyone later toggles flags for a Params sweep. | Low | Real semantic difference between libraries that's masked by the all-true `GlobalSetup`; readers iterating on the file should be aware. |
| Raw_AllColumns / Dapper_AllColumns produce malformed SQL (`UPDATE users SET  WHERE UserId = @id`) when **all four** flags are false — the StringBuilder ends with the literal `"UPDATE users SET "` and no SET assignments. SqlKata_AllColumns passes an empty dictionary to `AsUpdate`; behavior is library-defined. Not reachable today (`GlobalSetup` forces all true) but it's the same hazard noted above, in inverse. | Low | All-zero-mask is an edge case; flagging for parity with the Quarry note above. |
| `EfCore_OneColumn` / `EfCore_AllColumns` use `FirstAsync` (throws if missing) on a row that GlobalSetup pins as `_targetId = 1` and IterationCleanup re-asserts; row 1 always exists, so the lookup never throws. `SaveChangesAsync` returns 1 when any tracked property differs from the loaded value, 0 otherwise — with `_setName=true` and the loaded `UserName="User001"` ≠ `NewName="Updated"`, EF emits an UPDATE and returns 1. Same logic for all-true AllColumns. Idiom is correct. | Low | Confirms EF baseline produces the same return-value shape (`Task<int>`) as the others. |
| Comma-separator dance (`first` flag pattern) is identical and correct in both Raw_AllColumns and Dapper_AllColumns — `first` is only set to `true` initially, flipped to `false` after the first appended column, and gated by `if (!first) sb.Append(", ")` on subsequent branches. The LastLogin branch correctly omits the trailing `first = false;` (no fifth branch). | Low | Off-by-one in the separator dance would silently produce invalid SQL only when specific flag combinations were exercised. |
| Parameter naming is consistent **within** each library (Raw: `@name/@email/@active/@last/@id`; Dapper: `@UserName/@Email/@IsActive/@LastLogin/@UserId`; SqlKata: auto-generated `@p0…`) and matches that library's sibling-file conventions. | Low | Inconsistent parameter naming within a single command would be a latent bug; not present here. |
| `IsActive` is bound as `int 1` in Raw and SqlKata, but `bool true` in Dapper and as an entity property in EF. This matches the per-library idiom established in `InsertSingleBenchmarks.cs` (Raw=`1`, Dapper=`true`, SqlKata=`1`, EF=`true`). SQLite stores `INTEGER` so both bindings work — no correctness issue. | Low | Documents that the asymmetry is intentional and consistent with the rest of the suite. |
| Cleanup uses synchronous `using var cmd = Connection.CreateCommand(); cmd.ExecuteNonQuery();` to match `UpdateBenchmarks.cs:40-42`. The synchronous call inside `[IterationCleanup]` is acceptable (BDN's cleanup hook is sync). | Low | A latent mistake would be `await using` + `ExecuteNonQueryAsync` without an `async` cleanup method — not present. |

## Security

No concerns.

The file is a benchmark harness against an in-memory SQLite DB. Every value is a `const` or computed (`DateTime.UtcNow`). No user input flows into any of the StringBuilders or interpolated strings. The dynamic SQL assembly in `Raw_AllColumns`/`Dapper_AllColumns` would be unsafe if copy-pasted into production — but the column names being appended (`UserName`, `Email`, `IsActive`, `LastLogin`) are hard-coded literals, not user-supplied, and the values are all parameterized via `AddWithValue`/`DynamicParameters.Add`. The pattern is safe **only because** the column names are literals; that's a benchmark convention, not a transferable practice. The plan does not advertise this code as a production template.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| Plan claim ("no benchmark unit tests in this repo") is correct. Searched `src/Quarry.Tests` for `Quarry.Benchmarks.Benchmarks` and `BenchmarkBase` references — the only hit (`GeneratorBenchmarkHarnessTests.cs`) imports `Quarry.Benchmarks.GeneratorHarness` for a source-generator test, not the benchmark classes. No existing precedent for unit-testing benchmark files. | Low | Verifies the plan's verification posture (build + dry-run + existing Patch tests) is the standard for this repo, not a shortcut. |
| Verification path is adequate for an additive benchmark: build green proves the interceptor was emitted (otherwise the chain would fall through to the throwing `UpdateBuilderPatchExtensions.Set`); dry-run smoke confirms no runtime crashes in any of the 10 methods; 53 existing Patch tests passing confirms the new call site didn't shift interceptor numbering in a way that broke an existing snapshot. No regression risk identified that this misses. | Low | The risk this verification *could* miss — silently-wrong UPDATE results — would be caught by anyone reading the benchmark output if Quarry showed an obviously-wrong row count. Acceptable for a benchmark file. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| Benchmark method naming uses `Library_Cardinality` (e.g. `Raw_OneColumn`) rather than the `Library_Scenario` shape used by `UpdateBenchmarks.cs` (`Raw_UpdateSingleRow`, `Dapper_UpdateSingleRow`, …). The new file omits an action verb. Both shapes are present elsewhere — `ConditionalBranchBenchmarks` uses `Raw_ConditionalQuery`. The cardinality-only suffix is unambiguous within `PatchUpdateBenchmarks` (class name supplies the scenario) and BDN displays the fully-qualified name in reports, so disambiguation is preserved. Minor style drift, not a problem. | Low | Cosmetic; flagged only because the prompt explicitly asks about naming convention. |
| Using-directive grouping differs slightly from `UpdateBenchmarks.cs`: new file omits `Microsoft.Data.Sqlite` (Connection is typed via `BenchmarkBase`, never referenced as `SqliteConnection`) and `SqlKata.Compilers` (compiler is inherited from base). New file adds `Quarry.Benchmarks.Context` for `User.Patch`. All inclusions/exclusions are warranted by what the file actually references. `InsertSingleBenchmarks.cs` also imports `Quarry.Benchmarks.Context` for the same reason. Consistent. | Low | Documents the trimmed using set as intentional. |
| Field naming/visibility follows the sibling pattern: `_iterationEfContext` (instance, lowercase camel underscore) for the EF context recreated per iteration; `_targetId` and the flag fields as `private static` to dodge the source-generator `UnsafeAccessor` bug. The static workaround comment cross-references `UpdateBenchmarks` ("See UpdateBenchmarks for the original reference"), where `UpdateBenchmarks.cs:19-20` itself points at `handoff-bug.md`. One-hop indirection is acceptable. | Low | Confirms the static-field workaround is applied uniformly and the comment chain is intact. |
| `[IterationSetup]` / `[IterationCleanup]` shape matches `UpdateBenchmarks.cs` exactly: setup creates a fresh `EfBenchContext`; cleanup disposes it (null-conditional) and runs a synchronous SQL reset. Same hook order, same `using var cmd` style, same `Connection.CreateCommand()` pattern. | Low | Eliminates "did we change the iteration shape?" as a source of result skew vs `UpdateBenchmarks`. |
| `[Benchmark(Baseline = true)]` is on `Raw_OneColumn` — single baseline per class, repo convention. Matches `UpdateBenchmarks.cs:47` (Raw baseline) and `InsertSingleBenchmarks.cs:32`. | Low | Confirms baseline placement is consistent. |
| Comment density and tone match `UpdateBenchmarks.cs`: a class-level XML summary explaining the scenario; per-section banner comments (`// --- One column potentially set ---`); a multi-line comment block on `EfCore_OneColumn` explaining the 2-round-trip trade-off (mirrors the value-of-fields commentary in `UpdateBenchmarks.cs:15-21`). Tone is informative without being preachy. | Low | Matches house style — no over-commenting and no under-commenting. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| `git diff master --stat` shows three files changed: `_sessions/add-patch-update-benchmarks/plan.md`, `_sessions/add-patch-update-benchmarks/workflow.md`, `src/Quarry.Benchmarks/Benchmarks/PatchUpdateBenchmarks.cs`. No changes to `Quarry.Benchmarks.csproj`, no changes to existing benchmark files, no changes to Quarry library code, no changes to `BenchmarkBase.cs` or infrastructure. Single-commit branch (`45f83da`). Purely additive. | Low | Confirms zero blast radius — running the existing benchmark suite or shipping the library is unaffected. |
