# Plan: scope-trest-tuple-projection

## Goal recap

Pin down what `ProjectionAnalyzer` and `ReaderCodeGenerator` would need to do to handle C# `ValueTuple` arities at and beyond the 7-element flattening boundary (the `TRest` nesting case). Deliverable is **scope-only**: a written audit document plus four cross-dialect tests at arities 7, 8, 10, and 16 that pin down current runtime behavior. **No generator changes.**

This is preparatory work for an upcoming feature: an analyzer code-fix that rewrites anonymous-type projections (`new { … }`) to named tuples (`(Name: …, …)`). Anonymous types have no arity ceiling, so the rewrite can land users directly in `TRest` territory without warning. Before shipping that rewrite we want certainty about whether the projection layer survives 8+ elements as-is.

## Background — what `TRest` actually is

C# represents tuples through the `System.ValueTuple<…>` family, which has overloads for arities 1–8. The 8-arity overload is special: its eighth type parameter is named `TRest`, and the C# compiler requires `TRest` to itself be a `ValueTuple`. So a 10-element tuple `(a, b, c, d, e, f, g, h, i, j)` is *not* a struct with ten fields — it is `ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8, T9, T10>>`, and `tuple.Item8` is rewritten by the compiler to `tuple.Rest.Item1`. Source code keeps the flat `(a, b, …, j)` syntax both for declaration and access; the nesting is invisible above the IL layer.

This matters for Quarry because the generator emits source code that becomes part of the user's compilation, so as long as we emit the flat tuple syntax the user wrote, the C# compiler will fold it into the right `ValueTuple` shape. The risk is in places where the generator inspects the *type* (rather than the syntax) and assumes a flat shape — and in any path that re-emits a tuple from generator-side metadata rather than copying the user's syntax.

## Key concepts

### What's almost certainly fine

**Reader emission.** `ReaderCodeGenerator.GenerateTupleReader` at `Projection/ReaderCodeGenerator.cs:149` builds a flat `(r.GetX(0), r.GetY(1), …)` literal and returns it as the body of a `static (DbDataReader r) => …` lambda. The C# compiler synthesizes the nested `ValueTuple` for us. Element naming via `ItemN`-detection (line 165) operates on `column.PropertyName` and `column.Ordinal`, neither of which depend on tuple arity.

**Type-name building.** `TypeClassification.BuildTupleTypeName:303` and `ProjectionAnalyzer.BuildTupleTypeNameFromSymbol:1598` both produce flat `(int, string Name, …)` syntax. Roslyn's `INamedTypeSymbol.TupleElements` returns the full element list regardless of `TRest` nesting (it flattens for you), so `BuildTupleTypeNameFromSymbol` never sees `TRest` in the element collection.

**Carrier `TResult` parameter.** Carriers extend `CarrierBase<T, TResult>`. `TResult` is a CLR type, and the CLR represents the 10-tuple as the nested `ValueTuple<…, ValueTuple<…>>`. The generic substitution is opaque to Quarry — it just flows through.

### What's worth confirming

**`ProjectionAnalyzer.IsValidTupleTypeName:1637`.** Uses naive `inner.Split(',')` at line 1650 to check element count and names. This is *already wrong* for any arity that contains nested generics (e.g., `(Dictionary<int, string>, int)` would split into three parts), but the project already has a depth-aware `TypeClassification.SplitTupleElements:276` that's used elsewhere. Not strictly a `TRest` issue, but the same audit should fix it because the rewrite path for wide tuples is more likely to surface generic-arg cases.

**Set operation projection mismatch (QRY072).** `Quarry.Generators` raises `QRY072` when two arms of a `Union`/`Intersect`/`Except` have mismatched projections. The check operates on `ProjectionInfo.Columns.Count`, which is flat regardless of `TRest`. Should work, but worth a regression test if we expand coverage.

