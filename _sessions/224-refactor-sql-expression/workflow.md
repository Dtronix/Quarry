# Workflow: 224-refactor-sql-expression
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #224
pr:
session: 1
phases-total: 5
phases-complete: 4
## Problem Statement
Refactor SqlExpression to dialect-agnostic representation. Currently `ProjectedColumn.SqlExpression` stores pre-built SQL with PostgreSQL-quoted identifiers. A `ReQuoteSqlExpression` post-processing step converts them to the target dialect during `BuildProjection` enrichment. The goal is to store column references in a canonical format and quote at render time in `SqlAssembler.AppendSelectColumns`.

Baseline: 3062 tests all passing (97 Migration, 103 Analyzers, 2862 Quarry.Tests). No pre-existing failures.

## Decisions
- 2026-04-09: Use `{identifier}` curly-brace placeholder as canonical format for column references in SqlExpression. Quote at render time via new `SqlFormatting.QuoteSqlExpression`. Remove `ReQuoteSqlExpression` post-processing. Remove `dialect` from `GetColumnSql`/`GetJoinedColumnSql` and related helper methods. Simplify `ExtractColumnNameFromAggregateSql` to parse `{...}` format.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | IMPLEMENT | Issue #224 loaded, worktree created, baseline green. Design approved (canonical `{identifier}` format). Plan approved (5 phases). |
