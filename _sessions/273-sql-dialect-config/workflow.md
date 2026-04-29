# Workflow: 273-sql-dialect-config

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #273
pr:
session: 1
phases-total: 4
phases-complete: 4

## Problem Statement
Quarry's generator threads a flat `SqlDialect` enum through every emit path. There is no way to express per-context dialect-mode flags (e.g. MySQL `sql_mode`, PG `standard_conforming_strings`).

Immediate defect: emitted `LIKE '...%' ESCAPE '\'` fails with MySqlException 1064 on default-mode MySQL because backslash is a string-literal escape with default `sql_mode`. PR #271 papered over this in tests by configuring the container with `NO_BACKSLASH_ESCAPES` in `--sql-mode`. Real consumers on stock MySQL 8 would hit the 1064.

Two coupled changes:
1. **Refactor:** Replace flat `SqlDialect` parameter through generator with `SqlDialectConfig` carrier (dialect + per-context mode flags). Mirror flags as additive properties on `QuarryContextAttribute`.
2. **Fix:** Use the new structure to emit MySQL-portable LIKE SQL regardless of `sql_mode`.

Issue recommends Phase 3.A: switch the LIKE ESCAPE character generator-wide from `\` to `|` (works on every dialect/`sql_mode` combination; lowest cost; no new emit-path branching).

### Test Baseline
All tests green at start: 128 (Analyzers.Tests) + 201 (Migration.Tests) + 3035 (Quarry.Tests) = 3364. No pre-existing failures.

## Decisions

### 2026-04-29 — LIKE-emit fix approach: 3.B (mode-aware)
Branch LIKE pattern + ESCAPE emission on `SqlDialectConfig.MySqlBackslashEscapes` for MySQL. Emit doubled-backslash form when true (matches default MySQL `sql_mode`); ANSI form otherwise. **Why:** emitted SQL matches what consumers would write by hand for their server config; sets up the carrier-flag emit path the issue's other deferred axes will reuse.

### 2026-04-29 — PR scope: refactor + fix together
Land `SqlDialectConfig` carrier refactor AND the LIKE fix in this PR per issue Phases 1–4. **Why:** the carrier is the foundation other `sql_mode` axes will plug into; doing it now while threading sites are fresh is reversible/additive. Phases stay independently committable.

### 2026-04-29 — `SqlDialectConfig` shape: internal sealed record
Per the issue spec. Value equality matches incremental generator caching. **Why:** ContextInfo and BoundCallSite participate in the IR equality cache; record gets correct `Equals`/`GetHashCode`/`with` for free.

### 2026-04-29 — `MySqlBackslashEscapes` default: `true`
Default matches stock MySQL behavior (backslash IS an escape character with default `sql_mode`). Consumers who configure `NO_BACKSLASH_ESCAPES` set it to `false`. **Why:** wrong default is a footgun; matching stock MySQL means consumers who do nothing get parseable SQL on the most common server config.

### 2026-04-29 — Phase 2 attribute scope: only `MySqlBackslashEscapes`
Other `sql_mode` axes (`MySqlAnsiQuotes`, `MySqlOnlyFullGroupBy`, `PgStandardConformingStrings`, `SqlServerQuotedIdentifier`) get separate follow-up issues. **Why:** smallest blast radius; one flag is exercised end-to-end in this PR. Future axes are additive and follow the same shape established here.

### 2026-04-29 — Test container mitigation: keep, add focused default-mode test
Leave `NO_BACKSLASH_ESCAPES` in the main `MySqlTestContainer.cs` `--sql-mode` argument. Add a new focused container helper (`MySqlDefaultModeTestContainer`) and a regression test class (`MySqlBackslashEscapesTests`) that boots a default-mode container and proves the generator fix works. **Why:** existing suite stays green throughout the refactor with no churn; the new focused test is the proof point that the fix actually works on default-mode MySQL.

### 2026-04-29 — Carrier scope: generator-internal only
`SqlDialectConfig` is `internal` to `Quarry.Generator`. `Quarry.Shared` (`SqlFormatting.cs`, parser), `Quarry`, and `Quarry.Migration` keep consuming bare `SqlDialect`. At the boundary where the generator calls into shared formatting helpers, extract `config.Dialect`. **Why:** carrier carries generator-time decisions (per-context mode flags); runtime/shared helpers don't need them. Keeps runtime API surface unchanged.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | REVIEW | 2026-04-29: Loaded issue #273. Worktree/branch created. Baseline 3364 tests green. DESIGN: 7 decisions recorded (3.B mode-aware emit, refactor+fix scope, internal sealed record, MySqlBackslashEscapes default true, attribute-only-MySqlBackslashEscapes-this-PR, keep test mitigation+add focused container, generator-internal carrier). PLAN: 4 phases written and approved. IMPLEMENT: 4 phases committed. Phase 1 introduced SqlDialectConfig carrier (3364 tests green). Phase 2 added MySqlBackslashEscapes attribute+carrier flag (3372 tests). Phase 3 threaded SqlDialectConfig through SqlExprRenderer + SqlAssembler and branched LIKE emit on the flag (3378 tests). Phase 4 added default-mode container + 3 integration regression tests (3381 tests). |
