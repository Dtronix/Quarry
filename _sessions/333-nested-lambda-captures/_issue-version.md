## Description

> **Re-diagnosed.** This issue was originally filed as "prediction is not stable across compiler
> versions", inferred from a local pass on SDK 10.0.110 and a CI failure on 10.0.302. That was wrong.
> The variable was never the SDK — it is the `<Optimize>` MSBuild property. Corrected below.

The generator predicts compiler-generated display-class names (`<>c__DisplayClass{M}_{C}`) to emit
`[UnsafeAccessor]` extractors. The **closure ordinal** `{C}` depends on whether the compiler is
optimizing, so the same source yields different names in a Debug and a Release build. The prediction
does not model this, so one of the two builds gets a wrong name and fails at runtime:

```
System.TypeLoadException : Could not resolve type
  '…+<>c__DisplayClass5_3' in assembly 'Quarry.Tests'
```

## Mechanism

`src/Compilers/CSharp/Portable/Lowering/ClosureConversion/ClosureConversion.Analysis.cs`:

```csharp
analysis.MakeAndAssignEnvironments();
analysis.ComputeLambdaScopesAndFrameCaptures();
if (compilationState.Compilation.Options.OptimizationLevel == OptimizationLevel.Release)
{
    // This can affect when a variable is in scope whilst debugging, so only do this in release mode.
    analysis.MergeEnvironments();
}
```

`MergeEnvironments()` folds a child environment into its parent and sets
`scope.DeclaredEnvironment = null`. `SynthesizeClosureEnvironments` only creates a frame for a scope
that still has an environment, and the ordinal comes from `closureDebugInfo.Count`, which is
incremented **only per surviving environment**. So a merged-away environment never consumes an
ordinal and **every later closure ordinal shifts down by one**.

Introduced by [roslyn#32092](https://github.com/dotnet/roslyn/pull/32092) "Optimise DisplayClass
Allocations" (2019), fixing [roslyn#29965](https://github.com/dotnet/roslyn/issues/29965). No
breaking-change note was filed, because the emitted closure layout is explicitly not a contract.

**The knob is `<Optimize>`, not the configuration name.** `OptimizationLevel` maps to `/optimize+`.
A configuration called "Debug" with `<Optimize>true</Optimize>` merges; one called "Release" with
`<Optimize>false</Optimize>` does not.

## Reproduction

Same SDK, same `Microsoft.CodeAnalysis.CSharp` build, only `OptimizationLevel` differs:

```csharp
int a = 1;
if (a > 0) { var b = 2; Use(u => u > a && u > b); }
foreach (var c in new[]{1,2}) { Use(u => u > c); }
```

```
Debug:    _0 [a]       _1 [b, CS$<>8__locals1]   _2 [c]
Release:  _0 [a, b]                              _1 [c]
```

Driving Quarry's own `DisplayClassNameResolver` over the identical trees predicts `_2` for
`u => u > c` in both — correct in Debug, wrong in Release.

Rebuilding the `ConcurrencyTests.ParallelHarnesses_MixedReadWrite_DoNotShareParameterState` shape
from commit `9d3aaf2` reproduces the original report exactly: predicts `_3`; Release emits only
`_0.._2`, with the captured `name` on `_2`.

`dotnet test` defaults to Debug; `.github/workflows/ci.yml` runs `-c Release`. That is the entire
"passes locally, fails in CI" split — **no SDK difference is required**, and Roslyn 4.11 / 4.14 / 5.0
agreed on all 25 shapes tested while the `<Optimize>` axis changed 7 of them. A `global.json` would
not have helped.

## Why the existing guard does not catch it

The mispredicted clause is an ordinary **single-scope** capture (`captureScopes = 1`), so the
multi-scope disqualifier added in #333 correctly stays silent. It is an *unrelated* lambda elsewhere
in the same method that causes the merge and the renumbering. The generator cannot guard on a lambda
it never inspects, so no purely local, syntax-based rule can model this.

## Scale

`Quarry.Tests.dll` today contains **531** display classes in Debug and **527** in Release. The
current fixtures happen to sit upstream of every merge, which is why the suite passes both ways —
that is luck, not design.

## Suggested Approach

1. **Post-compile verification (recommended).** After `CoreCompile`, read the emitted assembly with
   `System.Reflection.Metadata` and check every `[UnsafeAccessorType]` string against the real
   typedefs and every `Name =` against the real fields. A prototype over the real `Quarry.Tests.dll`
   checked 667 accessors / 168 distinct display classes in **0.72 s**, and a negative test caught both
   a wrong ordinal and a wrong field name. This does not fix a bad prediction — it converts the whole
   *class* of failure (this issue, #310's ordinal shifts, any future compiler change) from a runtime
   `TypeLoadException` into a build error. Shipping it in the generator's `build/*.targets` protects
   consumers too, not just this repo.
2. **Add an `<Optimize>` axis to CI.** Every affected test compiles either way; only *execution*
   distinguishes them, so the suite must run both.
3. **Fail legibly at runtime** if a prediction is ever wrong in a shipped build — name the predicted
   type and point here, rather than surfacing a bare `TypeLoadException`.
4. **Document the escape hatch.** A non-capturing lambda emits no display class at all, so an
   explicit-parameter overload (`Where((u, p) => u.UserId > p, minId)`) sidesteps prediction entirely.

## No Roslyn API can remove the guessing

Display classes are synthesized during `Emit`, long after generators run, so a generator cannot learn
the name. Verified: the pre-emit symbol table holds the user's type only, while the emitted PE holds
the display classes; every closure/frame type in `Microsoft.CodeAnalysis.CSharp` is `NotPublic`; and
for a captured local, **every** shipped `SymbolDisplayFormat` — `FullyQualifiedFormat` included —
returns just `minId`, with `GetDocumentationCommentId()` returning `null` and `ContainingSymbol` being
the user's own method.

> **Correction.** An earlier revision of this issue said a public API had been "requested and refused
> twice", citing roslyn#11565 and #55651. That was wrong on both counts. Both issues are the
> **opposite direction** — mangled/generated name → original name — and #55651 is still open:
>
> | Issue | Title | State | Direction |
> |---|---|---|---|
> | [#11565](https://github.com/dotnet/roslyn/issues/11565) | Provide a public API to **parse** generated names | closed / not_planned | name → parsed parts |
> | [#55651](https://github.com/dotnet/roslyn/issues/55651) | Support retrieving original type name **from mangled** type name | **open** | mangled → original |
>
> The direction Quarry needs (symbol → emitted name) appears once, in #55651's "Alternative Designs",
> and drew no maintainer reply. So there is no refusal on the merits — the API is absent because it
> was never really proposed. `SymbolKey`, the closest symbol→identity mechanism, is internal, encodes
> source spans rather than emitted names, and its lambda keys stop resolving when an unrelated
> statement is inserted.

The relevant maintainer position is on
[roslyn#50978](https://github.com/dotnet/roslyn/issues/50978) ("Emitting compiler details"), where any
such API was required to "not leak implementation details out, **so that we allow the compiler to
change how code is emitted without breaking the API consumers**."

**They are still changing it.** [roslyn#82430](https://github.com/dotnet/roslyn/issues/82430)
(Feb–Mar 2026) modifies `ClosureConversion.Analysis.cs` to defer display-class allocation for async
local functions: it adds `IsDeferrableEnvironment`, and `IntroduceFrame` skips frame creation for
eligible environments — gated, again, on optimized builds. Fewer frames created means later closure
ordinals renumber. That is this issue's failure mode, from a change that shipped months ago, which is
the strongest argument that prediction must be *verified* rather than trusted.
