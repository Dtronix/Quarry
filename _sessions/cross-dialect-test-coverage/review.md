# Review: cross-dialect-test-coverage

## Classifications

Class: **A** valid, address now · **B** gap, address now · **C** separate issue · **D** not valid

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---|---|---|---|---|---|
| 1 | Plan Compliance | Phase 3 unscoped production fix (s_deferredDescriptors) needs PR-body callout | Info | B | B | Will be called out in PR body during REMEDIATE step 6 |
| 2 | Plan Compliance | EntityReaderIntegrationTests.cs has no in-source pointer to #277 | Low | B | B | Added /// <remarks> linking #277 at top of file |
| 3 | Correctness | QRA503 misses ToAsyncEnumerable terminal — Ss + Offset still slips through | Medium | A | A | Added `InterceptorKind.ToAsyncEnumerable` to `IsExecutionSite`; added regression test `QRA503_SqlServerOffsetWithoutOrderBy_AtToAsyncEnumerableTerminal_Reports` |
| 4 | Correctness | Prepare_BatchInsert_4Dialect docstrings 4-dialect execution but only ToDiagnostics | Medium | A | A | Added 4× `await xx.ExecuteNonQueryAsync()` with `Is.EqualTo(2)` row-count assertions |
| 5 | Correctness | Connection-lifecycle tests use un-isolated connections without an in-test note | Low | B | B | Added "NOTE: bypasses harness's transaction-rollback isolation; keep limited to SELECT 1" comments to both tests |
| 6 | Test Quality | Sensitive-redaction rationale lives only in class docstring | Low | B | B | Added per-test "// SQLite-only — provider-independent path, see class docstring" comments |
| 7 | Test Quality | RawSql @pN convention not pinned by an end-to-end assertion | Low | B | B | Added `RawSql_ParameterName_IsAlwaysAtPN_AcrossDialects` to CrossDialectLoggingTests (NonParallelizable fixture) asserting all 4 dialects log `@p0` |
| 8 | Codebase Consistency | SuboptimalForDialectRule emits 2 ids but its `Descriptor` advertises only QRA502 | Low | C | A | Split into two rules: SuboptimalForDialectRule (QRA502, perf) + UnsupportedForDialectRule (QRA503, capability). Each advertises one id. Updated 8 unit tests to instantiate the new rule for QRA503 cases |
| 9 | Integration | QRA503 is a new compile-time break; needs release-notes callout | Medium | B | B | Will be called out in PR body during REMEDIATE step 6 |
| 10 | Integration | QRY070/071/072 silent-drop fix is technically a compile-time break | Low | B | B | Will be called out in PR body during REMEDIATE step 6 |

Final: **3A / 7B / 0C / 0D**

## Plan Compliance

