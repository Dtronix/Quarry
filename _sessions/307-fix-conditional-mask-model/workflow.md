# Workflow: 307-fix-conditional-mask-model

## Config
platform: github
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #307
pr:

## Problem Statement
Issue #307 — Conditional clause mask model: unconditional Limit/Offset/Distinct +
runtime crash on else-if / multi-clause branches. Combined finding from the
2026-07-07 multi-agent deep review; both defects adversarially verified.

The conditional bitmask model (`if`/`else` clause dispatch, up to 8 bits / 256
variants) produces silent wrong results or runtime crashes on documented usage
patterns, with zero compile-time signal:

**Defect 1 (critical):** Conditional `.Limit()` / `.Offset()` / `.Distinct()` /
`.WithTimeout()` are silently applied unconditionally. `ChainAnalyzer` assigns
mask bits to these sites when nested under `if` (`Parsing/ChainAnalyzer.cs:639-652`,
role mapping `2814-2817`), but (1) `SqlAssembler.AppendPagination`
(`IR/SqlAssembler.cs:1066-1123`) and DISTINCT emission (`SqlAssembler.cs:264,448`)
render into every variant with no mask gating — `hasLimit`/`isDistinct` set
unconditionally (`ChainAnalyzer.cs:1188-1201`), `PaginationPlan` carries no clause
bit; (2) `TransitionBodyEmitter.EmitPagination`/`EmitDistinct`/`EmitWithTimeout`
(`TransitionBodyEmitter.cs:320-386`) never emit `__c.Mask |=`. Verified in
`ConditionalBranchBenchmarks` generated output: all 8 variants contain `LIMIT 25`,
the `Limit_*` interceptor body is a no-op. Consequences: silent truncation when
the branch isn't taken; runtime-valued limit defaults to 0 → zero rows;
`ToDiagnostics` reports the clause inactive while executed SQL contains it.
Comment in `MaskAwareTerminalBindingTests.cs:362` suggests pagination was designed
unconditional — intent never surfaced as error/warning.

