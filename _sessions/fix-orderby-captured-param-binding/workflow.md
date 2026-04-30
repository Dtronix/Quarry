# Workflow: fix-orderby-captured-param-binding

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: DESIGN
status: active
issue: discussion
pr:
session: 1
phases-total: 5
phases-complete: 3

## Problem Statement
Generator-emitted clause interceptors silently drop captured-variable extraction for OrderBy/ThenBy/GroupBy/Join when the clause's lambda contains captured locals (e.g. `OrderBy((u, o) => o.Total + bias)`). Symptom: `CS0649 Field 'Chain_N.Px' is never assigned to, and will always have its default value 0` on the carrier P-fields backing those parameters. Root cause is symmetric across four emitters:

1. **`JoinBodyEmitter.EmitJoinedOrderBy`** — primary trigger of the CS0649 warnings on `CrossDialectDistinctOrderByTests.g.cs`.
2. **`ClauseBodyEmitter.EmitOrderBy`** — same shape, latent.
3. **`ClauseBodyEmitter.EmitGroupBy`** — same shape, latent.
4. **`JoinBodyEmitter.EmitJoin`** — same shape on both first-in-chain and chained-second-join paths, latent.

Two parallel defects per emitter:
- Lambda parameter hardcoded to `_`, so `func.Target` referenced by carrier extraction-plan emission is unreachable.
- Generic-key carrier path emitted only `cast → mask bit → return`, completely bypassing `EmitCarrierClauseBody` and skipping the per-clause extraction plan.

Net effect: the carrier holds `__ExtractVar_<var>_<i>` accessor methods and a `Px` field, the SQL references `@px`, but no interceptor ever calls the extractor or writes to `Px`. The currently failing test (`Distinct_OrderBy_NonProjectedExprWithCapturedParam_ThreadsParamIndex`) only "passes" because `bias = 0.00m` happens to match the `default(decimal)` the parameter binds. Any non-zero captured value would silently bind 0 — class of silent-correctness bug.

### Baseline test status (origin/master at b0f8d37, fix applied)
- `Quarry.Tests`: 3,084 / 3,084 passing
- `Quarry.Analyzers.Tests`: 146 / 146 passing
- No pre-existing failures.

### Pre-fix evidence (origin/master)
Build emitted:
```
warning CS0649: Field 'Chain_6.P1' is never assigned to, and will always have its default value 0
```
Once for each of the four `*Db.Interceptors.*CrossDialectDistinctOrderByTests.g.cs` files (SsDb, MyDb, PgDb, TestDbContext).

## Decisions

### 2026-04-30 — Scope of fix
**Decision:** Fix all four emitters in one workflow (EmitJoinedOrderBy, EmitOrderBy, EmitGroupBy, EmitJoin) rather than only the two that triggered CS0649.
**Rationale:** Identical bug shape, identical fix shape. Splitting yields four nearly-identical PRs over a class of latent silent-correctness bugs.

### 2026-04-30 — Fix mechanism
**Decision:** For each affected emitter, (a) detect captures via `clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath)`, (b) name the lambda `func` instead of `_` when captures present, (c) emit `[UnconditionalSuppressMessage("Trimming", "IL2075", …)]` attribute conditionally, (d) route the generic-key carrier path through `CarrierEmitter.EmitCarrierClauseBody` (which handles extraction + binding + masked return) so it stops being a divergent code path. For `EmitJoin`'s not-first-in-chain branch, also bind params via `EmitCarrierClauseBody` when `siteParams.Count > 0`.
**Rationale:** Mirrors the already-correct pattern in `EmitWhere`, `EmitJoinedWhere`, `EmitHaving`, `EmitModificationWhere`, `EmitSelect`, `EmitJoinedSelect`, `EmitUpdateSetAction`. Funneling through `EmitCarrierClauseBody` collapses two divergent paths (keyType-known vs generic-key) into one, eliminating the class of bug.

### 2026-04-30 — Regression test strategy
**Decision:** Tighten the existing `Distinct_OrderBy_NonProjectedExprWithCapturedParam_ThreadsParamIndex` test by changing `bias = 0.00m` to a non-zero value, AND assert the runtime ordering reflects the captured value (currently it doesn't because `default(decimal) == 0` masks the bug). Plus add generation-level assertions in `CarrierGenerationTests` that the OrderBy interceptor body contains the `__ExtractVar_<var>_<i>` extraction call AND the `__c.P{n} = <var>!;` assignment.
**Rationale:** Belt-and-suspenders. Runtime test catches silent-default at execution time; generation test catches missing emission at compile time. Either alone could be defeated; both together are robust.

### 2026-04-30 — Coverage for latent paths
**Decision:** Add three new generation-level tests in `CarrierGenerationTests`, one per latent path: (i) single-table `OrderBy` with a captured local, (ii) `GroupBy` with a captured local in the key expression, (iii) `Join` with a captured local in the join condition. Each asserts the interceptor body contains the expected extraction + assignment text. No new `CrossDialect*` runtime tests for these paths.
**Rationale:** Bug shape is identical across emitters; generation-level tests pinpoint the failure mode (missing emission) without requiring data fixtures. Avoids 12-cell SQL matrix churn (4 dialects × 3 paths).

### 2026-04-30 — Generator self-check (QRY037)
**Decision:** Add a new diagnostic `QRY037` of severity **Error** that fires when a carrier P-field has no `__c.P{i} = ...` assignment in any emitted interceptor body. Implementation: track assigned P-indices per carrier during emission via a `Dictionary<carrierClass, HashSet<int>>` populated by every binding-emit helper (`EmitCarrierClauseBody`, `EmitCarrierParamBind`, `EmitCarrierChainEntry`, `EmitCarrierParamBindings`, `EmitCollectionContainsExtraction`). After each chain's interceptors are emitted, `FileEmitter` post-checks each carrier's `CarrierPlan.Fields` (filtered to P-fields) against the assigned set and appends a `Models.DiagnosticInfo` for any gap. `QuarryGenerator.GetDescriptorById` registers QRY037 alongside existing entries. Source location: chain-root site (`assembled.ExecutionSite.Bound.Raw` line/column).
**Rationale:** Self-check that fails fast on regressions of this exact class. Error severity prevents shipping silently-wrong queries. Error message includes carrier class name + missing P-index + chain location for clear diagnosis.
**Edge case decision:** Any-branch assignment counts; do NOT require per-mask coverage. Rationale: keeps the rule simple and avoids false positives on legitimate conditional chains where the SQL guarantees the parameter is only referenced when its bit is set.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE     | DESIGN    | Stashed pre-applied fix, created worktree off origin/master, popped stash, ran baseline (3,084 + 146 passing). Scaffolded workflow.md. |
