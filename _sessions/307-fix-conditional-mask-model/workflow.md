# Workflow: 307-fix-conditional-mask-model

## Config
platform: github
base-branch: master

## State
phase: DESIGN
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

## Working Notes

## Suspend State

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-13 | INTAKE | Loaded issue #307, created worktree + branch from 7bb0e35, baseline suite started. #305 workflow suspended at FINALIZE in parallel worktree. |
