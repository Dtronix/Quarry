# Workflow: 305-fix-cte-outer-param-assignment

## Config
platform: github
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #305
pr: #306

## Problem Statement
QRY037 build failure for CTE chains that combine a captured parameter inside the
`With<T>(...)` lambda with a captured parameter in an outer clause (e.g. `Where`
after `FromCte<T>()`). The generated carrier declares the outer parameter's field
(`P1`) but no clause interceptor body assigns it, so the generator's self-check
(`DiagnosticDescriptors.CarrierParameterFieldUnassigned`) fails the build. The
chain shape is valid SQL on all four dialects but is currently unusable.

Repro (from issue):
```csharp
decimal threshold = 100.00m;
int minId = 2;
var rows = await db.With<Order>(orders => orders.Where(o => o.Total > threshold))
    .FromCte<Order>()
    .Where(o => o.OrderId >= minId)   // carrier field never assigned -> QRY037
    .Select(o => (o.OrderId, o.Total))
    .ExecuteFetchAllAsync();
```

Suspected root cause (from issue): CTE parameter rebasing in `ChainAnalyzer`
prepends inner-lambda params to the outer parameter list with sequential
`GlobalIndex`, but the clause-interceptor parameter-assignment emission does not
apply the rebase offset for outer clauses — with zero inner params the offset is
zero, which is why outer-only chains work.

Baseline test status: all green at 7bb0e35 — Quarry.Tests 3281 passed, Quarry.Migration.Tests 201 passed, Quarry.Analyzers.Tests 146 passed, 0 failed, 0 skipped (Docker available; all 4 dialects executed). Build emits pre-existing CS0219 `__colShift` warnings from CrossDialectUpdateTests interceptors — warnings only, addressed by an unrelated uncommitted change in the main repo.

## Decisions
- 2026-07-02: Root cause fixed at `AssembledPlan.BuildSiteParamsMap`/`BuildParamConditionalMap`
  (offset walk skips CteDefinition entries), NOT in ChainAnalyzer/SqlAssembler as the issue
  guessed — those are correct. CTE match by short name via `CteNameHelpers.ExtractShortName`,
  mirroring `TransitionBodyEmitter` (first match; duplicates already QRY082, failed CTEs
  already QRY080).
- 2026-07-02: Design + 3-step plan **provisionally approved** (user AFK at AskUserQuestion
  timeout; proceeded per autonomous-operation guidance with the recommended option). User may
  revisit with "go back to DESIGN".
- 2026-07-02: Tests: (1) generation guard — no QRY037 + interceptor assigns P0 and P1;
  (2) cross-dialect SQL + execution in CrossDialectCteTests; (3) MySQL bind-order pin
  `ParameterizedCteInnerAndOuterParams_OnMySQL_BindsInnerBeforeOuter` exactly as specified
  in issue #305.

- 2026-07-02: REVIEW classifications applied as recommended (user AFK at prompt; same
  autonomous-default as DESIGN): F1→D, F2→B, F3→B, F4→B, F5→C. Final: 0A/3B/1C/1D.
  F5's issue creation deferred until user confirmation (outward-facing action).

## Working Notes
- **Root cause (confirmed by code trace, DESIGN 2026-07-02):** not in ChainAnalyzer or
  SqlAssembler — both are correct. `ChainAnalyzer` inserts CTE inner params into the
  plan's global parameter list at `CteDef.ParameterOffset` and remaps outer clause
  params to follow them (`ChainAnalyzer.cs:977` runs after the CTE loop advanced
  `paramGlobalIndex`). SQL rendering rebase is also correct (`SqlAssembler` uses
  `paramBaseOffset: cte.ParameterOffset`). The defect is in
  `AssembledPlan.BuildSiteParamsMap` (`AssembledPlan.cs:187`) and
  `BuildParamConditionalMap` (`AssembledPlan.cs:238`): both walk clause entries
  accumulating `globalParamOffset`, but a `CteDefinition` entry has `Clause == null`
  (CteDefinition is not clause-bearing — `CallSiteTranslator.IsClauseBearingKind`,
  line 779) and matches no offset-advance case, so it contributes 0 to the offset
  despite occupying `InnerParameters.Count` slots in `ChainParameters`. Every
  param-bearing clause after the CTE then gets an offset short by the inner-param
  count. `CarrierEmitter.EmitCarrierClauseBody` (line 264, 300) uses that offset to
  emit `__c.P{offset+i} = ...`, so the outer Where writes `P0` (double-assigning the
  CTE's slot) and `P1` is never assigned → QRY037. With zero inner params the offset
  error is zero — exactly why outer-only and inner-only chains work.
- The CTE-slot assignments themselves don't use this map: `TransitionBodyEmitter.
  EmitCteDefinition` uses `cteDef.ParameterOffset` directly (name-matched via
  `CteNameHelpers.ExtractShortName`, first-match; duplicates rejected by QRY082).
  The fix should mirror that name-matching.
- `BuildParamConditionalMap` has the same walk and the same gap. Its consumer
  (`PipelineOrchestrator.RewriteMySqlBindMarkers:641`) treats missing keys as
  unconditional/active, so unmapped CTE slots are fine, but misaligned keys would
  mislabel slots for conditional-clause + parameterized-CTE chains (currently
  unreachable — QRY037 blocks all such shapes — but must be fixed consistently).
- All emitter paths funnel through `AssembledPlan.GetSiteParams` (ClauseBodyEmitter,
  JoinBodyEmitter, CarrierEmitter, TerminalEmitHelpers) — a single fix point in
  AssembledPlan covers every consumer.
- Generator llm.md's QRY table skips QRY037 (jumps QRY036→QRY040) — pre-existing doc
  gap, out of scope here.
- **Gotcha (REMEDIATE):** restoring a source file via `mv backup.cs file.cs` preserves the
  backup's OLD mtime — msbuild then treats the project as up-to-date and keeps the stale
  assembly built from the intermediate (mutated) source. Symptom here: the F2 QRY048 pin
  passed, then failed after the bite-verification cycle, because the generator DLL still
  had the conditional-map branch removed. Fix: `touch` the file (or `git checkout --`,
  which sets a fresh mtime) before rebuilding. When bite-verifying by mutating source,
  prefer `git stash` / `git checkout -- <file>` over manual backup files.
- Main repo (Quarry-master) has an uncommitted, unrelated change to
  `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` (Patch-chain `__colShift`
  CS0219 avoidance). It is NOT part of this branch; worktree branched from clean
  HEAD 7bb0e35. May cause a merge conflict later if it lands while this branch
  also touches CarrierEmitter.

## Suspend State

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-02 | INTAKE, DESIGN, PLAN | Loaded issue #305, created worktree + branch, baseline all green (3628 tests). Traced root cause to AssembledPlan offset walk. Design+plan provisionally approved (user AFK); wrote plan.md; entered IMPLEMENT. |
| 2026-07-02 | IMPLEMENT, REVIEW, REMEDIATE | 3 plan steps committed, suite green each step. Review: 5 findings (0A/3B/1C/1D), B-items fixed and bite-verified, F5 issue deferred pending user confirm. PR #306 created; awaiting CI. |
