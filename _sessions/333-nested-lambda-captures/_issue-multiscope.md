## Description

A query clause that captures locals from **two or more distinct closure scopes** cannot be emitted, and
is currently rejected at build time with `QRY032`. This issue tracks lifting that restriction if and when
the upstream runtime gap it depends on is closed.

The guard was added in #333. Before it, these shapes were emitted anyway and threw at first execution —
`MissingFieldException` when the extractor named a field that was not on the chosen display class, or
`InvalidCastException` when the delegate's `Target` was a different display class than the accessor
expected. The guard converts that into a build error naming the shape and the workaround.

Rejected shape:

```csharp
var minId = 0;
foreach (var name in names)
{
    await db.Users()
        .Where(u => u.UserName == name && u.UserId > minId)   // QRY032
        .ExecuteFetchAllAsync();
}
```

Supported workaround — split so each clause captures from one scope:

```csharp
await db.Users()
    .Where(u => u.UserName == name)
    .Where(u => u.UserId > minId)
    .ExecuteFetchAllAsync();
```

## Location

`src/Quarry.Generator/Parsing/ChainAnalyzer.cs` (`CheckDisqualifiers`, the `CapturedScopeCount > 1`
branch) and `src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs` (`CountCaptureScopes`).

## Diagnostics

`QRY032` at the terminal, with the reason `clause at {line}:{col} captures variables from N different
closure scopes …`. Covered by `Generation/LambdaCaptureScopeTests` (`MultiScope_*` cases assert the
diagnostic fires; the remaining cases assert it does not).

## What Has Been Tried

Two mechanisms for reading the outer scope were investigated and both are dead ends:

1. **Chained `[UnsafeAccessor]` through the compiler's `CS$<>8__locals` link field.** Not expressible.
   Four signature shapes were tried against a hand-built three-scope closure:

   | Variant | Result |
   |---|---|
   | `ref object` + `[return: UnsafeAccessorType]` | `NotSupportedException: Invalid usage of UnsafeAccessorTypeAttribute` |
   | `ref object`, no return attribute | `MissingFieldException` — field lookup matches on name **and exact type** |
   | `object` (non-ref) + return attribute | `BadImageFormatException` |
   | `object` (non-ref), no attribute | `BadImageFormatException` |

   The restriction is blanket, not a type-name problem: it reproduces on ordinary private nested types
   with both simple and assembly-qualified names, while the same `[return: UnsafeAccessorType]` works
   fine on `UnsafeAccessorKind.StaticMethod`. This is
   [dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664) — open, milestone `Future`,
   fields deliberately excluded because *"having a `ref object` return type for the accessor isn't memory
   safe, it would need `TypedReference` support for that."*

2. **`Unsafe.As` shadow-type overlay.** Works mechanically — a shadow class or `ValueTuple` with a
   matching field shape reads the link correctly — but rejected as unsafe:
   - Undefined behaviour per
     [dotnet/runtime discussion #111049](https://github.com/dotnet/runtime/discussions/111049)
     ("*reinterpretation-like casts are UB and may lead to hard-to-reproduce crashes/gc holes*").
   - Display classes hold reference fields, so they are non-blittable and get `Auto` layout, which has
     **no guaranteed field offsets** and cannot be pinned with `Sequential`.
   - The failure mode is silent: with two same-typed fields, a shadow declared in the wrong order returns
     the values **swapped** — binding `@p0` to the wrong variable and returning wrong rows with no error.
   - Validating the hop with a type-checked accessor afterwards does not rescue it; the check runs after
     the read, so a misaligned read has already handed the GC a bad reference.

## Gathered Information

- The delegate's `Target` is the display class of the **innermost** scope the lambda captures from; outer
  scopes are reached through `CS$<>8__locals{C}` link fields. The link's name uses the child's own closure
  ordinal, but its **type is the lexically enclosing scope, not ordinal `C-1`** — verified with sibling
  `if` blocks, where `_2` links straight back to `_0`. Reaching a grandparent takes two hops; the compiler
  never emits a skip-link.
- A link field exists **only** when some closure in that scope's subtree actually reaches outward, so hops
  cannot be assumed present.
- Ground-truth tables for scope→display-class mapping are in the "Display Class Prediction" section of
  `src/Quarry.Generator/llm.md`.

## Suggested Approach

Blocked on [dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664). If that ships,
`CS$<>8__locals` becomes readable and the guard can be replaced with real chained extraction:

1. Record the scope **tree** (each capture scope's lexically enclosing capture scope), not just the count.
2. Resolve the site's display class to the innermost captured scope, and give each captured variable a hop
   path out to its own scope.
3. Emit one link accessor per hop, then read the variable by name from the final display class.
4. Carry the hop path in `CapturedVariableExtractor`'s equality — `CarrierStructuralKey` dedupes carriers
   on extractor equality, and two carriers differing only in hop path must not merge (the #268 failure
   mode, pinned by `CarrierStructuralKeyTests`).

Until then the guard is the correct behaviour: a build error at the call site beats a
`MissingFieldException` in production.
