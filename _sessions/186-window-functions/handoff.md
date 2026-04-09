# Work Handoff: 186-window-functions

## Key Components
- `src/Quarry/Query/IOverClause.cs` — Interface for OVER clause specification (PartitionBy, OrderBy, OrderByDescending)
- `src/Quarry/Query/OverClause.cs` — Runtime dummy implementation (throws at runtime)
- `src/Quarry/Query/Sql.cs` — All window function methods + aggregate OVER overloads added
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` — Window function detection + OVER clause parsing (~437 lines added)
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — ExtractColumnNameFromAggregateSql fix + ReQuoteSqlExpression application
- `src/Quarry.Shared/Sql/SqlFormatting.cs` — ReQuoteSqlExpression utility method
- `src/Quarry.Tests/SqlOutput/CrossDialectWindowFunctionTests.cs` — 18 cross-dialect tests

## Completions (This Session)
- Phase 1: Runtime API (IOverClause + Sql.* methods) — committed
- Phase 2: ProjectionAnalyzer OVER clause analysis — committed
- Phase 3: 14 window function tests — committed
- Phase 4: Enrichment fix (ExtractColumnNameFromAggregateSql + ReQuoteSqlExpression) — committed
- Phase 5: Joined query support + test — committed
- Review: 13 findings analyzed, classified (3B, 2C, 8D)
- Remediation: 3 B-item tests added, 3 C-item issues created (#222, #223, #224)
- Manifest files regenerated for new API
- PR #225 created, CI passing

## Previous Session Completions
N/A — first session

## Progress
All 5 implementation phases complete. Review and remediation complete. PR #225 created and CI green.

## Current State
Awaiting user confirmation to squash merge PR #225. No code changes pending.

## Known Issues / Bugs
- Non-column arguments (NTILE buckets, LAG/LEAD offset/default) use raw `.ToString()` — tracked in #222
- Table aliases inside SqlExpression are unquoted in joined contexts — pre-existing, affects aggregates too

## Dependencies / Blockers
None. PR is ready to merge.

## Architecture Decisions
- OVER clause lambdas are NOT inner chains — purely syntactic analysis in ProjectionAnalyzer
- Column references in OVER clause reuse the outer Select lambda's column lookup
- Window function SQL is stored in ProjectedColumn.SqlExpression (same as aggregates)
- ReQuoteSqlExpression converts discovery-dialect (PostgreSQL) quoting to target dialect during BuildProjection

## Open Questions
None.

## Next Work (Priority Order)
1. User confirms merge → FINALIZE (squash merge + worktree cleanup)