| Finding | Severity | Why It Matters |
|---|---|---|
| Phase 3 expanded scope: in addition to adding QRY070/QRY071 generator tests (`src/Quarry.Tests/GeneratorTests.cs:1421-1509`), the commit also adds 3 entries to `s_deferredDescriptors` in `src/Quarry.Generator/QuarryGenerator.cs:777-779` to fix a pre-existing silent-diagnostic-drop bug. The plan's Phase 3 only described adding tests; the production bug fix is unscoped. | Info | Reviewers/PR description need to flag this as a behavior fix (not just a test addition) so consumers know diagnostics that previously vanished now surface as errors. The commit message acknowledges it; the PR body should too. |
| Phase 10 was deferred to issue #277, leaving `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs` as the only remaining SQLite-only Integration file. The deferral is well-documented in `workflow.md` and `handoff.md`, but the EntityReader file itself carries no in-source pointer to #277 — a future contributor opening the file won't see why this one wasn't migrated. | Low | A short docstring at the top of `EntityReaderIntegrationTests.cs` referencing #277 would prevent re-opening the architectural can of worms each time someone notices the asymmetry. |

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| `SuboptimalForDialectRule.IsExecutionSite` (`src/Quarry.Analyzers/Rules/Dialect/SuboptimalForDialectRule.cs:92-98`) enumerates `ExecuteFetchAll/First/FirstOrDefault/Single/SingleOrDefault/Scalar/NonQuery` but omits `InterceptorKind.ToAsyncEnumerable` (defined at `src/Quarry.Generator/Models/InterceptorKind.cs:106`, which the doc-comment confirms "assembles complete SQL and wires streaming reader"). A chain like `Ss.Users().Offset(10).ToAsyncEnumerable()` therefore bypasses QRA503 and produces SQL Server's parse-time-rejected `OFFSET … FETCH` without ORDER BY. | Medium | The whole point of QRA503 is "if it compiles for the dialect, it executes" — this hole silently re-introduces the runtime-failure case the rule was promoted to prevent, on a real consumer-facing terminal. |
| `Prepare_BatchInsert_4Dialect` (`src/Quarry.Tests/SqlOutput/PrepareTests.cs:663-689`) lives in a region docstring'd "verify that a Prepared chain actually executes correctly on all four dialects" but only calls `ToDiagnostics()`/`AssertDialects` and never invokes `ExecuteNonQueryAsync()` on any of `lt`/`pg`/`my`/`ss`. The phase summary in `workflow.md` claims this test covers BatchInsert "SQL+verification" but the verification half is missing — the test will pass even if every dialect's batch INSERT fails at runtime. | Medium | Defeats the stated purpose of Phase 9. Either rename the test to make the SQL-only intent explicit or add the `await xx.ExecuteNonQueryAsync()` calls (with row-count assertions) that the other 5 tests in the region include. |
| `ClosedConnection_LogsOpened` and `ClosedConnection_Dispose_LogsClosed` (`src/Quarry.Tests/SqlOutput/CrossDialectLoggingTests.cs:453-491`, `533-575`) build fresh PG/My/Ss connections from `*TestContainer.GetConnectionStringAsync()`, which point at the default DB/schema (PG `public`, MySQL the per-container default DB, no `quarry_test` `search_path`). Both tests only run `SELECT 1`, so they're safe today, but they bypass the harness's transaction-rollback isolation entirely — any future expansion that touches tables would commit outside isolation. | Low | Documenting the "fresh, un-isolated connection" intent inside each test body would prevent a later contributor from adding an `INSERT`/`DELETE` that leaks state across the shared containers. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| Sensitive-redaction tests at `src/Quarry.Tests/SqlOutput/CrossDialectLoggingTests.cs:687-745` stay SQLite-only with the rationale "redaction code path runs in QuarryContext before the SQL is built." The rationale is correct (see `src/Quarry/Logging/ParameterLog.cs:15-17` — `BoundSensitive` is logged regardless of provider), but the rationale lives only in the class-level docstring, not on each test. A future refactor that moves redaction into a per-provider parameter binder (e.g., to support provider-specific value redaction) would silently regress on Pg/My/Ss without any test catching it. | Low | A one-line `// Provider-independent path — see class docstring` comment per Sensitive test would make the assumption checkable in future PRs. |
| Phase 11's class-level docstring (`src/Quarry.Tests/SqlOutput/CrossDialectRawSqlTests.cs:10-18`) explains that `@pN` works on every dialect because the runtime always assigns `@pN`, but the cited reference is the docstring of `QuarryContext.RawSqlAsync` rather than a code line. If that runtime convention ever changes (e.g., switching to native positional binding on Npgsql 11+), the tests will silently start exercising whatever the new wire format is — without any failing test surfacing the change. | Low | Add a single test that asserts the `_logger` records the literal `@p0` parameter name end-to-end on Pg/My (the dialects most likely to diverge), pinning the convention. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| `SuboptimalForDialectRule.RuleId` is still `"QRA502"` and the doc-comment now correctly notes the rule emits two ids (`src/Quarry.Analyzers/Rules/Dialect/SuboptimalForDialectRule.cs:27-31`). However, `Descriptor` (the `IQueryAnalysisRule` contract) only points at QRA502 — there's no second hook for QRA503. `QuarryQueryAnalyzer.SupportedDiagnostics` (`src/Quarry.Analyzers/QuarryQueryAnalyzer.cs:73`) does include QRA503, so consumers see it, but the rule-level abstraction now lies (the rule is named "Suboptimal" + advertises one descriptor + emits an Error of a different id). | Low | Either split into two `IQueryAnalysisRule` instances (one per descriptor) or extend the rule contract to advertise multiple descriptors. The current layout works but invites the next dialect Error rule to be tucked into yet another existing rule, eroding the 1-rule-1-id pattern of QRA101–QRA501. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| QRA503 is a new Error-severity diagnostic. Consumer chains that compiled before with a Warning — `My.Users().FullOuterJoin<...>(...)` and `Ss.Users()...Offset(N).ExecuteFetchAllAsync()` without `OrderBy` — now fail compilation outright. The branch internally fixed two such lines (workflow notes pruning a MySQL clause from `CrossDialectJoinTests.FullOuterJoin_OnClause` + a JoinNullable test), but the same pattern in any downstream consumer breaks. | Medium | This is a meaningful break for users on stable. The PR description / release notes need to call out QRA503 explicitly with the migration path (UNION-of-LEFT-and-RIGHT for MySQL FULL OUTER, add an OrderBy for SqlServer OFFSET). |
| The `s_deferredDescriptors` registry fix (`src/Quarry.Generator/QuarryGenerator.cs:777-779`) means QRY070/QRY071/QRY072 now actually surface to the IDE/build as Errors instead of being silently dropped by `GetDescriptorById` returning null at line 524. Code that was previously relying on the silent drop — using `IntersectAll`/`ExceptAll` on a non-PostgreSQL dialect and getting a generated runtime path — now fails at compile time. | Low | Behavior is now correct (the silent drop was a bug), but it is technically a compile-time break for consumers on those dialects. Worth one line in the release notes. |
