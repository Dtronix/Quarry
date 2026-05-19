# Workflow: benchmark-double-migration

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
session: 2
phases-total: 5
phases-complete: 2

## Problem Statement

The benchmark suite uses `decimal` for the `Total`/`UnitPrice`/`LineTotal` columns on
`OrderSchema`/`OrderItemSchema`. SQLite stores these as REAL, and
`Microsoft.Data.Sqlite.SqliteValueReader.GetDecimal` is implemented as
`decimal.Parse(GetString(ordinal), NumberStyles.Number | AllowExponent, InvariantCulture)`
— a string allocation plus culture-aware parse per cell. That driver-implementation
quirk contaminates the inter-library comparison: it artificially inflates the cost of
hand-rolled Raw, Quarry's generated reader, and SqlKata's raw fallback, while Dapper's
IL-emitted deserializer sidesteps it by going through the `DbDataReader` indexer
(boxed double → unbox.any → `(decimal)(double)` conversion). The result is that Dapper
appears 19–30% faster than Raw on decimal-column workloads, but the gap is a SQLite
driver implementation choice, not a library-level advantage.

Switching the schema column types to `double` removes the slow path: every library
reads the native REAL→CLR-double via `GetDouble` (or for Dapper, an unbox-only path
with no conversion). Empirical measurements on `CteSimpleBenchmarks` and
`WindowLagBenchmarks` confirmed that with `double`:
- Raw and Quarry converge to within noise (~37µs / ~132µs respectively)
- Quarry tracks Raw within 0.5%
- Dapper drops from "fastest" to ~13–31% slower than Raw because the boxing tax
  is now the dominant cost
- Allocations drop ~35% for Raw/Quarry/SqlKata (per-row string allocations from
  `GetDecimal` disappear)

The migration exposed a separate Quarry generator bug: when the schema column type
changes to `double`, `ProjectionAnalyzer.ResolveAggregateClrType` for
`Sql.Sum(o.Total)` / `Sql.Avg(o.Total)` still falls back to a hardcoded `"decimal"`
default, emitting `Func<Order, decimal>` interceptors when the lambda actually
returns `double`. This breaks compilation of `AggregateSumBenchmarks`,
`AggregateAvgBenchmarks`, and `WindowRunningSumBenchmarks`. User scope decision:
fix this bug as part of the same branch so the disabled files can be restored
and all benchmarks remain runnable.

### Baseline (master = 08d8323, before any changes)
- Quarry.Migration.Tests: 201/201 passed
- Quarry.Analyzers.Tests: 146/146 passed
- Quarry.Tests: 3130/3130 passed
- Pre-existing warnings: NU1903 on `System.Security.Cryptography.Xml` 9.0.0 (two advisories: GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf). Not addressed in this branch.
- Pre-existing benchmark suite buildable on master.

## Decisions

### 2026-05-18 — Scope: migration + generator bug fix
The branch will include both the decimal→double migration AND the fix for
`ProjectionAnalyzer.ResolveAggregateClrType`'s hardcoded `"decimal"` default. This
keeps the benchmark suite buildable and avoids leaving `.disabled` files behind.
Alternative considered: ship the migration alone and file the generator bug as a
separate issue. Rejected because `.disabled` files in the repo are a smell, and the
two changes are tightly coupled (the migration exposed the bug).

### 2026-05-18 — Benchmark integrity, not realism
The benchmark suite measures library overhead, not realistic application workloads.
The benefit of using `decimal` (representative of currency schemas) is outweighed by
the cost: SQLite driver quirks dominate the measurement and obscure the actual
library comparison. The committed comments documenting the GetDecimal cost as a
"driver characteristic" can be removed since the benchmark no longer exercises that
path; the documentation lives in PR/commit history for anyone curious.

