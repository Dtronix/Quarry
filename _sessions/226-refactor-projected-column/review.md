# Review: 226-refactor-projected-column

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 4 (ProjectionAnalyzer `with` conversions) is marked complete in `workflow.md` but `ProjectionAnalyzer.cs` has zero diff vs master. The plan called for converting ~9 clone-like sites to `with` expressions. | Medium | Either the phase was skipped intentionally (all 21 sites construct from `ColumnInfo`, not clones) or it was missed. The plan's own Phase 4 description acknowledges "sites constructing from ColumnInfo ... remain as named constructor calls", so the most likely explanation is that audit found zero eligible clone sites. However, `workflow.md` should document this decision. |
| Phase 5 lists `EntityReaderTests.cs`, `PipelineOrchestratorTests.cs`, `TypeMappingProjectionTests.cs`, `TypeClassificationTests.cs` as targets, but none were modified. | Low | These files already used named parameters, so no changes were needed. Correct outcome, but the plan implied they would be touched. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| **Behavior change at line ~762 (CTE ordinal clone)**: Old code used 16 positional args, stopping at `col.IsEnum`, which caused `NavigationHops` to default to `null` and `IsJoinNullable` to default to `false`. New `col with { Ordinal = ord++ }` preserves both fields. | Medium | This is likely a latent **bug fix** rather than a regression -- the old code silently dropped `NavigationHops` and `IsJoinNullable` when re-numbering ordinals for CTE column filtering. The `with` expression correctly preserves all fields. However, if any downstream code relied on the stripping behavior (e.g., expecting `NavigationHops == null` after CTE reduction), this could change output. Should be validated against navigation-through-CTE test scenarios. |
| **Behavior change at line ~1835 (aggregate type resolve)**: Old code omitted `CustomTypeMapping`, `IsForeignKey`, `ForeignKeyEntityName`, `IsEnum`, `NavigationHops`, `IsJoinNullable` (all defaulted). New `with` preserves them from `col`. | Medium | Same pattern: likely a latent bug fix. Aggregate columns are unlikely to have FK or navigation metadata, so the practical impact is near-zero, but the semantic correctness is improved. |
| **Behavior change in `StripNonAggregateSqlExpressions`**: Old code omitted `NavigationHops` (defaulted to `null`). New `with` preserves it. | Low | Non-aggregate computed columns could theoretically carry `NavigationHops` from the new pipeline; preserving them is the correct behavior for the bridge compatibility layer. |
| `Equals(object?)` override removed (line 296 on master). | None | Records auto-generate `public override bool Equals(object? obj)` which delegates to the typed `Equals(ProjectedColumn?)`. The removed line did the same thing (`Equals(obj as ProjectedColumn)`). Behavior is identical. |
| Custom `Equals(ProjectedColumn?)` and `GetHashCode()` are preserved. `NavigationHops` uses `EqualityHelpers.SequenceEqual`. | None | Correct. The compiler defers to user-provided equality members in records. The `IReadOnlyList<string>?` field gets proper sequence equality rather than reference equality. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No new tests were added for the behavior changes identified above (field preservation in `with` expressions vs field dropping in old constructor calls). | Low | The existing 3062-test suite provides coverage through integration/snapshot tests. The behavior changes are improvements (preserving fields instead of dropping them), so dedicated unit tests for the old dropping behavior would be counterproductive. However, a targeted test confirming `NavigationHops` survives CTE ordinal re-numbering would strengthen confidence. |
| Test file changes are limited to `WastedWorkRuleTests.cs` and `SignCastReaderTests.cs` (named parameter conversion only). | None | These are pure cosmetic changes (positional to named args). Correct and consistent. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `IsExternalInit` polyfill matches the well-known `manuelroemer/IsExternalInit` NuGet package pattern. Uses `internal static class`, `[ExcludeFromCodeCoverage, DebuggerNonUserCode]` attributes. | None | Standard practice for enabling C# 9 features in netstandard2.0 projects. |
| Remaining `new ProjectedColumn(...)` calls (30+ sites in `ProjectionAnalyzer.cs`, `ChainAnalyzer.cs`, `QuarryGenerator.cs`, and test files) all use named parameters consistently. | None | Good consistency. |
| `with` expressions used only at true clone-with-modification sites; fresh constructions from `ColumnInfo` correctly remain as constructor calls. | None | Clean separation of concerns. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `init` setters make properties publicly settable (via `with` or object initializer) from any code within the same assembly (`internal` visibility of the record limits this to `Quarry.Generator`). | None | Since `ProjectedColumn` is `internal`, the `init` setters do not expand the public API surface. No external consumers can use them. |
| Shallow copy semantics for `IReadOnlyList<string>? NavigationHops` in `with` expressions. | None | `IReadOnlyList<string>` is immutable-by-interface. The `with` expression shares the same list reference, but since the list contents cannot be mutated through this interface and the strings themselves are immutable, shallow copy is safe. No `with` site modifies `NavigationHops`, so the shared reference is never a problem. |
| No changes to `ProjectionAnalyzer.cs` despite 21 constructor call sites. | None | All sites construct from `ColumnInfo` (a different source type), not from an existing `ProjectedColumn`. These are not clone-with-modification patterns and correctly remain as constructor calls. |

## Classifications

| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Plan Compliance | Phase 4 marked complete with no changes to ProjectionAnalyzer | Medium | D | No action — correct outcome, all 21 sites construct from ColumnInfo |
| 2 | Correctness | CTE ordinal clone now preserves `NavigationHops` and `IsJoinNullable` | Medium | D | No action — improvement over old behavior, all tests pass |
| 3 | Correctness | Aggregate resolve clone now preserves 6 previously-defaulted fields | Medium | D | No action — improvement, zero practical impact |
| 4 | Correctness | `StripNonAggregateSqlExpressions` now preserves `NavigationHops` | Low | D | No action — improvement |
| 5 | Test Quality | No targeted tests for field-preservation behavior changes | Low | D | No action — existing 3062 tests cover implicitly |
| 6 | Plan Compliance | Phase 5 test files already used named params, no changes needed | Low | D | No action — correct outcome |

## Issues Created

None.
