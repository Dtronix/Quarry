## Description

Three related hardening items in closure-capture resolution, all surfaced by the code review of #333.
None is a live failure in a shape the test suite covers; each is a way the current design can mispredict
or over-reject without a loud signal.

**1. `switch`-expression arm variables are unhandled.** They own a display class like the other
scope-owning forms, but their field is **name-mangled**:

```csharp
o switch { string s => (() => s.Length)(), _ => 0 }
// emits <>c__DisplayClass2_1 with a field named `<s>5__2`, NOT `s`
```

So both the closure ordinal and the accessor's `Name = "s"` would be wrong. Simply adding the form to
`IsOwnScopeStatement` is **not** sufficient and would trade one wrong prediction for another — the field
name has to be predicted too, or the shape detected and disqualified.

**2. The multi-scope guard is keyed on dataflow captures, not on the extraction plan.** `CountCaptureScopes`
counts scopes among `dataFlow.CapturedInside` (filtered to declarations outside the clause), while the
extractors that are actually emitted are built later from `ClauseExtractionPlan.Extractors`. Nothing ties
the two together. A clause whose second-scope capture never becomes an extractor would be rejected at
build time even though it would have worked.

This is not hypothetical: the first version of that guard counted *all* of `CapturedInside` and rejected
several passing suites (`CrossDialectNestedSubqueryTests`, `CrossDialectSetOperationTests`,
`MySqlIntegrationTests`) because a nested subquery lambda contributes its own parameters. That was caught
only by running the suite. Deriving the count from the extraction plan would make over-firing
structurally impossible rather than empirically absent.

**3. `CapturedScopeCount == 0` conflates "not analysed" with "one scope."** The field defaults to `0`, and
every early `continue` in the `DisplayClassEnricher.EnrichAll` loop (missing syntax tree, span out of
range, stale-node recovery mismatch, no enclosing method, `methodOrdinal < 0`) leaves it there. The guard
therefore does not fire on sites whose enrichment was skipped. An `int?` or a `-1` sentinel would make
the unanalysed case explicit.

## Location

- `src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs` — `IsOwnScopeStatement`,
  `CountCaptureScopes`, `IsExtractableCapture`.
- `src/Quarry.Generator/Parsing/DisplayClassEnricher.cs` — the `EnrichAll` loop and its early exits.
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — `BuildExtractionPlans`, where the extractors that
  actually get emitted are decided.
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — `CheckDisqualifiers`, the `CapturedScopeCount > 1`
  branch.

## Diagnostics

No diagnostic for (1) — it fails at execution with `MissingFieldException` (mangled field name) or
`InvalidCastException` (wrong ordinal). (2) would surface as an unexpected QRY032 on a chain the user
expects to work. (3) is silent by construction.

## What Has Been Tried

- The `switch`-expression mangling was confirmed by dumping the emitted display classes from a compiled
  assembly by reflection; the field really is `<s>5__2`.
- The scope→display-class ground truth for every form that *is* handled (`foreach`, `for`, `using`,
  `switch` section, `catch`, lambda/local-function parameters) is tabulated in the "Display Class
  Prediction" section of `src/Quarry.Generator/llm.md`, dumped the same way.
- The guard's over-firing failure mode and its fix are recorded in that same section, and in
  `DisplayClassNameResolver.AnalyzeMethodClosures`'s comment explaining why the three capture loops
  deliberately differ.

## Suggested Approach

1. **switch-expression arms** — either predict the mangled field name (`<{name}>5__{n}`, itself another
   undocumented pattern needing ground truth) or, preferably, detect the form and disqualify with a
   QRY032 naming it. Failing loudly at build time is consistent with how the other unrepresentable
   capture shapes are handled.
2. **Guard input** — derive the scope count from the extraction plan, so the set that drives rejection is
   exactly the set that would be emitted. This likely means moving the check after plan construction, or
   computing a provisional plan during analysis.
3. **Sentinel** — make `CapturedScopeCount` nullable (or `-1` when unanalysed) and decide explicitly what
   the guard should do for an unanalysed site rather than defaulting to "allow".

Add a codegen test per item; note that a wrong prediction still compiles, so items 1 and 3 also need an
execution test to be meaningful.
