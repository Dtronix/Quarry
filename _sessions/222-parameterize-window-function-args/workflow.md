# Workflow: 222-parameterize-window-function-args
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REMEDIATE
status: active
issue: #222
pr:
session: 2
phases-total: 5
phases-complete: 5
## Problem Statement
Window function methods (`Sql.Ntile`, `Sql.Lag`, `Sql.Lead`) emit non-column arguments (offset, default value, bucket count) using raw `.ToString()` on the C# syntax expression. This embeds C# source text directly into SQL, producing invalid SQL for non-literal expressions (e.g., variables, C# suffixed literals like `0m`).

The same `.ToString()` pattern exists in `GetAggregateInfo` for aggregate function column arguments.

The fix needs to:
1. Detect whether the expression is a compile-time constant (literal) vs. a runtime value (variable/captured)
2. For literals: emit the literal value directly (stripping C# suffixes like `m`, `L`, etc.)
3. For variables: parameterize the value (add to the carrier's parameter list)

Baseline: All 3062 tests pass (97 Migration, 103 Analyzers, 2862 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-09: Scope = full fix (Part 1 literal/constant + Part 2 runtime variable parameterization). Covers Ntile, Lag, Lead in both single-entity and joined paths.
- 2026-04-09: Use `@__proj{N}` as local projection parameter placeholders, remapped to `@p{globalIndex}` in ChainAnalyzer.
- 2026-04-09: Add optional `SemanticModel?` to `AnalyzeJoinedSyntaxOnly` so joined path can use `GetConstantValue` for const variables.
- 2026-04-09: Literal extraction uses `SemanticModel.GetConstantValue()` as primary; falls back to `LiteralExpressionSyntax.Token.Value` when no SemanticModel.

## Suspend State
- Phase: IMPLEMENT, phase 3/5 (ChainAnalyzer parameter merging) not yet started
- In progress: Nothing — Phase 2 just committed cleanly
- Immediate next step: Implement Phase 3 — merge projection params into global param list in ChainAnalyzer
- WIP commit: none (all work committed)
- Test status: all 3090 tests passing
- Unrecorded context: None — all decisions in Decisions section, all code committed

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT | Issue #222 loaded, worktree created, baseline green. Design: full parameterization approach selected. Phases 1-2 implemented (literal fix + call chain threading + discovery enrichment). Suspended for context management. |
| 2 | IMPLEMENT | | Resumed from suspend. Baseline: 3090 tests passing. Starting Phase 3. |
