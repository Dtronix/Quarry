# Work Handoff

## Key Components

| Area | Files |
|------|-------|
| Runtime types | `src/Quarry/Schema/One.cs`, `OneBuilder.cs`, `Schema.cs` (HasOne, HasManyThrough methods) |
| T4 templates | `src/Quarry/Query/IJoinedQueryBuilder.tt` → `.g.cs`, `src/Quarry.Generator/CodeGen/JoinArityHelpers.tt` → `.g.cs` |
| Models | `SingleNavigationInfo.cs`, `ThroughNavigationInfo.cs`, `ImplicitJoinInfo.cs` (all in `Models/`) |
| Entity pipeline | `EntityInfo.cs`, `EntityRef.cs` (carry `SingleNavigations`/`ThroughNavigations` through pipeline) |
| SqlExpr IR | `SqlExprNodes.cs` (`NavigationAccessExpr`), `SqlExpr.cs` (`NavigationAccess` enum value) |
| Parser | `SqlExprParser.cs` (emits `NavigationAccessExpr` for chained member access) |
| Binder | `SqlExprBinder.cs` (`BindNavigationAccess` method, `HasManyThrough` expansion in `BindSubquery`) |
| Assembly | `QueryPlan.cs` (`ImplicitJoins` field), `SqlAssembler.cs` (renders implicit joins), `ChainAnalyzer.cs` (collects implicit joins) |
| Plumbing | `CallSiteTranslator.cs`, `TranslatedCallSite.cs`, `SqlExprClauseTranslator.cs`, `SqlExprRenderer.cs` |
| Code gen | `CarrierEmitter.cs`, `InterceptorCodeGenerator.cs` (use `JoinArityHelpers`) |
| Diagnostics | `DiagnosticDescriptors.cs` (QRY040-045) |
| Tests | `CrossDialectNavigationJoinTests.cs`, `OrderSchema.cs`, `OrderItemSchema.cs` (added `One<T>` navs) |

## Completions (This Session)

- **Feature C: Explicit joins to 6 tables** — T4 templates generate `IJoinedQueryBuilder` interfaces (arity 2-6, 10 interfaces). `JoinArityHelpers` centralizes arity logic. Hand-written `IJoinedQueryBuilder.cs` deleted. `CarrierEmitter` and `InterceptorCodeGenerator` updated.
- **Feature A: `One<T>` navigation joins** — Full pipeline from runtime types through to SQL rendering. Works in Where, OrderBy, GroupBy, Having. Deep chains (2+ hops) work. Deduplication across clauses works. INNER/LEFT inference from FK nullability works.
- **Feature B: `HasManyThrough`** — Runtime types and schema parsing complete. Binder expansion in `BindSubquery` implemented (detects `ThroughNavigationInfo`, expands to junction subquery + implicit join to target). Not yet tested with cross-dialect tests.
- **Diagnostics QRY040-045** — Descriptors added. Not yet wired into parser/binder error paths (descriptors exist but no `context.ReportDiagnostic()` calls).
- **PR created**: Dtronix/Quarry#158 on branch `feature/navigation-joins`. All 2458 tests pass (2454 existing + 4 new).

## Previous Session Completions

None — first session on this issue.

## Progress

Issue #156 has 3 features × 16 phases. Core implementation is done for all 3 features. Remaining work is hardening: projection support, diagnostic wiring, additional tests, and integration tests for 5-6 table joins and HasManyThrough.

## Current State

PR Dtronix/Quarry#158 is open. The branch builds clean and all tests pass. The implementation covers the "happy path" for navigation joins but has gaps in projection analysis and diagnostic reporting.

**Navigation in Select projections does not work yet.** When a user writes `.Select(o => (o.OrderId, o.User.UserName))`, the `ProjectionAnalyzer` cannot resolve tuple element types through navigation chains. The generator produces unresolved tuple types like `(OrderId, UserName)` instead of `(int, string)`, causing CS0246 build errors in generated interceptors.

