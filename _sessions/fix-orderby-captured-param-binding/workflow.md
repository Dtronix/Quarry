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
phases-complete: 5

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

### 2026-04-30 — CS0649 coverage gap motivates QRY037 (revisited during IMPLEMENT)
**Finding:** CS0649 does NOT fire on every unassigned carrier P-field. `CarrierEmitter.EmitCarrierClass` (line 385) emits `internal {Type} P{i} = null!;` for non-nullable reference-type fields (to silence CS8618). The C# rule: any explicit initializer counts as "assigned" for CS0649. Therefore CS0649 covers value-type and nullable-ref-type captures (caught the original `decimal bias` bug) but is silenced for non-nullable reference-type captures (`string`, custom classes, etc.). A `string keyword` capture pre-fix would have shipped silently — null binding at runtime, no compile-time signal.
**Implication:** QRY037 closes a real coverage hole, not a redundant check. The plumbing cost (`CarrierAssignmentRecorder` threaded through ~14 methods in `CarrierEmitter` / `ClauseBodyEmitter` / `JoinBodyEmitter`) is paying for class-of-bug coverage, not just the value-type subset.
**Decision confirmed:** Implement QRY037 with during-emission tracking as planned. Implementation strategy: introduce internal `CarrierAssignmentRecorder` class; add optional `CarrierAssignmentRecorder? recorder = null` parameter to every emit method that writes `__c.P{i} = ...` and to every emit method that delegates to one. Default `null` preserves callsite ergonomics outside `FileEmitter`'s orchestration (e.g., during unit tests that call emit helpers directly).

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE     | DESIGN    | Stashed pre-applied fix, created worktree off origin/master, popped stash, ran baseline (3,084 + 146 passing). Scaffolded workflow.md. |
| 1 | DESIGN     | PLAN      | Iteratively clarified: regression test strategy (tighten existing + generation assertions), latent-path coverage (generation tests only), QRY037 diagnostic (Error severity, during-emission tracking). |
| 1 | PLAN       | IMPLEMENT | Authored plan.md with 5 phases. CS0649 coverage gap analysis surfaced during DESIGN motivated keeping QRY037 in scope. |
| 1 | IMPLEMENT  | (active)  | Phase 1 (fix) + Phase 2 (tightened test) + Phase 3 (OrderBy generation test) + Phase 4 (latent-path generation tests) + Phase 5 (QRY037 self-check) committed. Final state: 3,095 + 146 = 3,241 tests passing. |
