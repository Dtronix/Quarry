# Plan: 331-sql-parser-cte

Implementation plan for issue #331 — teach the shared SQL parser to parse `WITH`, and let the three downstream consumers act on the result.

## Key concepts

**The parse-enablement hole.** The single most important sequencing constraint is that enabling CTE parsing is not a safe change on its own. Today `SqlParser.cs:193-199` returns a null AST for any `WITH` query, and that null is what stops the two chain converters from acting. Neither `ChainEmitter.Translate` nor `RawSqlMigrationAnalyzer` inspects `HasUnsupported` — they check `Statement == null` and `Success` respectively. The instant a `WITH … SELECT … FROM users` query parses into a plain outer SELECT, both converters would happily emit chain code with the WITH clause silently dropped, producing C# that does not match the SQL. Step 3 therefore ships the parser enablement *and* a conservative "any CTE ⇒ not convertible" rejection in both converters as one commit. Steps 6–9 then replace that rejection with real conversion. Every commit on the branch is correct in isolation.

**Why set operations are in scope.** A recursive CTE body is UNION-joined by definition (`SELECT anchor UNION ALL SELECT recursive-step`). Parsing `WITH RECURSIVE` therefore forces a real AST representation for set operations. Per D5 that representation is used *only* inside CTE bodies; a top-level `UNION` keeps its current diagnostic and `HasUnsupported` flag, so the three existing pinning tests stay untouched and the blast radius stays on this issue.

**Why recursive CTEs parse but never convert.** The Quarry runtime has no recursive `With`. A recursive CTE can be represented in the AST (which is what unblocks RawSqlAsync ordinal resolution and removes the hard parse error) but can never be expressed as a chain. Convertibility rules must reject `IsRecursive` outright.

**The CTE-name/type-name coupling.** Quarry derives the emitted CTE name from the C# type name: `With<Order, OrderSummaryDto>(…)` emits `WITH "OrderSummaryDto" AS (…) … FROM "OrderSummaryDto"`. Converting arbitrary SQL therefore means synthesizing a type whose name is the PascalCased CTE name. This is safe because the CTE name is query-internal — the outer `FROM` reference changes in lockstep. Two emission forms exist:

```csharp
// whole-entity CTE — no synthesized type needed
db.With<Order>(o => o.Where(o => o.Total > 100)).FromCte<Order>()…

// projected CTE — needs a synthesized DTO
db.With<Order, RecentOrders>(o => o.Where(o => o.Total > 100)
      .Select(o => new RecentOrders { UserId = o.UserId, Total = o.Total }))
  .FromCte<RecentOrders>()…
```

**Convertibility rules (applied by both converters).** A CTE query converts only when all hold; otherwise it is reported not-convertible and falls back to existing behavior:
- not recursive;
- every CTE body is a single-table SELECT over a mapped entity with no joins and no reference to another CTE, and satisfies the converter's existing expression rules;
- the outer query's `FROM` is either a CTE name or a mapped entity;
- **no CTE appears as a JOIN target** — `CteWithEntityAccessorTests.cs:63-66` documents that `Join<TCte>` resolves to the underlying table, not the CTE, so converting such a query would emit different SQL.

## Steps

- [x] **1. Tokenizer: `RECURSIVE` keyword.** Add `SqlTokenKind.Recursive` to `SqlToken.cs` and a `case 9:` arm in `SqlTokenizer.ClassifyKeyword` (next to the existing `INTERSECT` arm). No parser change — purely additive, `RECURSIVE` currently tokenizes as `Identifier`.
  *Tests:* `SqlTokenizerTests.cs` — assert `RECURSIVE` tokenizes to `SqlTokenKind.Recursive`, mixed-case included.

- [x] **2. AST nodes and walker (no parser change).** In `SqlNode.cs`: add `SqlNodeKind.CommonTableExpression` and `SqlNodeKind.SetOperation`; add `SqlSetOperator` enum (`Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll`); add `SqlSetOperationStatement : SqlStatement` with `Left`/`Operator`/`Right` (both sides `SqlStatement` so chains nest left-associatively); add `SqlCommonTableExpression : SqlNode` with `Name`, `ColumnNames` (`IReadOnlyList<string>?`), `Query` (`SqlStatement`). Add optional trailing ctor params `ctes` and `isRecursive` to `SqlSelectStatement` (per D1 — one construction site, so this is source-compatible). In `SqlNodeWalker.Walk`: descend into `SqlSelectStatement.Ctes`, `SqlCommonTableExpression.Query`, and `SqlSetOperationStatement.Left`/`Right`. Nothing constructs these yet, so behavior is unchanged.
  *Tests:* none behavioral yet — full suite must stay green, proving source compatibility across both compiled copies of the AST.

