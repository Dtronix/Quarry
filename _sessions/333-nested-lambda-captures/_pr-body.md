## Summary

- Closes #333

A Quarry chain written inside a lambda emitted an interceptor that referenced the enclosing method's
locals directly, so the generated file failed to compile with `CS0103` — a build break inside generated
code, with no Quarry diagnostic. Root-causing it surfaced three further capture-resolution defects that
failed *silently at runtime*, plus one shape that cannot be supported at all. This fixes four and rejects
the fifth at build time.

## Reason for Change

The generator predicts compiler-generated display-class names in order to emit `[UnsafeAccessor]`
extractors for captured variables without reflection. Several of those predictions were wrong:

1. **Sites inside a lambda were never enriched.** `DisplayClassEnricher` unwrapped only
   `MethodKind.LocalFunction`, so an enclosing lambda's `AnonymousFunction` symbol was left in place,
   `ComputeMethodOrdinal` returned `-1`, and the site was skipped entirely — no extraction plan, so the
   raw captured-local name was emitted. That is the CS0103.

   The issue title says "doubly-nested", but the trigger is **any** enclosing lambda that is an
   invocation argument, at depth 1 or deeper. `new Func<>(lambda)` only appeared to work because it trips
   the QRY032 lambda-capture disqualifier first, which masks the bug.

2. **Declarations that own a scope were mis-scoped.** A lambda parameter, or a
   `foreach`/`for`/`using`/`switch`-section/`catch` declaration, resolved to the enclosing block. Verified
   against emitted IL: each of those owns its **own** display class, and a lambda's parameters share one
   class with its body-block locals. The collision shifted every later closure ordinal. Single-scope
   chains passed only because ordinal 0 is correct by accident — the bug was invisible until a method had
   two capture scopes.

3. **An instance field mixed with a local was read off the wrong object.** With only a field captured the
   delegate target is the containing instance; add a local and the compiler interposes a display class
   holding `<>4__this`. The generator now emits a `<>4__this` accessor returning `ref TContaining` and
   reads the field from that.

