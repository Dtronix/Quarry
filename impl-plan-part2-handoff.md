# Work Handoff

## Key Components

| Area | Files |
|------|-------|
| Projection (navigation in Select) | `ProjectionInfo.cs` (NavigationHops), `ProjectionAnalyzer.cs` (chain detection), `ImplicitJoinHelper.cs` (new), `ChainAnalyzer.cs` (BuildProjection enrichment + table-qualification) |
| Diagnostics | `EntityInfo.cs` (Diagnostics property), `SchemaParser.cs` (QRY040-042 collection), `QuarryGenerator.cs` (diagnostic reporting) |
| HasManyThrough fixes | `SqlExprBinder.cs` (ThroughNav priority over Nav), `SchemaParser.cs` (>= 2 type args), `Schema.cs` (TSelf type param) |
| Parser precision | `SqlExprParser.cs` (KnownDotNetMembers exclusion set) |
| Test schemas | `AddressSchema.cs`, `UserAddressSchema.cs`, `WarehouseSchema.cs`, `ShipmentSchema.cs` (all new) |
| Test contexts | `TestDbContext.cs`, `PgDb.cs`, `MyDb.cs`, `SsDb.cs` (added Address, UserAddress, Warehouse, Shipment accessors) |
| Test harness | `QueryTestHarness.cs` (addresses, user_addresses, warehouses, shipments tables) |
| Tests | `CrossDialectNavigationJoinTests.cs` (4 new nav-in-Select tests), `CrossDialectHasManyThroughTests.cs` (new), `JoinedCarrierIntegrationTests.cs` (5-6 table note) |

## Completions (This Session)

- **Phase 1: Navigation access in Select projections** — Full pipeline: `NavigationHops` on `ProjectedColumn`, navigation chain detection in `ProjectionAnalyzer` (single-entity and joined paths), `ImplicitJoinHelper` shared helper, `ResolveNavigationColumn` in `BuildProjection`, null-forgiving operator (`!`) support in chain walking, semantic model type resolution at discovery time. 4 new cross-dialect tests.
- **Phase 2: Table-qualify Select columns** — Post-process projection columns after `BuildProjection`: any primary entity column with `TableAlias == null` gets `"t0"` when implicit joins exist. Aggregate columns excluded. Updated all 8 navigation join test expectations.
- **Phase 3: Wire QRY040-042 diagnostics** — Added `Diagnostics` property to `EntityInfo`. Modified `TryParseSingleNavigation` to report QRY040 (no FK), QRY041 (ambiguous FK), QRY042 (invalid HasOne column). Diagnostics collected during parsing and reported in generator.
- **Phase 4: HasManyThrough cross-dialect tests** — Created `AddressSchema`, `UserAddressSchema`. Added `HasManyThrough` on `UserSchema`. Fixed `HasManyThrough` API (added `TSelf` type param for type-safe junction lambda). Fixed binder to check `ThroughNavigations` before `Navigations` (was shadowed by regular nav). Fixed parser to accept >= 2 type args. 1 new cross-dialect test with exact SQL assertions.
- **Phase 5: 5-6 table join infrastructure** — Created `WarehouseSchema`, `ShipmentSchema`. Registered in all contexts with SQLite table creation. Documented that T4 interfaces compile but `UsageSiteDiscovery` doesn't recognize 5+ table chains yet.
- **Phase 6: Parser precision** — Added `KnownDotNetMembers` exclusion set (Length, Year, Month, Day, Ticks, etc.) to prevent false-positive `NavigationAccessExpr` for .NET property accesses on column values.

## Previous Session Completions

- **Feature C: Explicit joins to 6 tables** — T4 templates generate `IJoinedQueryBuilder` interfaces (arity 2-6).
- **Feature A: `One<T>` navigation joins** — Full pipeline for Where, OrderBy, GroupBy, Having. Deep chains. Dedup. INNER/LEFT inference.
- **Feature B: `HasManyThrough`** — Runtime types and schema parsing. Binder expansion in `BindSubquery`.
- **Diagnostics QRY040-045** — Descriptors added in `DiagnosticDescriptors.cs`.
- **PR created**: Dtronix/Quarry#158 on branch `feature/navigation-joins`.

