# Plan: 283-analyzer-thenby-having

## Goal
Add two new analyzer warnings (`QRA403`, `QRA404`) plus one code fix that detect chain-continuation methods called without their required anchor:

- `QRA403` — `.ThenBy(...)` / `.ThenByDescending(...)` called without a preceding `.OrderBy(...)` / `.OrderByDescending(...)` in the same fluent chain.
- `QRA404` — `.Having(...)` called without a preceding `.GroupBy(...)` in the same fluent chain.
- `ThenByToOrderByCodeFix` — renames the `ThenBy` method-name token to `OrderBy` (preserving the "Descending" suffix), fixing `QRA403` only.

These guard against semantic foot-guns made newly compileable by PR #281, which added chain-continuation methods to `IEntityAccessor<T>`.

## Key concepts

### How analyzer rules are wired in
`Quarry.Analyzers.QuarryQueryAnalyzer` runs every rule in its `Rules` array against every interceptable Quarry call site. Each rule inspects `QueryAnalysisContext.Site.Kind` (an `InterceptorKind` enum) and either matches or short-circuits via `yield break`. The site is produced by `UsageSiteDiscovery.DiscoverRawCallSite` which classifies the call by its method name and receiver type, so for our purposes we can trust that `Site.Kind == InterceptorKind.ThenBy` only fires on actual `ThenBy` / `ThenByDescending` calls on Quarry chains. Identical for `InterceptorKind.Having`.

Two registration sites in `QuarryQueryAnalyzer.cs`:
1. `Rules` (`ImmutableArray<IQueryAnalysisRule>`) — instance list, must include the new rules.
2. `SupportedDiagnostics` (`ImmutableArray<DiagnosticDescriptor>`) — descriptor list, must include the new descriptors.

### Receiver-chain backward walk
The receiver chain is walked by descending `MemberAccessExpressionSyntax.Expression`. This is identical to the algorithm already used in `Rules/Dialect/UnsupportedForDialectRule.cs` for the SQL-Server "OFFSET without ORDER BY" check. Algorithm:

```csharp
SyntaxNode? current = context.InvocationSyntax;        // start at the ThenBy/Having invocation
while (current is InvocationExpressionSyntax invocation &&
       invocation.Expression is MemberAccessExpressionSyntax memberAccess)
{
    var name = memberAccess.Name.Identifier.Text;
    if (AnchorMethods.Contains(name))
        return /* anchor found, do not flag */;
    current = memberAccess.Expression;                 // descend to receiver
}
// fell off the chain root without finding the anchor → flag
```

