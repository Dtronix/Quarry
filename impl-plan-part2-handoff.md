# Work Handoff

## Key Components

| Area | Files |
|------|-------|
| Projection (navigation in Select) | `ProjectionInfo.cs` (NavigationHops), `ProjectionAnalyzer.cs` (chain detection), `ImplicitJoinHelper.cs`, `ChainAnalyzer.cs` (BuildProjection enrichment + table-qualification) |
| Diagnostics | `EntityInfo.cs` (Diagnostics property), `SchemaParser.cs` (QRY040-042, QRY044-045 collection), `ChainAnalyzer.cs` (QRY043 reporting), `QuarryGenerator.cs` (diagnostic reporting) |
| HasManyThrough fixes | `SqlExprBinder.cs` (ThroughNav priority over Nav), `SchemaParser.cs` (>= 2 type args), `Schema.cs` (TSelf type param) |
| Parser precision | `SqlExprParser.cs` (KnownDotNetMembers exclusion set) |
| 5-6 table join discovery | `UsageSiteDiscovery.cs` (builder names, ExtractResultType, IsJoinedBuilderName, ExtractJoinedEntityTypeNames), `VariableTracer.cs` (IsBuilderTypeName) |
| Test schemas | `AddressSchema.cs`, `UserAddressSchema.cs`, `WarehouseSchema.cs`, `ShipmentSchema.cs` |
| Test contexts | `TestDbContext.cs`, `PgDb.cs`, `MyDb.cs`, `SsDb.cs` (added Address, UserAddress, Warehouse, Shipment accessors) |
| Test harness | `QueryTestHarness.cs` (addresses, user_addresses, warehouses, shipments tables + seed data) |
| Tests | `CrossDialectNavigationJoinTests.cs` (4 nav-in-Select tests), `CrossDialectHasManyThroughTests.cs`, `JoinedCarrierIntegrationTests.cs` (5-6 table join tests) |

## Completions (This Session)

- **Extend UsageSiteDiscovery for 5-6 table joins** — Added `IJoinedQueryBuilder5`/`IJoinedQueryBuilder6` to 5 pipeline locations: builder names set, `ExtractResultType()`, `IsJoinedBuilderName()`, `ExtractJoinedEntityTypeNames()`, and `VariableTracer.IsBuilderTypeName()`. Added warehouse/shipment seed data. Added 5-table and 6-table integration tests with full cross-dialect SQL assertions and SQLite execution verification. 2 new tests pass.
- **Wire QRY044/045 diagnostics** — Added `List<Diagnostic>? diagnostics` parameter to `TryParseThroughNavigation`. Reports QRY044 when junction navigation lambda member name can't be extracted, QRY045 when target navigation lambda member name can't be extracted. Updated call site in `SchemaParser` second pass to thread `parseDiagnostics`.
- **Report QRY043 during chain analysis** — Added `List<DiagnosticInfo>? diagnostics` parameter to `BuildProjection` and `ResolveNavigationColumn`. When `registry.Resolve()` returns null for a navigation target entity, reports QRY043 with navigation property name, source entity name, and target entity name. Diagnostics threaded from `AnalyzeChainGroup` → `BuildProjection` → `ResolveNavigationColumn`.

## Previous Session Completions

- **Phase 1: Navigation access in Select projections** — Full pipeline: `NavigationHops` on `ProjectedColumn`, navigation chain detection in `ProjectionAnalyzer` (single-entity and joined paths), `ImplicitJoinHelper` shared helper, `ResolveNavigationColumn` in `BuildProjection`, null-forgiving operator (`!`) support in chain walking, semantic model type resolution at discovery time. 4 new cross-dialect tests.
- **Phase 2: Table-qualify Select columns** — Post-process projection columns after `BuildProjection`: any primary entity column with `TableAlias == null` gets `"t0"` when implicit joins exist. Aggregate columns excluded. Updated all 8 navigation join test expectations.
- **Phase 3: Wire QRY040-042 diagnostics** — Added `Diagnostics` property to `EntityInfo`. Modified `TryParseSingleNavigation` to report QRY040 (no FK), QRY041 (ambiguous FK), QRY042 (invalid HasOne column). Diagnostics collected during parsing and reported in generator.
- **Phase 4: HasManyThrough cross-dialect tests** — Created `AddressSchema`, `UserAddressSchema`. Added `HasManyThrough` on `UserSchema`. Fixed `HasManyThrough` API (added `TSelf` type param for type-safe junction lambda). Fixed binder to check `ThroughNavigations` before `Navigations` (was shadowed by regular nav). Fixed parser to accept >= 2 type args. 1 new cross-dialect test with exact SQL assertions.
- **Phase 5: 5-6 table join infrastructure** — Created `WarehouseSchema`, `ShipmentSchema`. Registered in all contexts with SQLite table creation.
- **Phase 6: Parser precision** — Added `KnownDotNetMembers` exclusion set (Length, Year, Month, Day, Ticks, etc.) to prevent false-positive `NavigationAccessExpr` for .NET property accesses on column values.
- **Feature C: Explicit joins to 6 tables** — T4 templates generate `IJoinedQueryBuilder` interfaces (arity 2-6).
- **Feature A: `One<T>` navigation joins** — Full pipeline for Where, OrderBy, GroupBy, Having. Deep chains. Dedup. INNER/LEFT inference.
- **Feature B: `HasManyThrough`** — Runtime types and schema parsing. Binder expansion in `BindSubquery`.
- **Diagnostics QRY040-045** — Descriptors added in `DiagnosticDescriptors.cs`.