4. **A clause capturing locals from two or more closure scopes cannot be emitted at all** and is now
   rejected at build time. The outer scope is reachable only through the compiler's `CS$<>8__locals` link
   field, whose type is another display class — and a field accessor must return byref while a byref
   return cannot name an inaccessible type
   ([dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664), open, milestone `Future`,
   deliberately excluded as not memory safe).

   The `Unsafe.As` shadow-overlay alternative was implemented and rejected: it is undefined behaviour
   ([dotnet/runtime#111049](https://github.com/dotnet/runtime/discussions/111049) — display classes hold
   reference fields, so they are non-blittable, get `Auto` layout and have no guaranteed offsets), and its
   failure mode is silent. With two same-typed fields a mismatched overlay returns the values **swapped**,
   binding `@p0` to the wrong variable and returning wrong rows with no error at all.

## Impact

Shapes that previously threw at runtime now work:

```csharp
// #333: chain inside nested lambdas
contexts.Select((db, i) => Task.Run(async () => {
    var name = $"Worker{i}";
    await db.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
}));

// loop variable and a method local, in separate clauses
foreach (var name in names)
    await db.Users().Where(u => u.UserName == name).Where(u => u.UserId > minId).ExecuteFetchAllAsync();

// instance field alongside a local
await db.Users().Where(u => u.UserId > _minId && u.UserName == name).ExecuteFetchAllAsync();
```

One shape that previously compiled and then failed at runtime now fails at build time — see Breaking
Changes.

## Plan items implemented as specified

- Unwrap `AnonymousFunction` when resolving the enclosing method.
- Resolve parameters to their owner's body block, and `foreach`/`for`/`using`/`switch`-section
  declarations to their own scope.
- Emit a `<>4__this` hop for instance fields captured alongside locals.
- Disqualify multi-scope clauses with a QRY032 naming the shape and the workaround.
- Revert the `ConcurrencyTests` named-worker workaround to inline lambdas.
- Ground-truth tables and rules recorded in `src/Quarry.Generator/llm.md`.

## Deviations from plan implemented

- **The original plan's steps 2–6 were abandoned.** They assumed chained display-class access was
  buildable. Step 1 was written as a gate specifically to test that, and it failed — four `[UnsafeAccessor]`
  signature shapes were tried and all were rejected, a result that matches the upstream issue above. The
  plan was rewritten against measured behaviour rather than continuing on the assumption.
- **"Pick the innermost captured scope as the Target" was dropped** in favour of just counting distinct
  scopes. Since every genuinely multi-scope clause is now rejected, surviving clauses capture from exactly
  one scope, where the existing first-match lookup is already correct. Smaller and lower-risk.

## Gaps in original plan implemented

- **`catch`-clause variables.** Not in the original plan; found in review. They own a display class, so a
  catch variable plus a method local looked single-scope and the guard did **not** fire — the safety
  invariant was defeated for that shape and it failed at runtime.
- **Accessibility/genericity fallback for the `<>4__this` hop.** The hop needs the containing type as a
  real type name, which is impossible for a generic type (CS0305) or one not visible to generated code
  (CS0122). Both were reproduced; such chains are now disqualified instead.
- **Per-clause hop naming.** The hop accessor was named after the containing type, so two clauses on one
  chain each mixing a field with a local emitted it twice — CS0111 in generated code. Reproduced, then
  named per clause.
- **User-facing documentation.** `docs/articles/analyzer-rules.md` now documents both capture limits under
  QRY032 with before/after examples.

## Migration Steps

Only for code hitting the new build error. Split a clause that captures across scopes:

```csharp
- .Where(u => u.UserName == name && u.UserId > minId)   // name and minId in different scopes
+ .Where(u => u.UserName == name).Where(u => u.UserId > minId)
```

Or copy the outer value into a local in the inner scope. For a field on a generic or inaccessible type,
copy it into a local before the chain.

## Performance Considerations

No runtime cost change on the generated hot path — the same `[UnsafeAccessor]` extraction, plus one extra
field read for the `<>4__this` hop where it applies. Carriers that differ only in hop path no longer merge
(`CapturedVariableExtractor` equality feeds `CarrierStructuralKey`), so generated output grows slightly
for the field-plus-local shape; deliberate, since merging them would reintroduce the #268 failure mode.

## Security Considerations

None. Compile-time source generator with no new inputs or external surface. The `Unsafe.As` approach was
rejected partly on memory-safety grounds, so no undefined behaviour is introduced.

## Breaking Changes

- Consumer-facing: a clause capturing locals from **two or more closure scopes** is now a build error
  (QRY032) instead of compiling and throwing `MissingFieldException`/`InvalidCastException` at execution.
  Likewise a clause capturing an instance field alongside a local when the containing type is generic or
  inaccessible. Both previously produced silently broken builds, so this converts a runtime failure into a
  build-time one — but code that "compiled" before may now stop compiling. Documented in
  `docs/articles/analyzer-rules.md`.
- Internal: `RawCallSite` gains `CapturedScopeCount` and `ThisIndirectionUnavailable` (both excluded from
  `Equals`/`GetHashCode`, consistent with the other enricher-set members); `CapturedVariableExtractor`
  gains `ThisIndirectionDisplayClass` and `ThisHopMethodName`, both included in equality.

## Follow-ups filed

- **#338** — chain rooted at a member access (`t.Lite.Users()`) is emitted against the wrong context
  (CS9144/CS0029). Pre-existing; reproduced with this branch's generator stashed.
- **#339** — supporting multi-scope captures, blocked on dotnet/runtime#119664.
- **#341** — instance field in an `Update().Set(...)` computed expression emits an invalid accessor.
  Pre-existing; reproduced with this branch's generator stashed.
- **#342** — hardening: switch-expression arm variables, deriving the guard from the extraction plan, and
  an explicit "unanalysed" sentinel.