- **Attempted**: Writing tests with navigation in Select (e.g., `.Select(o => (o.OrderId, o.User.UserName)).Prepare()`).
- **Why it failed**: `ProjectionAnalyzer` (in `Projection/ProjectionAnalyzer.cs`) analyzes `Select()` lambda bodies to determine column types. It resolves `MemberAccessExpressionSyntax` on the entity type but doesn't handle the chained access pattern `o.User.UserName` where `User` is a navigation property. The semantic model resolves `o.User` to `User?` (the generated entity type), but the subsequent `.UserName` resolves correctly — the issue is in how `ProjectionAnalyzer` classifies the expression, not in type resolution per se.
- **What's left**: Extend `ProjectionAnalyzer` to detect navigation chain patterns and resolve the final property's type. The column SQL is already correct (the binder produces `"j0"."UserName"`), so only the result type name needs fixing.

**Workaround**: Navigation joins work correctly in Where, OrderBy, GroupBy, Having clauses. For Select, users can select only primary entity columns (e.g., `.Select(o => o.Total)`) and the navigation-joined WHERE/ORDER still works.

## Known Issues / Bugs

1. **Select columns not table-qualified with implicit joins** — When implicit joins are present, SELECT columns from the primary entity render as `SELECT "Total"` instead of `SELECT "t0"."Total"`. Valid SQL (column is unambiguous) but inconsistent with explicit join behavior. Root cause: the Select clause is translated in a separate call site from Where, and the binder doesn't know about implicit joins from other clauses when binding the Select. Severity: cosmetic for non-ambiguous columns, would break for same-named columns across joined tables.

2. **SqlExprParser now emits NavigationAccessExpr for ALL chained member access** — The parser changed the fallthrough behavior: previously `o.Column.Length` (member access on a non-Ref property) produced `SqlRawExpr`; now it produces `NavigationAccessExpr`. The binder falls back gracefully (returns `SqlRawExpr("/* unresolved navigation... */")`) so existing queries aren't broken, but the error message is misleading. Could be improved by checking if the property is actually a `One<T>` before emitting `NavigationAccessExpr`, but the parser has no semantic model access.

3. **QRY040-045 diagnostics not wired** — Descriptors exist in `DiagnosticDescriptors.cs` but `SchemaParser.TryParseSingleNavigation` silently returns `false` instead of reporting QRY040/041/042. `SqlExprBinder.BindNavigationAccess` returns `SqlRawExpr` comments instead of QRY043.

## Dependencies / Blockers

- **dotnet-t4 global tool** — Must be installed (`dotnet tool install --global dotnet-t4`) to regenerate T4 templates. Not required for normal builds since generated `.g.cs` files are checked in. MSBuild targets for auto-regeneration are NOT added to `.csproj` files.

## Architecture Decisions

1. **Implicit joins separate from explicit joins (`QueryPlan.ImplicitJoins`)** — Originally tried adding implicit joins to `QueryPlan.Joins` alongside explicit joins. This caused the chain to be classified as a multi-entity join chain (`IsJoinChain = true`), which triggered `JoinedEntityTypeNames!.Select(...)` null dereferences in `CarrierEmitter` and `JoinBodyEmitter` because implicit joins don't set `JoinedEntityTypeNames`. Separating into `ImplicitJoins` keeps the chain as a single-entity query with additive JOINs in the SQL.

2. **`NavigationAccessExpr` uses flat hop list, not nested nodes** — The issue plan suggested both approaches. Flat was chosen: `NavigationHops = ["User", "Department"]` + `FinalPropertyName = "Name"`. Simpler to bind (iterate left-to-right) and extend (just append to list). Nested would require recursive unwrapping.