**Defect 2 (critical):** else-if chains and multi-clause branches produce
unenumerated masks → `null` SQL dispatched at runtime. Branch groups keyed by
condition text only (`ChainAnalyzer.cs:646-652`; `UsageSiteDiscovery.
DetectNestingContext` ~`1983-2024` assigns only innermost if's condition text);
groups with a `MutuallyExclusive` site and ≥2 members enumerate "exactly one bit
set" (`EnumerateMaskCombinations`, `ChainAnalyzer.cs:2664-2722`).
Repro shape 1 — `if (a) Where(X); else if (b) Where(Y); else Where(Z);`: X is
independent ("a"), Y/Z share "b" → masks {2,3,4,5} emitted, `_sql[1]` is `null!`;
runtime branch `a` → Mask=1 → null CommandText → provider throws. Repro shape 2 —
two Wheres in one if-branch of if/else: both share one condition text → both-bits
mask never enumerated (masks {1,2,4}, runtime Mask=3 → null); enumerated variants
1 and 2 are also semantically wrong (each has only one of the two predicates).
Depth guard (`MaxIfNestingDepth = 2`) doesn't demote a one-level else-if chain.
`CarrierEmitter.cs:429-441` fills gaps with `null!`; terminal dispatch has no null
check.

**Test gap:** `CrossDialectConditionalMaskTests` / `CrossDialectConditionalUpdateTests`
cover only single-clause `if` and single-clause `if`/`else` — no else-if, no
multi-clause branch, no conditional Limit/Distinct.

**Issue work items:** (1) decide policy for conditional modifiers — honor the bit
(gate rendering per mask, set bit in emitters, bind params only when active,
consistent ToDiagnostics) vs reject with a new QRY error (smaller safe change,
doc must exclude modifiers from the feature); (2) model branch groups structurally
via syntax ancestry (one group per if/else-if/else cascade, per-arm enumeration
with all of an arm's bits set together, multi-clause arms supported); (3) defense
in depth — runtime guard on `_sql[__c.Mask] == null` with actionable message,
and/or generation-time validation that reachable masks ⊆ enumerated masks
(demote to QRY032 otherwise); (4) regression tests (conditional Limit false-branch
full row set, runtime-valued limit, 3-arm else-if down each arm, two clauses in
one branch both ways, ToDiagnostics consistency); (5) llm.md doc update stating
which chain methods participate in conditional masking.

Baseline test status: all green at 7bb0e35 — Quarry.Tests 3281 passed, Quarry.Migration.Tests 201 passed, Quarry.Analyzers.Tests 146 passed, 0 failed, 0 skipped (Docker available; all 4 dialects executed). Pre-existing CS0219 `__colShift` warnings from CrossDialectUpdateTests interceptors — warnings only, known issue.

Note: branched from 7bb0e35; the #305 fix (PR #306, AssembledPlan offset walk)
is NOT in this branch — suspended at FINALIZE in a parallel worktree. Possible
rebase later if #306 merges first.

## Decisions
- 2026-07-13: **Defect 1 policy — honor the bit.** Conditional Limit/Offset/Distinct become
  fully functional: per-mask gating of LIMIT/OFFSET/DISTINCT rendering, mask-bit set in
  EmitPagination/EmitDistinct, pagination params bound only when active, ToDiagnostics/manifest
  consistent. WithTimeout excluded: already runtime-correct; just stop assigning it a bit.
- 2026-07-13: **Defect 2 — structural branch grouping.** DetectNestingContext identifies each
  if/else-if/else cascade (and ternary) via syntax ancestry: group key = cascade head span,
  plus arm index / arm count / has-final-else. Enumeration is per-arm (all of an arm's bits
  set together), including the no-arm mask when the cascade lacks a final else or has arms
  without chain sites. Else-if chains and multi-clause branches become supported.
- 2026-07-13: **Design approved by user** (Parts A/B/C as presented; proceed to PLAN).
- 2026-07-13: **Defense in depth — both layers.** Generated terminal throws actionable
  InvalidOperationException on null _sql[mask]; generation-time validation that reachable
  masks ⊆ enumerated masks, demoting to QRY032 on violation.

## Working Notes
- **DESIGN verification (2026-07-13): all issue #307 claims confirmed by source read.**
  - Bit assignment: `ChainAnalyzer.cs:613-653` — sites deeper than terminal get bits via
    `MapInterceptorKindToClauseRole` (`:2792`), which includes Limit/Offset/Distinct/
    WithTimeout (`:2814-2817`). Groups keyed by `condInfo.ConditionText` only (`:646`).
  - `EnumerateMaskCombinations` (`:2664-2722`): group with any `MutuallyExclusive` member
    and Count≥2 → exactly-one-bit-set; group of 1 → independent even if MutuallyExclusive.
    Confirms both defect-2 repro shapes (else-if → {2,3,4,5}; two-clauses-in-branch → {1,2,4}).
  - `DetectNestingContext` (`UsageSiteDiscovery.cs:1983-2024`): innermost if's condition
    text + total depth + BranchKind (MutuallyExclusive if passed through else / if has else /
    parent is else-clause; ternary always MutuallyExclusive). No structural cascade identity.
  - `hasLimit`/`hasOffset`/`isDistinct` set unconditionally when site exists
    (`ChainAnalyzer.cs:1188-1201`); rendering: `SqlAssembler.RenderSelectSql:264` DISTINCT
    ungated, `AppendPagination:1066` ungated (contrast `AppendWhereForMask:277` which gates
    by mask). Batch paths (`RenderSelectSqlBatch`/`RenderDeleteSqlBatch`, `canBatch` at `:89`)
    share prefix/middle/suffix — pagination in suffix, also ungated.
  - `TransitionBodyEmitter.EmitPagination/EmitDistinct/EmitWithTimeout` (`:320-386`): no
    `Mask |=` (contrast `CarrierEmitter.cs:333/464/487` for Where/entry/param-bind sites).
  - Dispatch: `CarrierEmitter.cs:1173` `var sql = _sql[__c.Mask];` — no null check; `_sql`
    gaps filled `null!` (`:429-441`).
- **New finding — WithTimeout is already runtime-correct when conditional:** carrier field
  is `TimeSpan?` (`CarrierAnalyzer.cs:219`), terminal uses `__c.Timeout ?? __ctx.DefaultTimeout`
  (`CarrierEmitter.cs:983-989`, `TerminalBodyEmitter.cs:564-566`). Branch not taken → null →
  default. Its conditional bit only doubles the SQL variant table with identical entries.
  Correct fix for WithTimeout under either policy: stop assigning it a bit.
  Limit/Offset carrier fields are non-nullable (default 0) and literal limits are baked into
  SQL — genuinely broken when conditional. Distinct is baked into SQL — same.
- QRY032 = existing "chain not analyzable" compile error; `MakeRuntimeBuildChain` demotion
  is the established fail-loud path (e.g. depth > MaxIfNestingDepth at `ChainAnalyzer.cs:636`).
- `NestingContext` (`RawCallSite.cs:565`): ConditionText/NestingDepth/BranchKind, value-equal.
  Structural grouping needs new fields (cascade group key = head-if span, arm index, arm count,
  has-final-else) — produced in `DetectNestingContext`, which has the syntax node.
- Mask-gated per-mask rendering pattern to copy for pagination/distinct gating:
  `GetActiveTerms(terms, mask)` + offset-from-all-terms walk (see post-union WHERE,
  `SqlAssembler.cs:310-319`).
- **Fragile positional bit protocol:** `AssembledPlan.GetClauseEntries` (`:145-170`)
  re-derives site→bit by walking ClauseSites and assigning the next ConditionalTerm to each
  site with `NestingContext != null` — but ChainAnalyzer's bit loop ALSO skips
  `relativeDepth <= 0` and null-role sites. The two walks agree only by accident of current
  role coverage. Any change to bit assignment (e.g. skipping WithTimeout) MUST keep both
  sides in sync — plan: add `SiteUniqueId` to `ConditionalTerm` and match by ID, killing the
  positional coupling (QueryPlan/ConditionalTerm equality must include it for incremental
  caching correctness).
- Pagination params are NOT in ChainParameters — carrier binds them at slot
  `ChainParameters.Count`(+1) (see AppendPagination comment re #303). Mask-gating their
  binding on MySQL must extend the bind-order/conditional-map handling for those virtual
  slots (`PipelineOrchestrator.RewriteMySqlBindMarkers`, `BuildParamConditionalMap`).
- `NeedsDistinctOrderByWrap(plan, mask, config)` already takes mask — conditional DISTINCT
  slots into it; batch path: simplest correct approach is `canBatch = false` when pagination
  or distinct is conditional (per-mask fallback already exists for the distinct wrap).
- Runtime guard must cover BOTH failure modes: unenumerated mask ≤ maxMask → `null!` entry;
  unenumerated mask > maxMask → IndexOutOfRange. Emit bounds check + null check → actionable
  InvalidOperationException.

## Suspend State

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-13 | INTAKE | Loaded issue #307, created worktree + branch from 7bb0e35, baseline suite started. #305 workflow suspended at FINALIZE in parallel worktree. |
| 2026-07-13 | DESIGN, PLAN | Baseline green (3628 tests). Verified all issue claims in source; found WithTimeout already runtime-correct (bit is waste) and the fragile positional bit protocol. User approved: honor Limit/Offset/Distinct bits, structural cascade grouping, both defense layers. 7-step plan.md approved; entering IMPLEMENT. |