**Set-op auto-wrapping.** Post-set-op `Where`/`GroupBy`/`Having` wraps the prior chain as a subquery and the column list of the inner chain becomes the visible columns of the outer. With 8+ columns the inner SELECT and the outer alias list both need to enumerate all elements; if anything iterates `tuple.Arguments` (the syntax) it will see the 10 args; if anything iterates `TupleElements` (the symbol) it will also see 10. Mostly fine.

**CTE re-projection (`FromCte<T>`).** When `T` is a tuple type, `Quarry.Generators` resolves it via Roslyn — same flat-element behavior as a regular projection. But CTE chains traverse generic-argument lookups in a couple of places (`ChainAnalyzer.cs:2258, 2292`) where `BuildTupleTypeName(columns, fallbackToObject: false)` is called. With 8+ columns the produced flat type name is correct, but downstream consumers still need to round-trip it through Roslyn binding successfully.

**Diagnostics surface.** `QueryDiagnostics.ProjectionColumns` is a flat list with no arity cap. Cosmetic.

### What's at higher risk

**`IsValidTupleTypeName` with deep generic args.** Latent for any arity, more likely to surface as projection arity grows. Audit candidate.

**Anonymous-type rejection error message.** `ProjectionAnalyzer.cs:237/580/1445` rejects `AnonymousObjectCreationExpressionSyntax` with "Anonymous type projections are not supported. Use a named record, class, or tuple instead." The upcoming rewrite work needs to reach this error site and rewrite the syntax *before* the analyzer sees it (i.e., as a code-fix on the source, not as a generator behavior change). Audit candidate when planning the rewrite work; out of scope for this workflow.

## Phases

### Phase 1 — Add wide-tuple cross-dialect tests
**File to create:** `src/Quarry.Tests/SqlOutput/CrossDialectWideTupleTests.cs`

Mirrors the structure of `CrossDialectCompositionTests.cs` (cross-dialect SQL assertion via `QueryTestHarness.AssertDialects` plus end-to-end execution against all four real DB connections — SQLite in-memory, PostgreSQL/MySQL/SqlServer Testcontainers).

Four `[Test]` methods, one per arity:

#### Test 1 — `Tuple_7Elements_FlatLast`
Joined Users×Orders, project 7 elements covering the last *flat* `ValueTuple` arity:
```csharp
.Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total))
```
Expected SQL (per dialect): `SELECT t0."UserId", t0."UserName", … FROM "users" AS t0 INNER JOIN "orders" AS t1 ON t0."UserId" = t1."UserId"` etc., dialect-quoted appropriately. Execution asserts at least one row materializes and the named-element access (`r.UserName`) works.

#### Test 2 — `Tuple_8Elements_FirstNested`
Same join, add `o.Status`:
```csharp
.Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status))
```
This is the first arity that compiles to `ValueTuple<…, ValueTuple<string>>`. Same assertion shape as Test 1, but the runtime-side assertion (`results[0].Status == "Shipped"` or whatever the seed data yields) verifies that `Item8`-style access is correctly rewritten to `Rest.Item1` by the compiler in our generated reader.