### 2026-05-18 — Generator fix: reorder + stricter gate
`ProjectionAnalyzer.ResolveAggregateClrType` calls happen in Stage 1
(UsageSiteDiscovery) — before Quarry regenerates entity classes. The
SemanticModel cannot see the entity (`Order`) at this point, so `o.Total` is an
ErrorType expression. The buggy resolution order is:
- Try 1: SemanticModel argument type — fails on Error type (correct skip)
- Try 2: SemanticModel invocation-return-type — Roslyn's overload resolution
  against an Error-typed argument silently picks `Sum(decimal)` as the "best
  applicable candidate" and returns a non-error `decimal` return type. **Bug:**
  Try 2 succeeds with a fabricated answer.
- Try 3: column lookup from schema (authoritative, would have returned `double`)
  — never reached.

Fix: reorder so column lookup is Try 1 (most authoritative for direct entity
property access), and gate the invocation-return-type fallback so it only runs
when the argument's type was actually resolvable. This makes the resolver robust
to both the current Stage-1 timing AND any future Roslyn behavior change in
overload-resolution heuristics for Error-typed arguments.

### 2026-05-18 — Test coverage: both unit and cross-dialect
Two regression tests will be added — both layers fail today before the fix:
- `Generation/AggregateTypeResolutionTests.cs` — narrow unit-level coverage of
  `ProjectionAnalyzer.ResolveAggregateClrType` semantics: same fluent shape with
  `Col<double>`, `Col<int>`, `Col<long>` to lock down the resolution priority.
- `SqlOutput/CrossDialectAggregateTests.cs` — extends the existing decimal-based
  cross-dialect aggregate tests with a parallel set on a `Col<double>` schema
  so the bug can't reappear at the SQL emission layer either.
- The cross-dialect extension was deferred during implementation when the test
  infrastructure cost (DDL changes across 4 container baselines for a `Col<double>`
  column) was reassessed against the unit-level coverage already in place. The
  unit test exercises the exact bug at the right layer (generator CLR-type
  resolution); the cross-dialect tests would have only re-verified SQL emission,
  which is dialect-independent for this bug. Cross-dialect addition is tracked
  as a follow-up.

### 2026-05-19 — Scope expansion: deeper bug + typed sentinel
While implementing Phase 1, the reorder-and-gate fix on `ResolveAggregateClrType`
alone did **not** make the tests pass. Tracing revealed the actual mechanism is
two-stage:

1. Stage 1 (`AnalyzeSingleEntitySyntaxOnly`) is **syntax-only by design** for the
   incremental-pipeline cache contract: it constructs an empty `columnLookup`,
   so the column-lookup branch in `ResolveAggregateClrType` cannot succeed there.
2. Stage 4 (`ChainAnalyzer.BuildProjection`) walks the produced aggregate columns
   and enriches any whose `ClrType` is still unresolved — but its
   `IsUnresolvedTypeName` check considers `"decimal"` resolved and `"object"`
   unresolved. The hardcoded `"decimal"` default at the Stage 1 call sites for
   Sum/Avg therefore short-circuited the enrichment, silently miscompiling any
   schema where the summed column wasn't `decimal`. `Min`/`Max` already used
   `"object"` and worked correctly via enrichment.

The minimal correctness fix changes Sum/Avg defaults from `"decimal"` to
`"object"`. Confirmed: all 5 new aggregate-type-resolution tests pass, full
suite 3482/3482.

### 2026-05-19 — User pushback: introduce typed unresolved sentinel
Using the bare string `"object"` as an "unresolved — please enrich" sentinel is
a code smell. It collides with the legitimate `"object"` CLR type and is the
reason `IsUnresolvedTypeName` and `IsUnresolvedTypeNameLenient` have to maintain
a confusing strict-vs-lenient distinction. Revised plan introduces a named
sentinel constant in `TypeClassification` (`"?"`, already partially in use by
both Is\* helpers) and migrates the **aggregate call sites** to it. This is
a rename refactor — same runtime behavior — but makes the intent explicit and
removes the lurking ambiguity for future maintainers.

**Out of scope for this branch:** broader migration of the `"object"`
unresolved-sentinel usage across all projection paths, or a full type-safe
`ResolvedClrType` discriminated union. Those would expand the surface area
significantly and are tracked as follow-up work (see plan.md "Known follow-ups").
This branch fixes the aggregate path where the actual bug lives.

