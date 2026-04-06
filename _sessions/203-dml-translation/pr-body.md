## Summary
- Closes #203
- Extend the Quarry.Migration SQL-to-chain translator to handle DELETE, UPDATE, and INSERT statements (previously SELECT-only).

## Reason for Change
`DapperMigrationAnalyzer` previously reported `ExecuteAsync` calls with DELETE/UPDATE/INSERT SQL as `QRM003 (unconvertible)` because `ChainEmitter.Translate()` only handled `SELECT`. Quarry has full DML support via the chain API (`Delete()`, `Update().Set()`, `Insert()`), so these calls should be translatable.

## Impact
- AST: `SqlSelectStatement`, `SqlDeleteStatement`, `SqlUpdateStatement`, and `SqlInsertStatement` now share an abstract `SqlStatement` base. `SqlParseResult.Statement` is `SqlStatement?` (with a `SelectStatement` convenience accessor for legacy callers).
- Parser: `ParseRoot()` dispatches by leading keyword. New `ParseDeleteStatement()`, `ParseUpdateStatement()`, `ParseInsertStatement()`, and `ParseColumnRef()` helpers.
- Tokenizer: six new keywords registered (`DELETE`, `UPDATE`, `INSERT`, `SET`, `VALUES`, `INTO`). All six are also marked usable-as-identifier so they can still appear as table/column names.
- Emitter: `Translate()` pattern-matches by statement type. New `TranslateDelete()`, `TranslateUpdate()`, `TranslateInsert()` plus a shared `RegisterPrimaryTable()` helper extracted from the SELECT path.
- DML now flows end-to-end through `DapperMigrationAnalyzer` because `DapperDetector` already includes `ExecuteAsync` and the analyzer routes all detected calls through `ChainEmitter`.

### Emission shapes
- `DELETE FROM users WHERE user_id = @id` → `db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync()`
- `UPDATE users SET email = @email, user_name = @name WHERE user_id = @id` → `db.Users().Update().Set(u => { u.Email = email; u.UserName = name; }).Where(u => u.UserId == id).ExecuteNonQueryAsync()`
- `INSERT INTO users (user_name, email) VALUES (@name, @email)` → emitted as a TODO comment with the entity-construction hint plus a diagnostic warning, since Quarry's `Insert(entity)` takes an entity object rather than column/value pairs.
- `DELETE` / `UPDATE` without `WHERE` → emit `.All()` plus a diagnostic warning to confirm the destructive full-table operation was intentional.

## Plan items implemented as specified
- Phase 1 — AST infrastructure: base `SqlStatement` class with DELETE/UPDATE/INSERT subclasses, `SqlAssignment`, updated `SqlParseResult.Statement` type and all internal consumers.
- Phase 2 — Tokenizer: six new keywords, also added to `IsKeywordUsableAsIdentifier()`.
- Phase 3 — DELETE parser: `ParseDeleteStatement()` with optional `WHERE`.
- Phase 4 — UPDATE parser: `ParseUpdateStatement()` with SET assignment list and `ParseColumnRef()` helper.
- Phase 5 — INSERT parser: `ParseInsertStatement()` with optional column list and multi-row VALUES clause.
- Phase 6 — ChainEmitter dispatch + `TranslateDelete()` (shape, no-WHERE warning, schema lookup).
- Phase 7 — `TranslateUpdate()` with single-block `.Set(u => { ... })` lambda; `TranslateInsert()` emits the TODO comment and diagnostic.
- Phase 8 — Integration & tests: 30 new unit tests, all suites green.

## Deviations from plan implemented
None. All eight phases were implemented as written.

## Gaps in original plan implemented
The plan's Phase 8 mentioned "verify the analyzer pipeline routes ExecuteAsync DML through the emitter" but did not specify a test. REVIEW surfaced this as a gap. The wiring already existed (`DapperDetector` covers `ExecuteAsync`/`Execute`; `DapperMigrationAnalyzer` routes all detected sites through `ChainEmitter`), but no end-to-end test exercised it. Three new tests in `DapperMigrationAnalyzerTests` now cover DELETE/UPDATE/INSERT through the full analyzer pipeline.

REVIEW also surfaced two missing edge case tests and a small duplication, all addressed:
- `Update_WithComputedExpression` — verifies `SET salary = salary + 1000` parses and emits as `u.Salary = u.Salary + 1000`.
- `Delete_WithTableAlias` — verifies `DELETE FROM users u WHERE u.user_id = @id` resolves the alias correctly.
- `TranslateInsert` now calls `RegisterPrimaryTable()` instead of duplicating the schema lookup and warning message.

## Migration Steps
None. Internal AST changes only — `SqlParseResult.Statement` and the new statement types are all `internal`.

## Performance Considerations
None. Translator runs at compile time inside the analyzer; no hot-path or runtime impact.

## Security Considerations
None. The translator is a source-to-source rewriter operating on parsed SQL ASTs at compile time. No runtime SQL execution, no user input handling. Parameter references are preserved by name without evaluation.

## Breaking Changes
- **Consumer-facing:** None.
- **Internal:** `SqlParseResult.Statement` changed from `SqlSelectStatement?` to `SqlStatement?`. All in-tree callers updated. `RawSqlMigrationAnalyzer` and `RawSqlColumnResolver` use the `SelectStatement` convenience accessor (with a null fallback for non-SELECT inputs in the resolver).

## Test Coverage
- 35 new tests total: 19 parser DML tests, 12 tokenizer keyword tests, 4 ChainEmitter DML tests, 3 analyzer end-to-end DML tests, plus the 2 added in REMEDIATE for the alias and computed-expression cases.
- Final suite: **2992 passing** (94 migration + 103 analyzer + 2795 main), 0 failures, 0 skipped.