## Progress

All 6 phases from `impl-plan-part2.md` complete. All 3 follow-up items (5-6 table discovery, QRY044/045, QRY043) complete. 2465 tests pass (2454 original + 11 new). All planned navigation join features are fully functional with diagnostic coverage.

## Current State

The branch is clean. All tests pass. No in-progress work items.

## Known Issues / Bugs

1. **SqlExprParser still emits NavigationAccessExpr for some non-navigation chains** — The `KnownDotNetMembers` exclusion is conservative. Custom property accesses like `o.SomeProp.CustomField` will still produce `NavigationAccessExpr` if neither name is in the exclusion list. The binder's fallback handles this gracefully.

2. **QRY044/045 validation is syntax-only** — The diagnostics fire when lambda member names can't be extracted from `HasManyThrough` declarations. Cross-entity validation (whether the referenced names actually point to `Many<T>` / `One<T>` properties on the junction entity) would require a second validation pass after all entities are parsed, which is not implemented.

## Dependencies / Blockers

- **dotnet-t4 global tool** — Must be installed to regenerate T4 templates. Not required for normal builds.

## Architecture Decisions

1. **Null-forgiving operator handling in chain parsing** — `TryParseNavigationChain` and all chain-walking helpers unwrap `PostfixUnaryExpressionSyntax` (SuppressNullableWarning) at each level. This is necessary because `o.User!.UserName` has the `!` between member accesses, producing nested syntax that the original simple loop didn't handle.

2. **HasManyThrough API uses TSelf generic** — Changed `Func<Schema, object?>` to `Func<TSelf, object?>` with a third type parameter. This allows the junction lambda to reference `Many<T>` properties on the concrete schema class. The parser ignores the third type argument.

3. **Binder checks ThroughNavigations before Navigations** — A `HasManyThrough` property has both a `NavigationInfo` (needed for entity property generation and subquery structure) and a `ThroughNavigationInfo` (for junction expansion). Previously the regular nav was found first, bypassing the junction logic. Now through-nav takes priority.

4. **Table-qualification in chain analysis, not projection analysis** — Primary entity columns get `"t0"` table alias only when implicit joins exist, and this is applied as a post-processing step in the chain analysis loop (after BuildProjection). This keeps ProjectionAnalyzer stage-agnostic.

5. **QRY043 reported via DiagnosticInfo in chain analysis, not PipelineErrorBag** — The chain analyzer already receives a `List<DiagnosticInfo>` parameter, so QRY043 is reported through that mechanism rather than the ThreadStatic PipelineErrorBag side-channel. This avoids the Bind-stage return type constraint that necessitated PipelineErrorBag.

6. **5-6 table join discovery is purely additive** — Only 5 locations in the discovery pipeline needed updating to add `IJoinedQueryBuilder5`/`IJoinedQueryBuilder6` recognition. All downstream stages (CallSiteBinder, CallSiteTranslator, ChainAnalyzer, SqlAssembler, CarrierAnalyzer, emitters) already handle joined entity type name lists of any length.

## Open Questions

None currently.

## Next Work (Priority Order)

1. **Cross-entity validation for QRY044/045** — Current diagnostics only fire on lambda extraction failure. A second validation pass (after all entities are parsed) could check that `junctionNavigationName` actually references a `Many<T>` on the junction entity, and `targetNavigationName` references a `One<T>`. This would catch schema definition errors earlier.

2. **Expand KnownDotNetMembers exclusion list** — As users hit false-positive `NavigationAccessExpr` for custom property chains, add their member names to the exclusion set in `SqlExprParser.cs`.
