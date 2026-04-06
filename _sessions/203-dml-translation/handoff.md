# Work Handoff: 203-dml-translation

## Key Components
- **`src/Quarry.Shared/Sql/Parser/SqlNode.cs`** — AST nodes. New: `SqlStatement` base, `SqlDeleteStatement`, `SqlUpdateStatement`, `SqlInsertStatement`, `SqlAssignment`. `SqlParseResult.Statement` is now `SqlStatement?`, with `SelectStatement` convenience accessor.
- **`src/Quarry.Shared/Sql/Parser/SqlToken.cs`** — `SqlTokenKind` additions: `Delete`, `Update`, `Insert`, `Set`, `Values`, `Into`.
- **`src/Quarry.Shared/Sql/Parser/SqlTokenizer.cs`** — `ClassifyKeyword()` entries for the six new keywords.
- **`src/Quarry.Shared/Sql/Parser/SqlParser.cs`** — `ParseRoot()` dispatches by statement type; new `ParseDeleteStatement()`, `ParseUpdateStatement()`, `ParseInsertStatement()`, `ParseColumnRef()` helpers. DML keywords added to `IsKeywordUsableAsIdentifier()`.
- **`src/Quarry.Migration/ChainEmitter.cs`** — `Translate()` dispatches via pattern matching on statement type. New: `TranslateSelect()` (extracted), `TranslateDelete()`, `TranslateUpdate()`, `TranslateInsert()`, `RegisterPrimaryTable()` helper.
- **`src/Quarry.Analyzers/RawSqlMigrationAnalyzer.cs`** — updated to use `parseResult.SelectStatement` (still SELECT-only).
- **`src/Quarry.Generator/CodeGen/RawSqlColumnResolver.cs`** — updated to use `SelectStatement` with null check for non-SELECT fallback.

## Completions (This Session)
1. **Phase 1** — AST infrastructure: base `SqlStatement`, DML statement nodes, `SqlAssignment`, new `SqlNodeKind`/`SqlTokenKind` entries. Updated `SqlParseResult.Statement` type and all internal consumers (ChainEmitter, RawSqlMigrationAnalyzer, RawSqlColumnResolver, 3 test files).
2. **Phase 2** — Tokenizer: six new keywords registered in `ClassifyKeyword()`. DML keywords added to parser's `IsKeywordUsableAsIdentifier()`. Tokenizer tests added.
3. **Phase 3** — DELETE parser: `ParseRoot()` dispatch by statement type, `ParseDeleteStatement()`.
4. **Phase 4** — UPDATE parser: `ParseUpdateStatement()` with SET assignment list, `ParseColumnRef()` helper.
5. **Phase 5** — INSERT parser: `ParseInsertStatement()` with optional column list and VALUES clause (supports multi-row).
6. **Phase 6** — ChainEmitter refactor: `Translate()` pattern-matches on statement type, `TranslateSelect()` extracted. `TranslateDelete()` emits `.Delete().Where().ExecuteNonQueryAsync()` or `.Delete().All()` + warning.
7. **Phase 7** — `TranslateUpdate()` emits single-block `.Set(u => { ... })` with multiple assignments. `TranslateInsert()` emits TODO comment with entity construction hint.
8. **Phase 8** — Integration: all 2987 tests pass (30 new).
9. **REVIEW** — Review agent ran, `review.md` produced with 4 findings (1 medium, 3 low). Classification in progress when suspended.

## Previous Session Completions
(None — this is session 1.)

## Progress
- 8 of 8 implementation phases complete
- REVIEW phase: analysis complete, classification pending user confirmation
- REMEDIATE, FINALIZE not yet started
- Branch has 4 commits: AST infrastructure, tokenizer keywords, parser, emitter

## Current State
**In REVIEW phase, mid-classification.** The review agent produced `review.md` with 4 findings. I proposed classifications via AskUserQuestion but the user interrupted with "handoff" before answering.

**Proposed classifications (awaiting user confirmation):**
1. UPDATE with computed expression value test (e.g. `SET count = count + 1`) — **(B) Gap, address now**
2. DELETE with table alias test — **(B) Gap, address now**
3. No DML analyzer integration — the `DapperMigrationAnalyzer` doesn't yet route `ExecuteAsync` with DML SQL through `ChainEmitter` — **(C) Separate issue** (requires analyzer work beyond this branch's scope)
4. Duplicated table-lookup in `TranslateInsert` vs. `RegisterPrimaryTable` — **(D) Not valid** (intentional; INSERT only emits a comment)

**Note on finding 3:** This needs verification. The `DapperMigrationAnalyzer` (the one that produces QRM001/002/003) currently calls `ChainEmitter.Translate()` which now handles DML. The review agent's claim that "no analyzer handles ExecuteAsync" may be inaccurate — the analyzer routes *all* Dapper calls including ExecuteAsync through the pipeline. What may be missing is a specific end-to-end test for DML in `DapperMigrationAnalyzerTests.cs`. Resume should verify this by re-reading `src/Quarry.Migration/DapperMigrationAnalyzer.cs`.

## Known Issues / Bugs
None discovered during testing. All 2987 tests passing (89 migration + 103 analyzer + 2795 main).

## Dependencies / Blockers
None.

## Architecture Decisions
- **Base SqlStatement class** (not separate properties on `SqlParseResult`) — chose this for cleanest type hierarchy. `SelectStatement` convenience property eases migration of existing code.
- **INSERT as comment with TODO** — Quarry's `Insert(entity)` takes an entity object, not column/value pairs; there's no clean SQL→chain mapping. Emitted comment preserves user intent without producing incorrect code.
- **Single `.Set()` block for UPDATE** — matches hand-written idiom; one lambda captures all assignments: `.Set(u => { u.Col1 = val1; u.Col2 = val2; })`.
- **`.All()` + warning for no-WHERE DML** — Quarry API requires explicit `.All()` to confirm destructive full-table operations. Diagnostic warning alerts user to verify intent.
- **DML keywords usable as identifiers** — Added DELETE/UPDATE/INSERT/SET/VALUES/INTO to `IsKeywordUsableAsIdentifier()` so they can appear as column/table names in other contexts.

## Open Questions
- Finding 3 (DML analyzer integration) needs verification. Is there already an end-to-end path for `conn.ExecuteAsync("DELETE ...")` through `DapperMigrationAnalyzer`? The answer affects whether this is a (C) separate issue or a (D) non-issue.

## Next Work (Priority Order)
1. **Verify Finding 3**: Read `src/Quarry.Migration/DapperMigrationAnalyzer.cs` to confirm whether `ExecuteAsync` calls are routed through `ChainEmitter.Translate()` for DML SQL. If yes, downgrade to (D). If no, confirm as (C).
2. **Resume classification**: present the (possibly updated) classifications via AskUserQuestion.
3. **REMEDIATE phase — implement (B) gaps**:
   - Add test for UPDATE with computed expression: `SET login_count = login_count + 1` — verify parser produces `SqlBinaryExpr` in assignment value, emitter emits `u.LoginCount = u.LoginCount + 1` correctly
   - Add test for DELETE with table alias: `DELETE FROM users u WHERE u.user_id = @id` — verify alias resolution in emission
4. **REMEDIATE phase — create (C) issue** (if confirmed): file issue describing the DML analyzer integration gap
5. **REMEDIATE phase — commit fixes, rebase on origin/master, create PR**
6. **FINALIZE phase — merge once CI is green**
