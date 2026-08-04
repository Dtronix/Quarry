# Workflow: 331-sql-parser-cte

## Config
platform: github
base-branch: master

## State
phase: IMPLEMENT
status: suspended
issue: #331
pr:

## Problem Statement

**Issue #331** — Shared SQL parser rejects all `WITH` statements, degrading `RawSqlAsync` column resolution and blocking QRY042 + QRM CTE conversion.

`src/Quarry.Shared/Sql/Parser/SqlParser.cs:193-199` unconditionally bails on the `With` token:
returns a null AST with `HasUnsupported = true` and diagnostic "CTEs (WITH ... AS) are not yet supported".
The tokenizer already produces a `With` token correctly (`SqlTokenizer.cs:237`); only the parser bails.
There is no `RECURSIVE` keyword anywhere in the repo.

Three downstream consumers degrade:
1. `RawSqlAsync<T>` compile-time column resolution falls back to the runtime-ordinal `file struct IRowReader<T>` path, and can surface QRY041 on valid SQL.
2. QRY042 convertibility detection never fires for CTE queries.
3. `Quarry.Migration` cross-ORM converters fall to the `Sql.Raw` fallback even though the runtime has `With<TDto>` / `FromCte<TDto>`.

Suggested scope from the issue:
- Parse `WITH [RECURSIVE] name [(col, …)] AS ( <select> ) [, …] <select>` into the AST
- Expose CTE names to the walker so column references against a CTE resolve
- Retain `_hasUnsupported` only for constructs genuinely outside the AST's expressive range
- Extend `src/Quarry.Tests/SqlParserTests.cs` and `SqlParserReviewTests.cs`

### Baseline test results

`dotnet test Quarry.sln -c Release` — **all green, no pre-existing failures.**

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| Quarry.Tests | 3501 | 0 | 0 |
| Quarry.Migration.Tests | 201 | 0 | 0 |
| Quarry.Analyzers.Tests | 146 | 0 | 0 |
| **Total** | **3848** | **0** | **0** |

Build emits pre-existing warnings (CS0219 `__colShift` unused in generated interceptors, NUnit2009 in `IR/PipelineModelEqualityTests.cs:331`) — not introduced by this work.

## Decisions

- **2026-08-04 — D1: CTEs attach to `SqlSelectStatement`, not a wrapper node.** `SqlSelectStatement` gains optional trailing ctor params `Ctes` + `IsRecursive`. It is constructed in exactly one place (`SqlParser.cs:303`) and `SqlNodeKind` is only ever used for diagnostic text (`SqlToChainConverter.cs:713,781`), so this is source-compatible. `result.SelectStatement` keeps returning the outer SELECT, so no consumer breaks on the type shape.
- **2026-08-04 — D2: A fully-parsed CTE query sets `HasUnsupported = false`.** This is the mechanism that restores hardcoded ordinals in `RawSqlColumnResolver` — it bails on `HasUnsupported` at `RawSqlColumnResolver.cs:84-85`. Applies to recursive CTEs too, once their bodies parse (D4), since the outer SELECT's columns are the result columns regardless.
- **2026-08-04 — D3: Converter work is full CTE→chain conversion, not just a guard.** (User decision.) `ChainEmitter` and `SqlToChainConverter` both emit `.With<…>()` / `.FromCte<…>()`. The conservative "CTE ⇒ not convertible" guard still lands first, in the same commit that enables CTE parsing, so no commit in the branch can silently drop a WITH clause.
- **2026-08-04 — D4: Full recursive support — set operations get real AST nodes.** (User decision.) Recursive CTE bodies are UNION-joined, so `SqlSetOperationStatement` is required. Recursive CTEs remain **not convertible** to the chain API: there is no recursive `With` in the runtime. Recursive support is therefore parser-side only, and its payoff is RawSqlAsync ordinal resolution plus clean diagnostics.
- **2026-08-04 — D5: Top-level set operations keep today's behavior.** (User decision.) Set operations parse only inside CTE bodies; a top-level UNION/INTERSECT/EXCEPT still sets `HasUnsupported` + a diagnostic. Keeps the three pinning tests (`Parse_Union_MarkedAsUnsupported`, `Parse_Intersect_MarkedAsUnsupported`, `Parse_Except_MarkedAsUnsupported`) unchanged and confines blast radius to this issue. Top-level set-op support is a separate issue.
- **2026-08-04 — D6: CTEs on DML statements stay unsupported.** (User decision.) `WITH … UPDATE/DELETE/INSERT` keeps today's behavior: diagnostic, null AST. Only `SqlSelectStatement` carries `Ctes`.
- **2026-08-04 — D7: Synthesized CTE DTOs are inserted by the code fixes.** (User decision.) `ConversionResult` gains a generated-declarations field; `DapperMigrationCodeFix`, `AdoNetMigrationCodeFix` and `RawSqlToChainCodeFix` insert the class into the compilation unit alongside the expression replacement. They already inject `using` directives via `document.WithSyntaxRoot`, so the mechanism exists. Requires name-collision handling against existing types.

## Working Notes

- 2026-08-04 — Parser architecture recon:
  - `SqlParseResult` = `{ Statement, Diagnostics, HasUnsupported }`; `Success => Statement != null && Diagnostics.Count == 0`.
  - `SqlNode` carries `SourceStart`/`SourceLength` but the parser currently never populates them (default -1).
  - AST has no CTE node; `SqlSelectStatement` has no `With` slot. `SqlNodeKind` enum would need a new member.
  - `SqlNodeWalker.Walk` is a `switch (node)` over concrete types; any new node type must be added there or its children are silently skipped.
  - Parser files are `#if QUARRY_GENERATOR`-gated, dual-namespace (`Quarry.Generators.Sql.Parser` vs `Quarry.Shared.Sql.Parser`).
  - Existing "unsupported" precedent: subqueries in FROM / IN / expression context set `_hasUnsupported` and emit a `SqlUnsupported` node holding raw text — they do NOT null the whole statement. CTEs are the outlier that kills the whole parse.
  - Tests: NUnit, `[TestFixture]` + `[Test]`, `Assert.That(...)` constraint style. `SqlParserTests.Parse_CTE_MarkedAsUnsupported` (line 503) and `SqlParserReviewTests.Parse_CteError_HasActionableMessage` (line 106) both pin the current reject behavior and will need updating.

