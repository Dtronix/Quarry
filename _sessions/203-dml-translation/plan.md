# Plan: 203-dml-translation

## Overview

Extend the Quarry.Migration SQL-to-chain translator to handle DELETE, UPDATE, and INSERT statements. Currently, only SELECT is supported — DML calls using Dapper's `ExecuteAsync` are reported as QRM003. The parser, AST, emitter, and tests all need extension.

## Key Concepts

**SqlStatement base class**: A new abstract `SqlStatement : SqlNode` sits between `SqlNode` and statement types. `SqlSelectStatement`, `SqlDeleteStatement`, `SqlUpdateStatement`, and `SqlInsertStatement` all inherit from it. `SqlParseResult.Statement` changes from `SqlSelectStatement?` to `SqlStatement?`. This is the only breaking change — all existing code that accesses `parseResult.Statement` needs to cast or pattern-match.

**Token extension**: The tokenizer recognizes keywords by length-bucketed string comparison. We add `Delete` (6), `Update` (6), `Insert` (6), `Set` (3), `Values` (6), and `Into` (4) to `SqlTokenKind` and the `ClassifyKeyword` method.

**Parser dispatch**: `ParseRoot()` currently enforces SELECT. It becomes a dispatcher: check the first keyword token and call `ParseSelectStatement()`, `ParseDeleteStatement()`, `ParseUpdateStatement()`, or `ParseInsertStatement()` accordingly.

**DELETE emission**: `DELETE FROM users WHERE condition` → `db.Users().Delete().Where(u => condition).ExecuteNonQueryAsync()`. Reuses the existing expression emitter for WHERE.

**UPDATE emission**: `UPDATE users SET col1 = val1, col2 = val2 WHERE condition` → `db.Users().Update().Set(u => { u.Col1 = val1; u.Col2 = val2; }).Where(u => condition).ExecuteNonQueryAsync()`. Multiple assignments grouped into a single `.Set()` call with block-body lambda.

**INSERT emission**: `INSERT INTO users (col1, col2) VALUES (@v1, @v2)` → emitted as a comment: `// TODO: Construct entity and use: db.Users().Insert(entity).ExecuteNonQueryAsync()` since Quarry INSERT takes entity objects, not column/value pairs. A QRM002-level diagnostic is reported.

**No-WHERE handling**: DELETE/UPDATE without WHERE emit `.All()` (required by Quarry API) and add a diagnostic warning.

## Phases

### Phase 1: Token and AST infrastructure
**Files:** `SqlToken.cs`, `SqlNode.cs`

Add new token kinds: `Delete`, `Update`, `Insert`, `Set`, `Values`, `Into` to `SqlTokenKind` enum.

Add new `SqlNodeKind` variants: `DeleteStatement`, `UpdateStatement`, `InsertStatement`, `Assignment`.

Create abstract `SqlStatement : SqlNode` base class. Make `SqlSelectStatement` inherit from it instead of directly from `SqlNode`.

Create `SqlDeleteStatement : SqlStatement` with `SqlTableSource Table` and `SqlExpr? Where`.

Create `SqlUpdateStatement : SqlStatement` with `SqlTableSource Table`, `IReadOnlyList<SqlAssignment> Assignments`, and `SqlExpr? Where`.

Create `SqlAssignment : SqlNode` with `SqlColumnRef Column` and `SqlExpr Value`.

Create `SqlInsertStatement : SqlStatement` with `SqlTableSource Table`, `IReadOnlyList<SqlColumnRef>? Columns`, and `IReadOnlyList<IReadOnlyList<SqlExpr>> ValueRows`.

Update `SqlParseResult`: change `Statement` property from `SqlSelectStatement?` to `SqlStatement?`.

**Tests:** None for this phase — infrastructure only. Compilation is the gate.

### Phase 2: Tokenizer keyword registration
**Files:** `SqlTokenizer.cs`

Add keyword entries to `ClassifyKeyword()`:
- Length 3: `SET` → `SqlTokenKind.Set`
- Length 4: `INTO` → `SqlTokenKind.Into`
- Length 6: `DELETE` → `SqlTokenKind.Delete`, `INSERT` → `SqlTokenKind.Insert`, `UPDATE` → `SqlTokenKind.Update`, `VALUES` → `SqlTokenKind.Values`

Also add `Set`, `Into`, `Delete`, `Update`, `Insert`, `Values` to `IsKeywordUsableAsIdentifier()` in `SqlParser.cs` since these words may appear as column/table names.

**Tests:** Add tokenizer tests verifying the new keywords are classified correctly.

### Phase 3: Parser — DELETE statement
**Files:** `SqlParser.cs`

Modify `ParseRoot()`: instead of enforcing SELECT, dispatch based on the current token kind:
- `SqlTokenKind.Select` → `ParseSelectStatement()` (existing)
- `SqlTokenKind.Delete` → `ParseDeleteStatement()` (new)
- `SqlTokenKind.Update` → `ParseUpdateStatement()` (new, Phase 4)
- `SqlTokenKind.Insert` → `ParseInsertStatement()` (new, Phase 5)
- Otherwise → diagnostic "Expected SQL statement"

