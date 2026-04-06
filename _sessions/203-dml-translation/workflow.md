# Workflow: 203-dml-translation

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REVIEW
status: active
issue: #203
pr:
session: 2
phases-total: 8
phases-complete: 8

## Problem Statement
The Quarry.Migration SQL-to-chain translator (`ChainEmitter`) currently only handles `SELECT` statements. Dapper calls using `ExecuteAsync` with `DELETE`, `UPDATE`, or `INSERT` SQL are reported as QRM003 (unconvertible). Quarry has full DML support via the chain API (`Delete()`, `Update().Set()`, `Insert()`), so these should be translatable.

Baseline: 2957 tests passing (79 migration + 103 analyzer + 2775 main), 0 pre-existing failures.

## Decisions
- 2026-04-05: Scope includes DELETE, UPDATE, and INSERT (all three DML types)
- 2026-04-05: INSERT emitted as comment: `// db.Users().Insert(entity).ExecuteNonQueryAsync()` with TODO, since Quarry INSERT takes entity objects, not column/value pairs
- 2026-04-05: AST uses base SqlStatement class — SqlSelectStatement, SqlDeleteStatement, SqlUpdateStatement, SqlInsertStatement all inherit from it; SqlParseResult.Statement becomes SqlStatement?
- 2026-04-05: UPDATE SET uses single .Set() block: `.Set(u => { u.Col1 = val1; u.Col2 = val2; })`
- 2026-04-05: DELETE/UPDATE without WHERE: emit chain with .All() but add diagnostic warning about missing WHERE clause

## Suspend State
- Current phase: REVIEW (mid-classification)
- WIP commit: (none — working tree clean, all 8 implementation phases committed)
- Test status: all 2987 tests passing (89 migration + 103 analyzer + 2795 main)
- In progress: Presenting review.md classifications to user via AskUserQuestion. User interrupted with "handoff" before answering.
- Immediate next step: Resume by reading review.md and handoff.md, verify Finding 3 (DML analyzer integration) by reading DapperMigrationAnalyzer.cs, then re-present classifications to user
- Proposed classifications (see handoff.md for details):
  1. UPDATE computed expression test → (B) Gap, address now
  2. DELETE with alias test → (B) Gap, address now
  3. DML analyzer integration → (C) Separate issue — NEEDS VERIFICATION (may actually be covered by existing DapperMigrationAnalyzer routing)
  4. TranslateInsert table-lookup duplication → (D) Not valid
- Unrecorded context: Review agent's finding about "no analyzer handles ExecuteAsync" may be inaccurate. DapperMigrationAnalyzer routes all Dapper calls through ChainEmitter.Translate(), which now handles DML. Need to verify on resume.

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #203 loaded, worktree created, baseline green (2957 tests) |
| 1 | DESIGN | PLAN | Design confirmed: DELETE+UPDATE+INSERT(comment), base SqlStatement, single .Set() block, .All()+warning for no-WHERE |
| 1 | PLAN | IMPLEMENT | 8-phase plan approved |
| 1 | IMPLEMENT | REVIEW | All 8 phases complete, 2987 tests passing (30 new) |
| 1 | REVIEW | REVIEW | Review agent produced review.md; suspended mid-classification per user request |
| 2 | REVIEW |  | Resumed: worktree recreated from origin/203-dml-translation, baseline re-verified |
