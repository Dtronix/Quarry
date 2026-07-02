# Workflow: 303-fix-mysql-positional-binding

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #303
pr: #304
session: 2
phases-total: 4
phases-complete: 4

## Problem Statement
MySQL positional `?` binding misalignment in the DistinctOrderBy wrap path (and audit of other surfaces) — GitHub issue #303.

For MySQL (MySqlConnector), the Nth `?` in SQL text is bound to the Nth `cmd.Parameters.Add()` call. `ChainAnalyzer` assigns `GlobalIndex` in chain-call order and `CarrierEmitter` binds in that order, but `SqlAssembler.RenderSelectSqlWithDistinctOrderByWrap` hoists the OrderBy expression textually before the WHERE clause, so SQL-text order diverges from bind order. SQLite/SqlServer bind by `ParameterName` and PostgreSQL by explicit `$N`, so only MySQL is affected. Confirmed reproducer: `.Where(o => o.Total > threshold).OrderBy(o => o.Total + bias).Distinct().Select(o => o.Total)` returns 0 rows on MySQL (bias bound to the WHERE `?`).

Issue text contains full root-cause analysis, file:line references, candidate fix directions (reorder binds into SQL-text order for MySQL vs. switch MySQL to named `@pN` binding), and an audit list of other suspect surfaces (window functions, JOIN ON, conditional masks, set operations, CTE rebasing).

Although the issue's literal deliverable was a written plan, the user directed this workflow to implement the fix end-to-end (see Decisions).

