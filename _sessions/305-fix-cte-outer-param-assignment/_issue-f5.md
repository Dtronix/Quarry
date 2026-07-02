## Description
The "resolve a CteDefinition site to its CteDef" lookup — `CteNameHelpers.ExtractShortName(site.Bound.Raw.CteEntityTypeName ?? site.EntityTypeName)` compared against `CteDef.Name`, first match wins — is now duplicated in four places. `CteDef.cs`'s doc contract ("both sides MUST use the same helper so the names compare equal under all input forms") is enforced only by convention; a shared lookup helper would give it a single enforcement point.

## Location
- `src/Quarry.Generator/IR/AssembledPlan.cs` — `GetCteInnerParamCount` (added in #306)
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — `BuildLambdaInnerExtractionPlan` (~line 456)
- `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` — `EmitCteDefinition`, twice (~lines 173–221)
- Contract doc: `src/Quarry.Generator/IR/CteDef.cs` (`CteNameHelpers` remarks, ~lines 121–128)

## Diagnostics
None — this is a consolidation/maintainability cleanup, not a defect. Flagged as review finding F5 (severity L) during #306's review pass.

## What Has Been Tried
Nothing yet; the duplication was deliberately kept in #306 to mirror the established pattern rather than widen that PR's scope.

## Gathered Information
- All four sites use identical semantics: short-name match, first match wins.
- Duplicate CTE names are rejected at compile time by QRY082, and failed-analysis CTEs produce no CteDef (chain already carries QRY080), so first-match/no-match semantics are uniform across call sites.

## Suggested Approach
Add a static helper next to `CteNameHelpers` (e.g. `CteNameHelpers.FindCteDef(TranslatedCallSite site, IReadOnlyList<CteDef> cteDefinitions)` or an overload taking the raw type name) and route all four call sites through it. Pure refactor — behavior must be byte-identical; the existing CTE test suite (CrossDialectCteTests, LambdaCteTests, MySqlBindOrderGenerationTests, CarrierGenerationTests CTE regions) is the regression gate.
