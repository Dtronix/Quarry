# Workflow: carrier-codegen-efficiency
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
phases-complete: 3
## Problem Statement
Improve efficiency of generated carrier class code across multiple dimensions:
1. **Carrier deduplication** — structurally identical carriers produce separate classes (30-50% bloat)
2. **Dead `var __c` cast** — emitted in parameterless clause interceptors (unused)
3. **Batch insert string allocs** — `"@p" + __paramIdx` per-row allocations in hot loop
4. **SQL mask re-rendering** — full SQL re-rendered per mask variant (compile speed)
6. **Duplicate reader lambdas** — identical static lambdas per terminal
7. **`_sqlCache` not readonly** — prevents JIT optimization
8. **SQL Server boolean negation bug** — `NOT ([bit_col])` is invalid SQL Server syntax

Baseline: 2912 tests pass, 0 failures.

## Decisions
- 2026-04-10: Item 1 (carrier dedup) → create tracking issue, not this PR. Union-interfaces approach noted.
- 2026-04-10: Item 4 (SQL mask re-rendering) → defer to separate PR, create tracking issue.
- 2026-04-10: Item 2 (dead `var __c`) → fix in CarrierEmitter/ClauseBodyEmitter, conditional on params/mask.
- 2026-04-10: Item 3 (batch param names) → integrate existing `ParameterNames.AtP`/`Dollar` into TerminalBodyEmitter.
- 2026-04-10: Item 6 (reader lambda dedup) → emit shared `_reader` field on carrier class.
- 2026-04-10: Item 7 (`_sqlCache` readonly) → simple `readonly` addition.
- 2026-04-10: SQL Server bool negation → fix at binder level (SqlExprBinder.cs), emit `col = 0`/`FALSE`.

## Suspend State
- Phase: IMPLEMENT, phase 0/5, before starting phase 1
- In progress: Nothing — plan approved, implementation not yet started
- Next step: Begin Phase 1 — fix SQL Server boolean negation in SqlExprBinder.cs (line 139-145)
- WIP commit: 747fdcf
- Test status: All 2912 tests passing (baseline)
- Unrecorded context: None — all design decisions recorded above

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT | Created branch, baseline green, designed & planned 5 phases, suspended before impl |
| 2 | IMPLEMENT | | Resumed from suspend, continuing implementation |