- [x] **3. Parser: parse `WITH`, plus conservative converter rejection.** Depends on 1, 2. This is the pivotal commit.
  - Replace `SqlParser.cs:193-199` with `ParseWithClause()`: consume `WITH`, optional `RECURSIVE`, then a comma-separated list of `ParseCte()` — each `name [(col, …)] AS ( <body> )`.
  - `ParseCteBody()` parses a SELECT then, while the next token is `UNION [ALL]` / `INTERSECT [ALL]` / `EXCEPT [ALL]`, folds into `SqlSetOperationStatement`. Used **only** here (D5).
  - After the WITH clause, require `SELECT`; attach `Ctes` + `IsRecursive` to the resulting statement. If a DML keyword follows, keep today's behavior per D6 — diagnostic, `_hasUnsupported = true`, null statement.
  - Leave the top-level set-operation check at `SqlParser.cs:226-231` exactly as is (D5).
  - A fully-parsed CTE query leaves `_hasUnsupported` false (D2).
  - **Same commit:** add `stmt.Ctes != null` early rejection to `SqlToChainConverter.CheckConvertibility` (`SqlToChainConverter.cs:38`) and to `ChainEmitter.TranslateSelect` (`ChainEmitter.cs:56`, an Error diagnostic + null chain) so no WITH clause can be silently dropped.
  - Update the two tests that pin the reject behavior: `SqlParserTests.cs:502-508` (`Parse_CTE_MarkedAsUnsupported`) and `SqlParserReviewTests.cs:105-112` (`Parse_CteError_HasActionableMessage`).
  *Tests:* new `SqlParserTests` section covering single CTE, multiple CTEs, explicit column list, `WITH RECURSIVE` with a `UNION ALL` body, nested/chained set ops in a body, CTE referenced by the outer FROM, `WITH … UPDATE` still rejected, and malformed input (missing `AS`, missing close paren) recovering with diagnostics rather than looping. Walker coverage in `SqlParserReviewTests` — `FindAll<SqlColumnRef>` must reach inside a CTE body. Converter tests asserting a CTE query is reported not-convertible.