## Progress

All 6 phases from `impl-plan-part2.md` complete. 2463 tests pass (2454 original + 9 new). Core navigation join features are fully functional: Where, OrderBy, GroupBy, Having, and now Select projections with type resolution and table qualification. HasManyThrough works end-to-end with cross-dialect tests. Parser precision improved.

## Current State

The branch is clean. All tests pass. The implementation covers all planned features from both `impl-plan-joins.md` (Phase 1-12) and `impl-plan-part2.md` (Phase 1-6).

## Known Issues / Bugs

1. **5-6 table join discovery** — `UsageSiteDiscovery` doesn't recognize 5+ table join chains. T4-generated `IJoinedQueryBuilder5/6` interfaces compile but the discovery pipeline caps at 4-table joins. Infrastructure (schemas, contexts, tables) is in place for when discovery is extended.

2. **QRY043-045 not fully wired** — QRY043 (navigation target not found) is a binder-level error that returns SQL comments instead of diagnostics (binder is static, no context access). QRY044/045 (HasManyThrough junction/target validation) descriptors exist but `TryParseThroughNavigation` doesn't report them yet — it silently returns false on errors.

3. **SqlExprParser still emits NavigationAccessExpr for some non-navigation chains** — The `KnownDotNetMembers` exclusion is conservative. Custom property accesses like `o.SomeProp.CustomField` will still produce `NavigationAccessExpr` if neither name is in the exclusion list. The binder's fallback handles this gracefully.

## Dependencies / Blockers

- **dotnet-t4 global tool** — Must be installed to regenerate T4 templates. Not required for normal builds.

## Architecture Decisions

1. **Null-forgiving operator handling in chain parsing** — `TryParseNavigationChain` and all chain-walking helpers unwrap `PostfixUnaryExpressionSyntax` (SuppressNullableWarning) at each level. This is necessary because `o.User!.UserName` has the `!` between member accesses, producing nested syntax that the original simple loop didn't handle.

2. **HasManyThrough API uses TSelf generic** — Changed `Func<Schema, object?>` to `Func<TSelf, object?>` with a third type parameter. This allows the junction lambda to reference `Many<T>` properties on the concrete schema class. The parser ignores the third type argument.

3. **Binder checks ThroughNavigations before Navigations** — A `HasManyThrough` property has both a `NavigationInfo` (needed for entity property generation and subquery structure) and a `ThroughNavigationInfo` (for junction expansion). Previously the regular nav was found first, bypassing the junction logic. Now through-nav takes priority.

4. **Table-qualification in chain analysis, not projection analysis** — Primary entity columns get `"t0"` table alias only when implicit joins exist, and this is applied as a post-processing step in the chain analysis loop (after BuildProjection). This keeps ProjectionAnalyzer stage-agnostic.

## Open Questions

1. **Should 5-6 table join discovery be prioritized?** — The interfaces compile but the discovery pipeline needs extension. This may be needed if users want 5+ explicit joins.

2. **QRY043 in the binder** — The binder is a static utility without `SourceProductionContext`. The fallback SQL comment works but isn't user-visible. Could detect during chain analysis instead.

## Next Work (Priority Order)

1. **Extend UsageSiteDiscovery for 5-6 table joins** — The T4 interfaces exist but discovery doesn't recognize 5+ parameter join chains. Look at how the discovery pipeline detects `IJoinedQueryBuilder` type arguments and extend the maximum arity.

2. **Wire QRY044/045 diagnostics** — `TryParseThroughNavigation` silently returns false. Add diagnostic collection for invalid junction/target navigation names.

3. **QRY043 reporting during chain analysis** — When `ResolveNavigationColumn` in `BuildProjection` can't resolve a target entity, collect a diagnostic and report through the pipeline error mechanism.