The walk is transparent to intervening calls (`Where`, `Select`, `Distinct`, `Limit`, `Offset`, `Trace`, set ops). The walk stops naturally at the chain root (an identifier like `db` or a method call that is not member-access — e.g. `db.Users()` whose Expression is not a MemberAccessExpression on its receiver but rather a different shape; the while-loop exits correctly because `db.Users()`'s Expression *is* `db.Users` which IS a MemberAccessExpression on `db`. The walk ends one step later because `db` is an `IdentifierNameSyntax`, not an `InvocationExpressionSyntax`. Either way the loop terminates).

### Diagnostic location
Both rules report on the method-name identifier (the `Name` member of the outer MemberAccessExpression), not the whole invocation. Standard convention: tighter squiggle, points exactly at the offending token. Anchor rule: `((MemberAccessExpressionSyntax)invocation.Expression).Name.GetLocation()`.

### Code fix scope
`ThenByToOrderByCodeFix` runs only against `QRA403`. It renames the method-name identifier:
- `ThenBy` → `OrderBy`
- `ThenByDescending` → `OrderByDescending`

The fix is a pure syntax-token swap. It registers `WellKnownFixAllProviders.BatchFixer` so multiple offenders in one document fix together.

No code fix for `QRA404` because the analyzer cannot infer the user's intended grouping key.

## Phases

Phases are sized so each is independently committable with green tests at the end. Phase 1 stands alone (descriptors + rules + tests). Phase 2 layers the code fix on top.

### Phase 1 — Add `QRA403` and `QRA404` rules with tests
Adds the two analyzer rules and their tests. After this phase, both warnings fire correctly across all targeted chain shapes.

**File: `src/Quarry.Analyzers/AnalyzerDiagnosticDescriptors.cs`**
Append two `DiagnosticDescriptor` static fields under the existing `── QRA4xx: Patterns ──` section comment. Both `Warning`, `isEnabledByDefault: true`, category `"QuarryAnalyzer"`. Suggested text:

- `ThenByWithoutOrderBy` — id `"QRA403"`, title `"ThenBy without preceding OrderBy"`, messageFormat `"'{0}' called without a preceding OrderBy/OrderByDescending in the chain; the resulting SQL is ORDER BY <key>, equivalent to OrderBy. Did you mean to use OrderBy() instead?"`. The `{0}` arg is the actual method name (`ThenBy` or `ThenByDescending`). description: explains the foot-gun.
- `HavingWithoutGroupBy` — id `"QRA404"`, title `"Having without preceding GroupBy"`, messageFormat `"Having() called without a preceding GroupBy() in the chain; HAVING applied to the whole result is almost never the intended semantic. Add a GroupBy(...) clause first."`. description: explains the foot-gun.

**File: `src/Quarry.Analyzers/Rules/Patterns/ThenByWithoutOrderByRule.cs` (new)**
New rule, model on `OrderByWithoutLimitRule.cs` for shape and on `UnsupportedForDialectRule.cs` for the receiver-walk loop.
- `RuleId => "QRA403"`, `Descriptor => AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy`.
- Static `HashSet<string> AnchorMethods = { "OrderBy", "OrderByDescending" }`.
- `Analyze`:
  1. `if (context.Site.Kind != InterceptorKind.ThenBy) yield break;`
  2. Walk `context.InvocationSyntax`'s receiver chain via the loop above.
  3. If anchor found, `yield break`.
  4. If walk completes without finding it, capture the method name and the location of the outer `MemberAccessExpressionSyntax.Name`, then `yield return Diagnostic.Create(Descriptor, location, methodName)`.

**File: `src/Quarry.Analyzers/Rules/Patterns/HavingWithoutGroupByRule.cs` (new)**
Same shape as the ThenBy rule, but anchored on `GroupBy`.
- `RuleId => "QRA404"`, `Descriptor => AnalyzerDiagnosticDescriptors.HavingWithoutGroupBy`.
- Static `HashSet<string> AnchorMethods = { "GroupBy" }`.
- `Analyze`:
  1. `if (context.Site.Kind != InterceptorKind.Having) yield break;`
  2. Same backward walk.
  3. Diagnostic message has no method-name placeholder (always `Having`), so just `Diagnostic.Create(Descriptor, location)`.

**File: `src/Quarry.Analyzers/QuarryQueryAnalyzer.cs`**
- Add `new ThenByWithoutOrderByRule()` and `new HavingWithoutGroupByRule()` to the `Rules` array under the `// QRA4xx: Patterns` section.
- Add `AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy` and `AnalyzerDiagnosticDescriptors.HavingWithoutGroupBy` to `SupportedDiagnostics`.

**File: `src/Quarry.Analyzers.Tests/PatternRuleTests.cs`** (extend the existing fixture)
The existing fixture has a `CreateContextFromSource(markedSource, methodName)` helper that hard-codes `kind: InterceptorKind.ExecuteFetchAll`. We need a helper that lets the test pick the kind. Add a second overload `CreateContextFromSource(markedSource, methodName, InterceptorKind kind)` and refactor the existing helper to delegate (default `ExecuteFetchAll` to keep existing tests valid).

Tests to add (named `QRA403_…` / `QRA404_…`):

QRA403 (ThenBy without OrderBy):
- `QRA403_ThenByWithoutOrderBy_Reports` — `db.Users().ThenBy(x => x.Id)` flagged.
- `QRA403_ThenByDescendingWithoutOrderBy_Reports` — `db.Users().ThenByDescending(x => x.Id)` flagged. (Need to call helper with method name `ThenByDescending`.)
- `QRA403_OrderByThenBy_NoReport` — `db.Users().OrderBy(x => x.Name).ThenBy(x => x.Id)` not flagged.
- `QRA403_OrderByDescendingThenBy_NoReport` — anchor recognized regardless of which OrderBy variant.
- `QRA403_OrderByThenWhereThenThenBy_NoReport` — intervening `Where` is transparent.
- `QRA403_PostCte_ThenByWithoutOrderBy_Reports` — `db.With<T>(...).FromCte<T>().ThenBy(x => x.Id)` flagged. (Synthetic source — exact compile-fidelity not required since the test bypasses semantic resolution.)
- `QRA403_UnionThenByWithoutOrderBy_Reports` — `q1.Union(q2).ThenBy(x => x.Id)` still flagged (set-op chains are not exempt).
- `QRA403_NonThenBySite_NoReport` — when `Site.Kind` is not `ThenBy`, the rule skips even if the source contains a `ThenBy` call.

QRA404 (Having without GroupBy):
- `QRA404_HavingWithoutGroupBy_Reports` — `db.Users().Having(x => x.Active)` flagged.
- `QRA404_GroupByHaving_NoReport` — `db.Users().GroupBy(x => x.Tag).Having(...)` not flagged.
- `QRA404_GroupByThenWhereThenHaving_NoReport` — intervening `Where` is transparent.
- `QRA404_PostCte_HavingWithoutGroupBy_Reports` — same coverage as QRA403's CTE case.
- `QRA404_NonHavingSite_NoReport` — `Site.Kind` is not `Having`, the rule skips.

Run `dotnet test src/Quarry.Analyzers.Tests/Quarry.Analyzers.Tests.csproj`. All existing tests + new tests must pass. Pre-existing build failures in `Quarry.Tests` (the QRY900 generator key collision recorded at INTAKE) are not in scope for this commit.

**Commit message:** `Add QRA403/QRA404 analyzer warnings: ThenBy without OrderBy, Having without GroupBy (#283)`

### Phase 2 — `ThenByToOrderByCodeFix`
Layered on top of Phase 1.

**File: `src/Quarry.Analyzers.CodeFixes/CodeFixes/ThenByToOrderByCodeFix.cs` (new)**
Model on `CountToAnyCodeFix.cs` and `SingleInToEqualsCodeFix.cs` for shape.
- Class: `ThenByToOrderByCodeFix : CodeFixProvider`, decorated with `[ExportCodeFixProvider(LanguageNames.CSharp), Shared]`.
- `FixableDiagnosticIds = ImmutableArray.Create("QRA403")`.
- `GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;`
- `RegisterCodeFixesAsync`:
  1. Load syntax root, find the diagnostic span's node.
  2. Walk `AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault()` — the diagnostic is on the `Name` token but we need the parent member-access to swap the name.
  3. Read the current method-name text. Compute new name: `"ThenByDescending" → "OrderByDescending"`, otherwise `"OrderBy"`. (Defensive: if the name isn't either ThenBy variant, return — happens only if descriptor is misregistered.)
  4. Register one `CodeAction.Create("Replace ThenBy with OrderBy", …, equivalenceKey: "QRA403_ThenByToOrderBy")`.
  5. Inside the action: build a new `MemberAccessExpressionSyntax` via `WithName(SyntaxFactory.IdentifierName(newName))`, swap into the syntax root, return the updated document.

**File: `src/Quarry.Analyzers.Tests/CodeFixTests.cs` (extend)**
Add three tests, mirrored on the existing `CountToAnyCodeFix_*` block:
- `ThenByToOrderByCodeFix_FixesCorrectDiagnosticId` — asserts `FixableDiagnosticIds` contains `"QRA403"`.
- `ThenByToOrderByCodeFix_HasFixAllProvider` — asserts `GetFixAllProvider()` is non-null.
- `ThenByToOrderByCodeFix_RegistersCodeFix` — functional test mirroring `CountToAnyCodeFix_RegistersCodeFix`. Builds an `AdhocWorkspace`, creates a synthetic `QRA403` diagnostic on a `.ThenBy(...)` call, asserts exactly one code action is registered with title containing `"OrderBy"`.
- (Optional) `ThenByDescendingToOrderByDescendingCodeFix_RegistersCodeFix` — same with the Descending variant.

Run `dotnet test src/Quarry.Analyzers.Tests/Quarry.Analyzers.Tests.csproj`. All previous tests + new tests pass.

**Commit message:** `Add ThenByToOrderByCodeFix for QRA403 (#283)`

## Dependencies
Phase 2 depends on Phase 1's `QRA403` diagnostic ID being registered. No other ordering constraints.

## Out of scope
- No changes to `IEntityAccessor<T>` or `IQueryBuilder<T>` API surface — the fluent surface stays as-is.
- No fix to the pre-existing `QRY900` generator key collision in `Quarry.Tests` (recorded as a baseline failure on master at INTAKE).
- No diagnostics on set-op family methods themselves (`Union`, `Intersect`, `Except`) — issue explicitly out of scope.
- No code fix for `QRA404` — would require synthesizing a grouping expression we cannot infer.

## Risks & mitigations
- **False positives across method-call argument boundaries.** The receiver walk does NOT descend into argument-side expressions (e.g. `db.With<T>(query.OrderBy(x => x.Id)).FromCte<T>().ThenBy(...)`) — the inner `OrderBy` is on the With's argument, not the outer chain's receivers, so the outer ThenBy is flagged. This is correct: the inner OrderBy applies to the CTE's inner query, not to the outer FromCte selection, so the outer ThenBy still has no anchor in its own chain. Mitigation: covered by a test in Phase 1 (the post-CTE test reflects this).
- **Set-op confusion.** Per the design decision, `q1.Union(q2).ThenBy(...)` is flagged. If the user actually wants ordering on the unioned result, the right code is `q1.Union(q2).OrderBy(...)` — which is what the QRA403 code fix produces. So both diagnostic and code fix are correct for this case.
- **Test-helper churn.** Adding the kind-overload to `CreateContextFromSource` may need to be handled carefully to avoid breaking the existing `QRA401` tests. Mitigation: keep the original signature as a forwarder to the new one with default `InterceptorKind.ExecuteFetchAll`.