- 2026-08-04 — **Quarry's CTE name is the C# type name, not free text.** `LambdaCteTests.cs:157-188`: `With<Order, OrderSummaryDto>(…)` emits `WITH "OrderSummaryDto" AS (…) … FROM "OrderSummaryDto"`. So converting `WITH recent_orders AS (…)` means synthesizing a type named `RecentOrders`; the emitted CTE name changes with it, which is safe because the outer FROM changes in lockstep. Two chain forms exist: whole-entity `With<Order>(o => o.Where(…))` (no new type needed) and projected `With<Order, Dto>(o => o.Where(…).Select(o => new Dto { … }))` (needs the synthesized type).
- 2026-08-04 — **`CteDtoResolver.cs:9` — "No schema attribute is required — the DTO is a plain C# class."** Properties need both a getter and a setter (`:28`). This is what makes DTO synthesis viable at all; emit `{ get; set; }` classes rather than positional records to stay clearly inside that contract.
- 2026-08-04 — **Joining against a CTE is a known runtime gap.** `CteWithEntityAccessorTests.cs:63-66` documents it in a comment: `Join<Cte.Order>(…)` resolves to the underlying table `"orders"`, not the CTE name `"Order"`. Converting a query whose outer part JOINs a CTE would therefore emit chain code producing *different SQL*. Convertibility rules must reject CTE-as-join-target.
- 2026-08-04 — **The parser change alone opens a silent-wrong-code hole.** Today the null AST is what protects the converters: neither `ChainEmitter.Translate` (`ChainEmitter.cs:28-35`, checks only `Statement == null`) nor `RawSqlMigrationAnalyzer` (`:112-113`, checks only `Success` + `SelectStatement != null`) inspects `HasUnsupported`. The moment `WITH … SELECT … FROM users` parses, both would emit chain code with the WITH clause dropped. Hence the guard ships in the same commit as parser enablement.
- 2026-08-04 — **Only Dapper and ADO.NET use the SQL parser.** EF Core and SqlKata converters translate Roslyn syntax directly and never call `SqlParser.Parse` — they are out of scope despite the issue naming all four.
- 2026-08-04 — **Two compiled copies of the AST exist** (`Quarry.Generators.Sql.Parser` for generator/analyzers under `QUARRY_GENERATOR`, `Quarry.Shared.Sql.Parser` for Quarry.Migration under `QUARRY_MIGRATION`). Instances never cross. Any AST change lands in both automatically, but the two *consumer* implementations (`SqlToChainConverter` vs `ChainEmitter`) are independent and must each be updated.
- 2026-08-04 — **`SqlNodeWalker` is used by no production code**, only tests (`SqlParserReviewTests.cs:207-241`). All four consumers hand-roll their own recursive switch. Adding a node type to the walker is necessary but not sufficient — each consumer's switch needs its own case.

## Suspend State

**Suspended 2026-08-04 — IMPLEMENT, after step 3 of 11.** Triggered by the workflow context check (≥3 plan steps completed this session), not by a problem.

- **Position:** plan.md steps 1–3 complete and committed. Next is step 4.
- **In progress:** nothing. Working tree clean, no WIP commit.
- **Immediate next step:** step 4 — correct the stale fallback message at `RawSqlColumnResolver.cs:85` (it still claims CTEs are unsupported) and add `RawSqlColumnResolverTests` proving a CTE query now resolves hardcoded ordinals. No logic change is needed there; D2 already routes CTE queries past the `HasUnsupported` gate.
- **Commits on branch:** `e7fbd82` session artifacts → `c9e1f14` step 1 → `f04c65f` step 2 → `793271e` step 3.
- **Test status:** all passing — 3873 total (Quarry.Tests 3522, Quarry.Migration.Tests 203, Quarry.Analyzers.Tests 148). Baseline was 3848, so 25 tests added so far.
- **Not yet pushed to origin** at time of suspend; push is part of the suspend procedure.
- **Unrecorded context:** none. All design conclusions are in Decisions D1–D7 and Working Notes; the remaining step details are in plan.md.
- **Watch on resume:** steps 6–9 are the substantial half (CTE convertibility rules, `.With<>`/`.FromCte<>` emission, DTO synthesis) and both converters must be done independently — `SqlToChainConverter` and `ChainEmitter` share no code. The step-3 blanket rejections in `SqlToChainConverter.CheckConvertibility` and `ChainEmitter.TranslateSelect` are placeholders to be replaced, not kept.

## Session Log

| Date | Phases | Summary |
|------|--------|---------|
| 2026-08-04 | INTAKE | Loaded issue #331, created worktree `331-sql-parser-cte`, baseline test run green (3848/3848), parser + consumer recon. |
| 2026-08-04 | IMPLEMENT | Steps 1–3 complete: RECURSIVE tokenizing, CTE + set-operation AST nodes, WITH parsing with converter guards. 3873 tests green. Suspended on the context check. |
| 2026-08-04 | DESIGN, PLAN | Mapped all four parser consumers. Confirmed the silent-CTE-drop hole the parser change opens, the CTE-name/type-name coupling, and the Join-against-CTE runtime gap. Decisions D1–D7 recorded; wrote plan.md (11 steps). |