Pre-existing baseline failures: none — 3234/3235 passed; the single failure is the new reproducer (expected pre-fix, 0 rows instead of 2).
Note: the new reproducer test `MySqlIntegrationTests.DistinctOrderByWrap_ParameterizedWhereAndOrderBy_OnMySQL_PreservesBindingAlignment` (brought in uncommitted from master's working tree along with its quarry-manifest.mysql.md delta) is EXPECTED to fail pre-fix — it is the bug's regression guard, not a pre-existing failure.

## Decisions
- 2026-06-10: Deliverable is the full fix implementation (design → plan → implement → PR), superseding the issue's literal "plan document only" ask. Prior plan-only branch `303-mysql-positional-binding-plan` (plan doc + ignored reproducer) was deleted at user direction.
- 2026-06-10: Of master's dirty working-tree files, only the 303-related ones were brought into the worktree: `src/Quarry.Tests/Integration/MySqlIntegrationTests.cs` (reproducer test) and `src/Quarry.Tests/ManifestOutput/quarry-manifest.mysql.md` (its chain manifest delta). `CarrierEmitter.cs` __colShift change and `llm.md` doc edits stay in master's working tree as the user's separate work.

- 2026-06-10: Fix direction = (a) keep bare `?` placeholders for MySQL (max performance; avoids driver-side named-parameter text parsing/matching) and make the generator track SQL-text binding order so `cmd.Parameters.Add()` order matches placeholder order. Option (b) named `@pN` was considered and rejected by user despite MySqlConnector support.
- 2026-06-10: Audit scope = focused set (2-3 MySQL integration tests on riskiest divergence surfaces), not the full 5-surface matrix.
- 2026-06-10: Bind-order tracking = marker-scan at generation time. MySQL placeholder emission renders indexed marker tokens (e.g. `{__Q{globalIdx}__}`); a post-pass at assembly's existing MySQL post-process point (where TokenizeCollectionParameters runs) scans each variant left-to-right to extract the SQL-text-order ranking, replaces markers with `?`, and validates marker set == expected param set per variant. CarrierEmitter emits bind blocks in ranked order (identity ranking → byte-identical generated output). Rejected alternatives: int[] sink during render (render-call order ≠ text order in the wrap path — pre-rendered fragments spliced out of order, speculative detection renders pollute); fragment-scoped (string,int[]) pairs (correct but imposes a compose-both obligation on every current/future splice site).
- 2026-06-10: Single per-chain ranking, not per-mask: every mask variant's text order is the full ranking filtered to active params (wrap always renders hoisted OrderBy → body → pagination regardless of mask). Bind loop keeps existing mask-gated if-blocks, just iterates in ranked order. Generator-time assert validates ranking consistency across variants.
- 2026-06-10: `ReplaceNthOccurrence(sql, '?', ...)` MySQL special case in TokenizeCollectionParameters is replaced by direct marker matching (also fixes latent miscount if a SQL string literal contains `?`).

- 2026-06-10: Marker rewrite is range-based single-pass (user refinement): scanner records marker ranges and builds output once via StringBuilder — no string.Replace. Collection tokenization for MySQL folds into the same pass (TokenizeCollectionParameters MySQL branch deleted, not rewritten). Rewrite runs for ALL MySQL plans (not just carrier-eligible) since manifests read variant SQL; collection-token emission keeps the carrier-eligibility gate.

- 2026-06-10 (REMEDIATE): Validation/merge failure now emits warning QRY048 (new descriptor) with a reason at the chain's terminal, plus identity fallback — replaces the silent fallback + Debug.Assert (review #1/#6). Post-process (RewriteMySqlBindMarkers + TokenizeCollectionParameters) moved into PipelineOrchestrator.AnalyzeAndGroupTranslated so both output actions see final SQL and incremental equality is consistent (review #8/#17). Pagination markers use PaginationPlan.LimitParamIndex/OffsetParamIndex true slots — fixes window-param + parameterized-Limit chains on MySQL (review #7/#13); the equivalent pre-existing numbering issue on non-MySQL dialects was initially slated for a spin-off issue, but the user directed (2026-06-10) that it be fixed in this PR: AppendPagination now uses true global slots on ALL dialects. Dead ParameterName guard removed — MySQL names are the constant "?" in every path (review #9/#16). Plan deviations recorded in plan.md (review #2/#3). TryMergeTextOrder made internal + unit-tested (review #4/#12).

- 2026-07-02 (REVIEW pass 2): User-requested full re-review (verify #303 truly fixed + hunt fix-introduced issues) → review-2.md; pass-1 review.md preserved. 17 findings classified accept-all: 5A/4B/8D. Two confirmed Highs: (a) QRY048 descriptor never added to s_deferredDescriptors — every emission silently dropped, pass-1 #1/#6 remediation inert; (b) TryMergeTextOrder insert-after-anchor placement falsely reports contradiction for ≥2 independent conditional parameterized clauses (masks ascending: [0] then [1] → [1,0]; [0,1] contradicts) → identity fallback, so wrap + that shape still misbinds silently — #303 not fully fixed. Fixes must land together (#16) or conditional chains emit false warnings. User directive: also add integration tests for all testable pass-2 gaps. Full suite re-verified green this session (3269+146+201, 0 skips) before pass 2.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | REMEDIATE | Pruned stale worktree + deleted prior plan branch; created worktree; copied 303 reproducer; baseline 3234/3235 (reproducer only failure). DESIGN: bare-? + marker-scan decided. PLAN: 4 phases approved. IMPLEMENT: phase 1 (marker emission + extraction, inert) committed 9e00328; phase 2 (CarrierEmitter consumes MySqlBindOrder) committed c1f4d83 — reproducer green, 3256/3256. Phase 3: 3 audit integration tests (window-fn params, conditional×wrap, collection×wrap) all green first run; manifest +3 chains, committed 2638907. Phase 4: docs (generator llm.md subsection, wrap comment updated). |
| 2 | REMEDIATE | | Resumed at REMEDIATE step 8: verified PR #304 open/mergeable, CI green on head 0e64940, origin/master unmoved (no rebase needed); recorded pr number + session bump. Back-step to REVIEW at user request for review pass 2 (verify #303 truly fixed + hunt fix-introduced issues); pass-1 review.md and all decisions preserved; pass 2 written to review-2.md. |