## Suspend State

_(no active suspend — session 2 resumed work and completed Phase 1)_

### As of 2026-05-19, end of session 1
**Phase:** IMPLEMENT, Phase 1 (revised — typed sentinel + reorder/gate + tests).
Phase 1 was about to be committed when the user pushed back on the use of
`"object"` as a sentinel. Plan revised to incorporate a typed marker; suspending
before applying the rename.

**In progress:**
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` has the reorder/gate
  applied to `ResolveAggregateClrType` AND the 6 Sum/Avg call sites changed
  from `"decimal"` → `"object"` defaults (4 in `GetSqlAggregateInfo`/
  `GetJoinedAggregateInfo`, 2 in `GetWindowFunctionInfo`, 2 in
  `GetJoinedWindowFunctionInfo` — line refs in handoff.md).
- `src/Quarry.Tests/Generation/AggregateTypeResolutionTests.cs` (NEW, 5 tests,
  all passing).
- All test suites pass at 3482/3482 (baseline 3477 + 5 new aggregate tests).

**Immediate next step:** Apply the typed-marker rename. Specifically:
1. Add `public const string UnresolvedTypeMarker = "?";` to
   `src/Quarry.Generator/Utilities/TypeClassification.cs`. Document it as
   "the canonical sentinel for an unresolved CLR type produced by Stage 1
   syntax-only analysis; ChainAnalyzer's `IsUnresolvedTypeName` check
   recognizes it and triggers enrichment."
2. Replace all 6 `"object"` default-argument strings at the aggregate call
   sites in `ProjectionAnalyzer.cs` (Sum/Avg in `GetSqlAggregateInfo` /
   `GetJoinedAggregateInfo` / `GetWindowFunctionInfo` /
   `GetJoinedWindowFunctionInfo`) with `TypeClassification.UnresolvedTypeMarker`.
3. Leave `"object"` defaults on Min/Max untouched — they live across more
   code paths and migrating them safely is the broader follow-up the user
   wants to defer.
4. Confirm both Is\* helpers already recognize `"?"` (they do — verified by
   reading `TypeClassification.cs:162-194` during DESIGN). No changes needed there.
5. Rerun the 5 unit tests; they must still pass (rename should be behavior-
   preserving).

**WIP commit hash:** `892312d` on branch `benchmark-double-migration`. The full
diff (generator fix + 5 new tests + this session directory) is preserved there.

**Test status:** all 3482 tests passed before suspend. Phase 1 changes
(reorder/gate + decimal→object default flip + 5 new tests) are committed as
WIP; the typed-marker rename is the only remaining work for Phase 1.

**Unrecorded context that won't survive context loss:**
- The user's question on the previous turn (and answered in the suspend
  response) made clear they want the sentinel made explicit, not removed
  entirely. The deeper architectural question — passing `EntityRegistry`
  into Stage 1 to remove the need for any sentinel — was acknowledged as
  a valid alternative but rejected for this branch due to cache-invalidation
  blast radius (Stage 1 cache currently keyed on syntax tree only;
  registry-aware Stage 1 would invalidate on every schema change).
- The "Stricter: gate Try 3" option chosen in DESIGN was implemented but
  is functionally inert for the Stage 1 single-entity case (argResolved is
  always false there). It remains valuable as future-proofing against
  Roslyn behavior changes when entities ARE resolvable in the SemanticModel
  (e.g. joined contexts with mixed resolved/unresolved sides).

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-05-18 | 2026-05-19 | INTAKE → DESIGN → PLAN → IMPLEMENT Phase 1 (90% — reorder/gate + decimal→object default + 5 new tests, all green at 3482/3482). Suspended before applying the typed-marker rename pushed back on by user. WIP commit: `892312d`. Plan revised; handoff.md written. |
| 2 | 2026-05-19 |           | Resume from suspend; apply typed-marker rename to finish Phase 1, then Phases 2–5 (benchmark schema migration). |
