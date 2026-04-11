# Workflow: 245-add-codefix-tests
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #245
pr:
session: 1
phases-total: 4
phases-complete: 2
## Problem Statement
Add dedicated code fix tests for EfCoreMigrationCodeFix, AdoNetMigrationCodeFix, and SqlKataMigrationCodeFix. These are the most complex components — they manipulate syntax trees, handle `await` expressions, and add `using` directives. Currently only tested indirectly via analyzer tests.

Baseline: 3178 tests all passing (163 Migration.Tests, 103 Analyzers.Tests, 2912 Quarry.Tests). No pre-existing failures.
## Decisions
- 2026-04-10: Scope includes all four code fixes (EfCore, AdoNet, SqlKata, Dapper) — user chose "All four"
- 2026-04-10: Tests go in `src/Quarry.Migration.Tests/` — one file per code fix, matching existing test organization
- 2026-04-10: End-to-end testing approach — run analyzer to get real diagnostics, then apply code fix via AdhocWorkspace and verify transformed source. This tests the full pipeline.
- 2026-04-10: Reuse existing framework stubs from analyzer/detector/converter tests (each test file duplicates stubs per existing pattern)
- 2026-04-10: Test scenarios per code fix: basic replacement, await preservation, using directive insertion, non-fixable diagnostic rejection (QRM0x3). ADO.NET also tests TODO comment. Dapper also tests IsSuggestionOnly rejection.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Loaded issue #245, created worktree, baseline 3178 tests all green |
| 1 | DESIGN | PLAN | Explored code fixes, stubs, test patterns. All four code fixes in scope. E2E test approach. |
| 1 | PLAN | IMPLEMENT | 4-phase plan approved. One test file per code fix. |