- [x] **4. RawSqlAsync ordinal resolution.** Depends on 3. `RawSqlColumnResolver.cs:84-85` needs no logic change — D2 makes CTE queries flow past the `HasUnsupported` gate on their own — but its fallback string still claims CTEs are unsupported and must be corrected.
  *Tests:* `RawSqlColumnResolverTests.cs` — a `WITH … SELECT a, b FROM cte` query resolves ordinals (this is issue #331's headline win); a recursive CTE resolves too; `WITH … SELECT *` still falls back on the `SELECT *` rule.

- [x] **5. QRY041/QRY042 analyzer hardening.** Depends on 3. Add the missing `HasUnsupported` check to `RawSqlMigrationAnalyzer.cs:112-113` — it currently lets window functions and subqueries through to the converter, relying on `SqlToChainConverter`'s `case SqlUnsupported` as an incidental net. Pre-existing gap, but this change widens what reaches that path, so it is in scope here rather than deferred.
  *Tests:* `RawSqlMigrationAnalyzerTests.cs` — QRY042 does not fire for a window-function query.

- [x] **6. `SqlToChainConverter`: CTE convertibility.** *(merged into one commit with step 7 — see deviation note below)* Depends on 3. Replace the step-3 blanket rejection with the convertibility rules above: register CTE names in a set distinct from `_tableToEntity`, validate each body, reject recursive and CTE-as-join-target, resolve an outer `FROM <cte>` against the CTE list.
  *Tests:* `RawSqlMigrationAnalyzerTests.cs` — convertible whole-entity CTE fires QRY042; recursive CTE does not; CTE-as-join-target does not; CTE body with a join does not.

- [x] **7. `SqlToChainConverter`: CTE emission + DTO synthesis.** Depends on 6. Emit `.With<TEntity>(…)` for whole-entity bodies and `.With<TEntity, TDto>(… .Select(e => new TDto { … }))` for projected bodies, followed by `.FromCte<T>()` when the outer FROM is a CTE. Synthesize the DTO class text (`{ get; set; }` properties per `CteDtoResolver.cs:28`) and carry it on a new `DtoDeclaration` diagnostic property. Extend `RawSqlToChainCodeFix` to insert it into the compilation unit, with collision handling against existing type names.
  *Tests:* analyzer tests asserting `ChainCode` and `DtoDeclaration` property contents; a code-fix test asserting the declaration is inserted and the result compiles.

- [ ] **8. `ConversionResult` + `ChainEmitter`: CTE convertibility.** Depends on 3. Add `GeneratedTypeDeclarations` (`IReadOnlyList<string>`) to `ConversionResult.cs`. Replace the step-3 blanket rejection in `ChainEmitter` with the same convertibility rules, registering CTE names alongside `_tables` without letting them collide with real schema tables.
  *Tests:* `ChainEmitterTests.cs` — CTE queries that must not convert produce a null chain plus a clear diagnostic.

- [ ] **9. `ChainEmitter`: CTE emission + DTO synthesis.** Depends on 8. Emit both `.With<>` forms and `.FromCte<>`, populate `GeneratedTypeDeclarations`.
  *Tests:* `ChainEmitterTests.cs` — whole-entity CTE, projected CTE (asserting the synthesized declaration), multi-CTE chain; `DapperConverterTests.cs` / `AdoNetConverterTests.cs` end-to-end.

- [ ] **10. Code fixes insert synthesized types.** Depends on 9. Extend `DapperMigrationCodeFix.ConvertToQuarryAsync` (`:70-96`) and the ADO.NET equivalent to add `GeneratedTypeDeclarations` to the compilation unit next to the expression replacement, reusing the existing `EnsureUsing` pattern. Skip declarations whose name already exists in the compilation.
  *Tests:* `DapperMigrationCodeFixTests` / `AdoNetMigrationCodeFixTests` — applying the fix yields a document containing both the chain and the declaration.

- [ ] **11. Documentation.** Depends on all. Update the "Shared SQL Parser" section of `src/Quarry.Generator/llm.md:311-313` (it currently lists CTEs as an unsupported construct), `src/Quarry.Migration/README.md:31`, and any diagnostic-table wording that claims CTEs are unparseable.
  *Tests:* none; if manifest goldens shift, rebuild rather than hand-edit them.

## Deviations

- **Steps 6 and 7 were committed together**, and steps 8 and 9 will be too. The split was not shippable: a commit that makes CTE queries pass `CheckConvertibility` while `Convert` still ignores the WITH clause re-opens exactly the silent-drop regression step 3 exists to prevent. Convertibility and emission have to land atomically in each converter.
- **Step 10's QRY042 half moved into step 7.** `RawSqlToChainCodeFix` had to learn to insert the synthesized DTO in the same commit that started emitting `DtoDeclarations`, for the same reason. Step 10 now covers only the Dapper and ADO.NET code fixes.
- **Convertibility is narrower than the plan's rules described.** Two additional restrictions emerged from reading the runtime:
  - The outer query must read *from a CTE*, not from a real entity. Starting the chain with an entity accessor after `.With<>()` requires the context to derive from `QuarryContext<TSelf>` (`QuarryContext.cs:140-151`), which the analyzer cannot verify — emitting it would risk a runtime `NotSupportedException`. `.FromCte<T>()` is declared on the base `QuarryContext`, so the CTE-first shape is always safe.
  - CTE bodies are restricted to `WHERE` plus a column projection. `DISTINCT`, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT` and `OFFSET` in a body are rejected rather than silently dropped, since `With<>`'s inner builder is a Where + Select chain.
  - Foreign-key columns are rejected in projections: they surface as wrapper types (`o.UserId.Id` in `CteWithEntityAccessorTests.cs:70`) that cannot be reliably reproduced in a synthesized DTO.

## Dependencies

```
1 ─┐
2 ─┴─► 3 ─┬─► 4
          ├─► 5
          ├─► 6 ─► 7 ──┐
          └─► 8 ─► 9 ─► 10 ─► 11
```

Steps 4, 5, and the 6→7 and 8→9→10 chains are independent of each other once 3 lands.
