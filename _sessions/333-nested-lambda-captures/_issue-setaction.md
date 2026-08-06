## Description

An instance field referenced inside an `Update().Set(...)` **computed expression**, alongside a captured
local, produces an interceptor that fails at runtime. Two defects stack on the same path:

1. The `[UnsafeAccessorType("…")]` string is built with `SymbolDisplayFormat.FullyQualifiedFormat`, which
   emits a `global::` prefix. That attribute takes a **metadata** type name, where `global::` is invalid,
   so the accessor throws at first use:

   ```
   System.TypeLoadException : Could not resolve type 'global::MyApp.MyClass' in assembly 'MyApp'
   ```

2. Even with the name corrected, the field would still be read off the wrong object. When a lambda
   captures an instance field *and* a local, the compiler interposes a display class and the field lives
   on the instance behind its `<>4__this` back-reference. The parameter path handles this (added in #333);
   the `SetActionAllCapturedIdentifiers` path does not, and would throw `InvalidCastException`.

Reproduction:

```csharp
private readonly string _prefix = "Pre";

var suffix = "X";
await db.Users()
    .Update()
    .Set(u => u.UserName = _prefix + suffix)   // field + local in a computed Set expression
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();
```

## Location

- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — the `containingClass` value is produced with
  `SymbolDisplayFormat.FullyQualifiedFormat` (search `field.ContainingType?.ToDisplayString`).
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — the second extractor pass over
  `SetActionAllCapturedIdentifiers`, which sets `effectiveCaptureKind = FieldCapture` and emits no
  `<>4__this` hop.

## Diagnostics

`System.TypeLoadException : Could not resolve type 'global::…'`, thrown from the generated
`__ExtractVar__prefix_0` accessor. Inspect the emitted file under
`obj/GeneratedFiles/Quarry.Generator/Quarry.Generators.QuarryGenerator/` — the bad attribute is visible
directly:

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_prefix")]
internal extern static ref string __ExtractVar__prefix_0(
    [UnsafeAccessorType("global::MyApp.MyClass")] object target);   // ← global:: is invalid here
```

Note the sibling accessor for the captured local on the same carrier is correct, which makes the
difference easy to see.

## What Has Been Tried

- **Confirmed pre-existing**, not a regression from #333: reproduced with that branch's generator changes
  stashed (`git stash push src/Quarry.Generator/`), producing the identical `TypeLoadException`.
- **Confirmed it is the `Set` computed-expression path specifically.** The equivalent capture in a
  `Where` clause works, because that goes through the parameter path which #333 taught to emit a
  `<>4__this` hop and which builds its type string without `global::`.

## Gathered Information

- `[UnsafeAccessorType]` takes a metadata type name: nested types use `+`, and there is no `global::`
  qualifier. The working `Where` path derives its string from
  `SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces` instead.
- The `<>4__this` hop is expressible with a plain `[UnsafeAccessor]` because that field's type is the
  user's own class — no `[return: UnsafeAccessorType]` is needed, so
  [dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664) does not block it. See the
  "Display Class Prediction" section of `src/Quarry.Generator/llm.md`.
- Accessibility caveat from #333: the hop must be skipped (and the chain disqualified) when the
  containing type is generic or not visible to generated code, or the emitted accessor is a CS0305 /
  CS0122 build break. `DisplayClassEnricher.IsNameableFromGeneratedCode` already implements that test.

## Suggested Approach

1. Add a failing execution test for the shape above (it must execute — a wrong accessor still compiles).
2. Build the `SetActionAllCapturedIdentifiers` type string the same way the parameter path does, without
   the `global::` prefix.
3. Reuse the `<>4__this` indirection for instance fields on that path, including the
   `IsNameableFromGeneratedCode` fallback to a QRY032 disqualification.
4. Ideally collapse the two extractor-building passes in `CarrierAnalyzer` so this class of divergence
   cannot recur — they now differ in three ways for no intended reason.
