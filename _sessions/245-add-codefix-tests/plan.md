# Plan: 245-add-codefix-tests

## Key Concepts

**Code Fix Testing Pattern**: Each code fix test uses a helper method that (1) builds a `CSharpCompilation` from user code + framework stubs + Quarry schema stub, (2) runs the migration analyzer to produce real diagnostics, (3) creates an `AdhocWorkspace` with a `Document` containing the same source and metadata references, (4) calls `RegisterCodeFixesAsync` with each diagnostic, (5) applies the resulting `CodeAction` to transform the document, and (6) returns the modified source text for assertion.

**Framework Stubs**: Each test file includes its own framework stub (EF Core, ADO.NET, SqlKata, or Dapper) and QuarryStub, following the established pattern in existing detector/converter/analyzer tests. The stubs provide just enough type surface for the Roslyn semantic model to resolve types, methods, and extension methods.

**Diagnostic ID Tiers**: Each migration family has three diagnostic IDs: `0x1` (clean conversion — fixable), `0x2` (conversion with warnings — fixable), `0x3` (not convertible — NOT fixable). The code fix providers only register for `0x1` and `0x2`. Tests must verify that no code action is registered when the analyzer reports `0x3`.

**IsSuggestionOnly Guard** (Dapper, ADO.NET): When the converter produces a comment-only suggestion (e.g., INSERT), `IsSuggestionOnly` is true, and the code fix returns the document unchanged. The analyzer routes these to `0x3`, so the code fix never sees them — but the guard exists as defense-in-depth. The Dapper INSERT test validates this contract end-to-end.

## Phases

### Phase 1: EfCoreMigrationCodeFixTests.cs

Create `src/Quarry.Migration.Tests/EfCoreMigrationCodeFixTests.cs` with these tests:

1. **FixableDiagnosticIds_ContainsExpectedIds** — Verify `FixableDiagnosticIds` contains QRM011 and QRM012.
2. **HasFixAllProvider** — Verify `GetFixAllProvider()` returns non-null.
3. **SimpleToListAsync_ReplacesWithChainApi** — `await db.Users.ToListAsync()` → replaced with Quarry chain code containing `.Users()` and `.ExecuteFetchAllAsync()`.
4. **AwaitPreserved_WhenOriginalIsAwaited** — The `await` keyword is preserved in the transformed code.
5. **UsingDirectivesAdded** — `using Quarry;` and `using Quarry.Query;` are added to the compilation unit.
6. **NonFixableDiagnostic_QRM013_NoCodeAction** — When the analyzer reports QRM013 (no schema match), no code action is registered (QRM013 is not in FixableDiagnosticIds).

The helper method `ApplyCodeFixAsync(string userCode)` builds the compilation with EfCoreStub + QuarryStub, runs `EfCoreMigrationAnalyzer`, finds the first fixable diagnostic (QRM011 or QRM012), creates an AdhocWorkspace Document, invokes `EfCoreMigrationCodeFix.RegisterCodeFixesAsync`, applies the first code action, and returns the resulting source text.

### Phase 2: AdoNetMigrationCodeFixTests.cs

Create `src/Quarry.Migration.Tests/AdoNetMigrationCodeFixTests.cs` with these tests:

1. **FixableDiagnosticIds_ContainsExpectedIds** — QRM021, QRM022.
2. **HasFixAllProvider**.
3. **SimpleExecuteReader_ReplacesWithChainApi** — `cmd.ExecuteReader()` with `SELECT * FROM users` → replaced with Quarry chain code.
4. **TodoCommentInserted** — The transformed code contains the TODO comment `// TODO: Remove DbCommand setup above — now using Quarry chain API`.
5. **UsingDirectivesAdded** — `using Quarry;` and `using Quarry.Query;` are added.
6. **NonFixableDiagnostic_QRM023_NoCodeAction** — INSERT reports QRM023 (not in FixableDiagnosticIds), no code action registered.

The helper method follows the same pattern as Phase 1 but uses AdoNetStub, `AdoNetMigrationAnalyzer`, and `AdoNetMigrationCodeFix`.

### Phase 3: SqlKataMigrationCodeFixTests.cs

Create `src/Quarry.Migration.Tests/SqlKataMigrationCodeFixTests.cs` with these tests:

1. **FixableDiagnosticIds_ContainsExpectedIds** — QRM031, QRM032.
2. **HasFixAllProvider**.
3. **SimpleWhereQuery_ReplacesWithChainApi** — `new Query("users").Where("user_id", ">", 5)` → replaced with Quarry chain code.
4. **UsingDirectivesAdded** — `using Quarry;` and `using Quarry.Query;` are added.
5. **NonFixableDiagnostic_QRM033_NoCodeAction** — No schema match reports QRM033, no code action registered.

Note: SqlKata code fix uses `ObjectCreationExpressionSyntax` rather than `InvocationExpressionSyntax` — the helper must handle this difference. The SqlKata code fix also uses `detector.Detect()` (full tree scan) + `FirstOrDefault(s => s.ChainExpression.Span.Contains(...))` rather than `TryDetectSingle`.

### Phase 4: DapperMigrationCodeFixTests.cs

Create `src/Quarry.Migration.Tests/DapperMigrationCodeFixTests.cs` with these tests:

1. **FixableDiagnosticIds_ContainsExpectedIds** — QRM001, QRM002.
2. **HasFixAllProvider**.
3. **SimpleQueryAsync_ReplacesWithChainApi** — `connection.QueryAsync<User>("SELECT * FROM users")` → replaced with Quarry chain code.
4. **AwaitPreserved_WhenOriginalIsAwaited** — `await` preserved in transformed code.
5. **UsingDirectivesAdded** — `using Quarry;` and `using Quarry.Query;` are added.
6. **NonFixableDiagnostic_QRM003_NoCodeAction** — INSERT (QRM003) is not in FixableDiagnosticIds, no code action.

## Dependencies

All four phases are independent and share no code (each has its own stubs and helper, matching existing test patterns). They can be committed in any order. The test project already has all required NuGet packages.
