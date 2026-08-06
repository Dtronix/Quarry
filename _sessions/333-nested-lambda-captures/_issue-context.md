## Description

A chain whose root is reached through a **member access** — `t.Lite.Users()…` rather than a plain
identifier — is emitted into an interceptor file bound to the **wrong context**. The generated file is
named for one context but the interceptor's receiver is another:

```csharp
// CteDb.Interceptors.….g.cs
file sealed class Chain_0 : IEntityAccessor<User>, …
{
    internal CteDb? Ctx;                                    // ← CteDb
}

[InterceptsLocation(1, "…")]
public static IEntityAccessor<User> Users_e9b6f26f(
    this TestDbContext @this)                               // ← TestDbContext
{
    return new Chain_0 { Ctx = @this };                     // ← CS0029
}
```

Build fails with:

- `CS9144: Cannot intercept method 'TestDbContext.Users()' with interceptor
  'CteDbInterceptors_….Users_…(TestDbContext)' because the signatures do not match` (once per clause in
  the chain), and
- `CS0029: Cannot implicitly convert type 'Quarry.Tests.Samples.TestDbContext' to
  'Quarry.Tests.Samples.Cte.CteDb'`.

Like #333 this is a build break inside generated code, with no Quarry diagnostic pointing at the call
site.

## Location

Context resolution for chain roots during discovery — `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`
(`ContextClassName` / `ContextNamespace` on the root `RawCallSite`), and the per-file/per-context
grouping that decides which `{Context}.Interceptors.*.g.cs` a chain lands in.

## Diagnostics

Reproduced in `Quarry.Tests` with a chain written as:

```csharp
await using var t = await QueryTestHarness.CreateAsync();

var updated = await t.Lite.Users()          // member-access root
    .Where(u => u.UserId == 1)
    .Select(u => u.UserName)
    .ExecuteFetchFirstAsync();
```

Writing the identical chain against a plain identifier root does **not** reproduce:

```csharp
var (Lite, _, _, _) = t;
var updated = await Lite.Users()            // identifier root — fine
    .Where(u => u.UserId == 1)
    .Select(u => u.UserName)
    .ExecuteFetchFirstAsync();
```

This is why no existing test trips it: the whole suite deconstructs the harness first
(`var (Lite, Pg, My, Ss) = t;`), which is the documented pattern in `llm-testing.md`.

`EmitCompilerGeneratedFiles` is enabled on `Quarry.Tests.csproj`, so the offending file is inspectable
under `obj/GeneratedFiles/Quarry.Generator/Quarry.Generators.QuarryGenerator/`.

## What Has Been Tried

- **Confirmed pre-existing and unrelated to #333.** Reproduced with the #333 generator fix stashed
  (`git stash push src/Quarry.Generator/`), so it is not a regression from that work.
- **Confirmed the receiver shape is what matters**, not the dialect or the entity: the same chain with an
  identifier root compiles and runs correctly.
- **Not yet isolated** to whether the wrong context comes from `ContextClassName` resolution on the root
  site or from the per-context file grouping downstream. The generated file being *named* `CteDb` while
  the receiver parameter is correctly typed `TestDbContext` suggests the entity/context lookup resolved
  `User` against the wrong `ContextInfo` — `CteDb` also declares a `User` entity — rather than the
  receiver type being misread.

## Gathered Information

- The entity registry resolves entities per context (`EntityRegistry.Resolve(entityTypeName,
  contextClassName)`). Several test contexts declare a `User` entity (`TestDbContext`, `Pg.PgDb`,
  `My.MyDb`, `Ss.SsDb`, `Cte.CteDb`), so a null or unresolved `ContextClassName` on the root site would
  let the lookup pick an arbitrary context that happens to declare a matching entity name.
- `VariableTracer.WalkFluentChainRoot` reduces a fluent chain to its root expression; for `t.Lite.Users()`
  that root is a `MemberAccessExpressionSyntax`, not an `IdentifierNameSyntax`. Several call sites branch
  on `receiver is not IdentifierNameSyntax` and bail — for example `DetectVariableDisqualifiers` returns
  early in that case — so a member-access root is a known shape that parts of discovery decline to handle.

## Suggested Approach

1. Add a failing test in `src/Quarry.Tests/Generation/` that pins the shape, asserting on the emitted
   interceptor (per the `llm-testing.md` note that receiver-arity mismatches raise nothing in an isolated
   `CSharpCompilation`, so assert on the generated text, not on a compiler diagnostic).
2. Resolve the context from the receiver's **semantic type** rather than requiring an identifier root, so
   `t.Lite` resolves to `TestDbContext` the same way a bare `Lite` does.
3. If the context genuinely cannot be resolved for some root shape, fail loudly with a QRY diagnostic at
   the call site instead of emitting a chain into an arbitrary context's file — a build error inside
   generated code is the worst way for a user to meet this.