3. **Binder always uses "t0" as primary table alias for navigation access** — Navigation access always produces implicit joins, and the assembler always aliases the primary table as "t0" when joins exist. So the binder unconditionally uses "t0" for the primary entity in `BindNavigationAccess`. This was a fix for a bug where the binder used the table name ("orders") but the assembler aliased it as "t0", causing mismatched ON clauses.

4. **`EntityRef` extended with `SingleNavigations`/`ThroughNavigations`** — `EntityRef` is a lightweight bridge between `EntityInfo` (heavy, carries `Location`) and the binding pipeline. It previously only carried `Columns` and `Navigations`. Extended with optional parameters defaulting to `Array.Empty<>()` for backward compatibility. `FromEntityInfo` and `ReconstructEntityInfo` updated to pass them through.

5. **T4 over manual code** — User explicitly requested T4 infrastructure. The `MaxArity` constant in both templates means extending to arity 7+ requires only changing one number and re-running `t4`.

## Open Questions

1. **Should navigation access in Select projections be a compile error (QRY-level) or should the ProjectionAnalyzer be extended?** — Extending the analyzer is the right long-term answer but is non-trivial. An intermediate option: allow navigation in Select for scalar projections (`o => o.User.UserName`) but not tuple projections (`o => (o.OrderId, o.User.UserName)`), since scalar projections don't need tuple type resolution.

2. **Cross-clause implicit join deduplication** — Currently each clause independently resolves implicit joins. When the same `One<T>` navigation is used in both Where and OrderBy, both clauses produce `j0` (deduplication within each clause works), but they're independent. The `ChainAnalyzer` deduplicates across clauses by `TargetAlias`, which works because the binder reuses aliases within a single bind context. But if Where and OrderBy are in different bind contexts (they are — each clause is translated independently), the alias counter resets and both get `j0`. The current dedup in `ChainAnalyzer` catches this by alias name. Needs validation with more complex multi-clause scenarios.

3. **Should HasManyThrough be merged into this PR or split out?** — The binder expansion is implemented but untested. Could ship Feature A+C now and add Feature B tests in a follow-up.

## Next Work (Priority Order)

1. **Extend ProjectionAnalyzer for navigation access in Select** — This is the #1 gap. Without it, users can't project navigation columns in Select tuples. Start by reading `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` and understanding how it resolves `MemberAccessExpressionSyntax` to `ProjectedColumn`. The fix likely involves detecting when a member access chain goes through a `One<T>` property (using the semantic model to check the property type) and resolving the final column's type from the target entity.

2. **Wire QRY040-045 diagnostics** — In `SchemaParser.TryParseSingleNavigation`: report QRY040 when no matching Ref column found, QRY041 when multiple found, QRY042 when explicit HasOne references wrong column. In `SqlExprBinder.BindNavigationAccess`: report QRY043 when target entity not found. Requires adding `Diagnostic` reporting infrastructure to the parser/binder (they currently don't have access to `SourceProductionContext`).

3. **Add HasManyThrough cross-dialect tests** — Create test schemas with junction tables (e.g., `UserAddressSchema` with `UserId` + `AddressId` + `One<AddressSchema>`). Add `HasManyThrough` navigation on `UserSchema`. Write tests for `.Any(a => a.City == "Portland")` through the skip-navigation.

4. **Add 5-table and 6-table join integration tests** — The T4-generated interfaces compile but no tests exercise them. Add to `JoinedCarrierIntegrationTests.cs`. Needs additional test entities/tables in `QueryTestHarness`.

5. **Table-qualify Select columns when implicit joins present** — In the assembler or projection rendering, detect when `ImplicitJoins.Count > 0` and prefix primary entity columns with `"t0"`. May require the projection to know about the table alias, which currently it doesn't.

6. **Improve parser precision for NavigationAccessExpr** — Currently emits `NavigationAccessExpr` for any chained member access that isn't `Id/Value/HasValue`. Could add a heuristic: only emit if the first hop name matches a known entity type pattern (PascalCase, not a known .NET member like `Length`). Or accept the binder fallback and improve the error message.