Implement `ParseDeleteStatement()`:
1. Consume `DELETE`
2. Expect `FROM`
3. Call `ParseTableSource()` (existing) for the target table
4. If `WHERE` follows, call `ParseExpression()` (existing) for the condition
5. Return new `SqlDeleteStatement(table, where)`

**Tests:** Parser tests for DELETE: simple delete, delete with WHERE, delete with complex WHERE, delete with parameters.

### Phase 4: Parser — UPDATE statement
**Files:** `SqlParser.cs`

Implement `ParseUpdateStatement()`:
1. Consume `UPDATE`
2. Call `ParseTableSource()` for the target table
3. Expect `SET`
4. Parse assignment list: loop parsing `column = expression` separated by commas
   - Each assignment: read column ref (possibly qualified), expect `=`, parse expression
5. If `WHERE` follows, parse the condition
6. Return new `SqlUpdateStatement(table, assignments, where)`

**Tests:** Parser tests for UPDATE: simple single-column update, multi-column update, update with WHERE, update with parameters.

### Phase 5: Parser — INSERT statement
**Files:** `SqlParser.cs`

Implement `ParseInsertStatement()`:
1. Consume `INSERT`
2. Expect `INTO`
3. Call `ParseTableSource()` for the target table
4. If `(` follows, parse column name list
5. Expect `VALUES`
6. Parse one or more `(expr, expr, ...)` value rows
7. Return new `SqlInsertStatement(table, columns, valueRows)`

**Tests:** Parser tests for INSERT: insert with columns and values, insert with parameters, insert without column list, multi-row insert.

### Phase 6: ChainEmitter — compile fix and DELETE emission
**Files:** `ChainEmitter.cs`

The `Translate()` method currently accesses `parseResult.Statement` as `SqlSelectStatement`. Update it to pattern-match on the statement type:

```csharp
switch (parseResult.Statement)
{
    case SqlSelectStatement select:
        return TranslateSelect(select, callSite);
    case SqlDeleteStatement delete:
        return TranslateDelete(delete, callSite);
    case SqlUpdateStatement update:
        return TranslateUpdate(update, callSite);
    case SqlInsertStatement insert:
        return TranslateInsert(insert, callSite);
    default:
        // error diagnostic
}
```

Extract the existing SELECT translation logic into `TranslateSelect()`.

Implement `TranslateDelete()`:
1. Resolve table from schema (reuse existing pattern)
2. Emit `db.{Accessor}().Delete()`
3. If WHERE present: emit `.Where(u => {condition})` reusing `EmitExpression()`
4. If no WHERE: emit `.All()` and add warning diagnostic
5. Append terminal `.ExecuteNonQueryAsync()`

**Tests:** ChainEmitter tests for DELETE: delete with WHERE, delete without WHERE (verify .All() + warning), delete with parameters.

### Phase 7: ChainEmitter — UPDATE and INSERT emission
**Files:** `ChainEmitter.cs`

Implement `TranslateUpdate()`:
1. Resolve table from schema
2. Emit `db.{Accessor}().Update()`
3. Emit `.Set(u => { u.Col1 = val1; u.Col2 = val2; })` — for each assignment, resolve column via `ResolveColumnAccess` and value via `EmitExpression`
4. If WHERE: emit `.Where(u => {condition})`; if no WHERE: emit `.All()` + warning
5. Append terminal `.ExecuteNonQueryAsync()`

Implement `TranslateInsert()`:
1. Resolve table from schema
2. Emit as comment: `// TODO: Construct {ClassName} entity and use:\n// db.{Accessor}().Insert(entity).ExecuteNonQueryAsync()`
3. If columns are specified, include them in the comment for reference
4. Return null chain code with QRM002-level diagnostic

**Tests:** ChainEmitter tests for UPDATE: single assignment, multi-assignment with block Set, with/without WHERE, with parameters. ChainEmitter tests for INSERT: verify comment output and diagnostic.

### Phase 8: Integration and analyzer tests
**Files:** `DapperMigrationAnalyzer` tests (if they exist), or manual verification

Run the full test suite to verify:
- All existing SELECT tests pass unchanged
- New DML tests pass
- The analyzer correctly routes `ExecuteAsync("DELETE ...")` through the full pipeline
- QRM001/QRM002/QRM003 diagnostics are reported correctly for DML

Clean up any edge cases found during integration.

## Dependencies
- Phase 1 must come first (everything depends on AST types)
- Phase 2 must precede Phases 3-5 (parser needs tokens)
- Phases 3, 4, 5 are independent of each other (but sequential for simplicity)
- Phase 6 depends on Phases 1-3 (needs DELETE AST to emit)
- Phase 7 depends on Phases 4-5 (needs UPDATE/INSERT AST)
- Phase 8 depends on all prior phases