#### Test 3 — `Tuple_10Elements_DeeperNested`
Same join, add `o.Priority, o.OrderDate, o.Notes`:
```csharp
.Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate))
```
(Choosing 10 elements that don't include the nullable `o.Notes` to keep the materialization clean — adding it is fine but adds an unrelated nullability concern.) Verifies materialization works at three positions inside the `Rest` segment.

#### Test 4 — `Tuple_16Elements_DeepDoubleNested`
Three-table join Users×Orders×OrderItems with 16 columns:
```csharp
.Select((u, o, oi) => (
    u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, u.LastLogin,
    o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate,
    oi.OrderItemId, oi.ProductName, oi.Quantity, oi.UnitPrice, oi.LineTotal))
```
This crosses *two* `TRest` boundaries — the runtime tuple shape is `ValueTuple<U1..U7, ValueTuple<U8..U14, ValueTuple<U15, U16>>>`. Same execution assertion pattern: assert row count, named access at three different nesting depths (e.g., `r.UserName` at depth 0, `r.OrderId` at depth 1, `r.OrderItemId` at depth 1, `r.LineTotal` at depth 2).

#### Test conventions (all four)
- Use `QueryTestHarness.CreateAsync()` which seeds users + orders + order_items.
- `Prepare()` each chain on Lite/Pg/My/Ss, call `ToDiagnostics()` and `QueryTestHarness.AssertDialects(...)` for SQL string parity across the four dialects.
- Then execute on all four (matches existing pattern in `CrossDialectCompositionTests`).
- Element-name assertions: read at least one named element back and assert against a known seed value to prove the named-tuple access path works through `Rest` levels.

### Phase 2 — Write `scope.md`

Create `_sessions/scope-trest-tuple-projection/scope.md` containing:

1. **Background** — the `TRest` mechanic, in 4–6 lines.
2. **Where this matters** — pointer to the upcoming anon→named-tuple code-fix as the load-bearing motivation.
3. **Audit table** — one row per code location with arity-sensitive logic, with a verdict from the test results:

   | Location | Behavior | Risk | Verdict (after Phase 1) |
   |---|---|---|---|
   | `ReaderCodeGenerator.GenerateTupleReader:149` | Emits flat tuple literal | Low — C# folds | TBD |
   | `TypeClassification.BuildTupleTypeName:303` | Emits flat type name | Low — C# folds | TBD |
   | `ProjectionAnalyzer.BuildTupleTypeNameFromSymbol:1598` | Reads `TupleElements` (flat) | Low | TBD |
   | `ProjectionAnalyzer.IsValidTupleTypeName:1650` | Naive `Split(',')` | Latent (generic args) | Audit candidate |
   | `ChainAnalyzer.cs:2258, 2292` | Rebuilds tuple type from columns | Low | TBD |
   | Set-op projection mismatch (QRY072) | Counts flat columns | Low | TBD |
   | CTE re-projection (`FromCte<T>`) | Roslyn binding round-trip | Medium | TBD |

   "TBD" entries get filled in based on whether the Phase 1 tests pass or fail.

4. **Findings from the tests** — written after Phase 1 runs. If all four pass: "TRest works at runtime today, in the joined-projection scenarios most likely to be hit by an anon→tuple rewrite. Latent risks: [list]." If any fail: pin the specific fault, the affected stage of the pipeline, and a hypothesis about the fix.

5. **Recommendations** — what a follow-up workflow would need to do. Options:
   - **(a) ship the anon→tuple rewrite now** — if everything passes, the rewrite path is unblocked; the only generator-side fix is hardening `IsValidTupleTypeName` to use depth-aware splitting.
   - **(b) ship the rewrite with an arity guard** — code-fix only offers itself for ≤7 elements; 8+ remains an explicit error (`ProjectionFailureReason.AnonymousTypeNotSupported`). Lowest risk, gives users a clear "run the fix and add a record" path for wide projections.
   - **(c) implement explicit TRest-aware logic before shipping the rewrite** — defensive, but only justified if Phase 1 reveals breakage.

6. **Out of scope for this workflow** — anything that requires a generator change.

### Phase 3 — Stage and commit

Single commit covering both Phase 1 (test file) and Phase 2 (scope document) plus the session directory state. Commit message: `Add wide-tuple projection tests and TRest scoping document`.

## Dependencies between phases

Phase 1 must run before Phase 2's "Findings" section can be written — the audit table verdicts depend on whether the tests pass or fail.

Phase 3 (commit) depends on both.

If Phase 1 surfaces a hard test failure that requires generator investigation to understand, we go back to DESIGN to discuss whether the workflow scope should expand from "scope + tests" to "scope + tests + minimal fix." Per the original DESIGN decision (`Scope-only deliverable, no generator changes`), the default reaction to a failure is to *document* it in scope.md, not fix it.

## Test verification

After Phase 1: run `dotnet test src/Quarry.Tests --filter "FullyQualifiedName~CrossDialectWideTuple"` — expect either four passes (most likely) or specific failure diagnostics that get folded into Phase 2 findings.

Full suite must remain green before commit (same baseline as INTAKE: 3022 passing).
