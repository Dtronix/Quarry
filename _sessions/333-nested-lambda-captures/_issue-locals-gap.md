## Description

Quarry reads a clause's captured values at runtime through `[UnsafeAccessor]` externs. For **captured
locals and parameters** — and only for those — the accessor must name a compiler-generated display
class, which the generator *predicts*:

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "minId")]
internal extern static ref int __ExtractVar_minId_0(
    [UnsafeAccessorType("MyNs.Repo+<>c__DisplayClass3_1")] object target);   // <- predicted
```

The prediction reconstructs two ordinals: `methodOrdinal` (index in `containingType.GetMembers()`) and
`closureOrdinal` (pre-order index over capture scopes). Neither is a documented contract, and both
move for reasons the generator cannot see. A wrong prediction is **not a build error** —
`[UnsafeAccessorType]` resolves its string at runtime, so it surfaces as `TypeLoadException`,
`MissingFieldException`, or `InvalidCastException` on the first execution of the chain.

**This issue is specifically about closing the local/parameter capture gap.** Everything else is
already guess-free (see below), so this is the whole remaining exposure.

### The gap is precisely bounded

Verified against emitted IL — only one row guesses:

| What the clause captures | `[UnsafeAccessorType]` target emitted | Predicted? |
|---|---|---|
| `static` field / property | the containing type (`MyNs.Repo`) | no |
| instance field, captured alone | the containing type (delegate `Target` **is** the instance) | no |
| instance field + a local | containing type, reached via `<>4__this` on the display class | partially — the display class is named |
| **local or parameter** | `MyNs.Repo+<>c__DisplayClass{M}_{C}` | **yes** |

So: *the entire guessing surface is "a clause captures a local or parameter."*

## Location

- `src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs` — `AnalyzeMethodClosures`,
  `FindDeclaringScope`, `LookupClosureOrdinal`, `ComputeMethodOrdinal`.
- `src/Quarry.Generator/Parsing/DisplayClassEnricher.cs` — builds the
  `{Type}+<>c__DisplayClass{methodOrdinal}_` prefix.
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — emits the accessors.
- Public API surface that would change: `IQueryBuilder<T>.Where(Func<T,bool>)` and the other
  clause-taking methods in `src/Quarry/`.

## Diagnostics

`TypeLoadException: Could not resolve type '…+<>c__DisplayClass5_3'` (ordinal wrong),
`MissingFieldException` (field name wrong), or `InvalidCastException` (right shape, wrong display
class) — all at first execution, never at build time. `Quarry.Tests.dll` currently ships **180**
distinct predicted type-name strings, **174** of which embed a guessed ordinal.

## What Has Been Tried

All of the following were investigated during #333; each was prototyped or empirically tested unless
marked otherwise.

### Ruled out — cannot work

| Approach | Result |
|---|---|
| **Chained `[UnsafeAccessor]`** through the compiler's `CS$<>8__locals` link field to reach an outer scope | Not expressible. Four signature shapes tried; all fail. A field accessor must return byref, and a byref return cannot name an inaccessible type — [dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664), open/`Future`, fields deliberately excluded because *"having a `ref object` return type for the accessor isn't memory safe"*. Reproduced on ordinary private nested types, so it is not a naming problem. |
| **`Unsafe.As` shadow-type overlay** onto the display class | Works mechanically, rejected as unsafe. Undefined behaviour per [dotnet/runtime#111049](https://github.com/dotnet/runtime/discussions/111049) (*"reinterpretation-like casts are UB and may lead to hard-to-reproduce crashes/gc holes"*); display classes hold reference fields so they are non-blittable and get `Auto` layout with no guaranteed offsets. **Failure mode is silent**: with two same-typed fields, a mismatched overlay returns the values *swapped*. A `ValueTuple` overlay is the same positional mechanism with the same hazard. |
| **Recording / proxy entity** — invoke the delegate with an instrumented entity and observe the comparisons | Prototyped. Recovers values, and `&&`/`||` work via `op_true`/`op_false`, but `?:`, block bodies and plain-`bool` short-circuits silently produce **wrong parameters** — worse than a crash. `SqlExprParser` already accepts ternaries. |
| **Call-site value injection** | Structurally impossible: an interceptor replaces a *call expression*; it cannot introduce locals into the enclosing method. |
| **Post-compile IL rewriting** | Rejected — adds a build step and a rewriting dependency for what is otherwise a pure source generator. |
| **`Expression<Func<>>` instead of `Func<>`** | This is what EF Core does. Costs a tree allocation per call and still ends in reflection at the leaf; contradicts Quarry's AOT/allocation goals. |

### Ruled out — no Roslyn API exists

A generator cannot learn the name, because display classes are synthesized during `Emit`, after
generators finish. Verified three independent ways:

- The pre-`Emit` symbol table contains the user's type only; the emitted PE for the same compilation
  contains six display classes. `GetSymbolsWithName(all)` and `GetTypeByMetadataName(…DisplayClass…)`
  both find nothing.
- Every closure/frame type in `Microsoft.CodeAnalysis.CSharp` is `NotPublic`
  (`SynthesizedClosureEnvironment`, `SynthesizedClosureMethod`, `GeneratedNames`). Zero hits across
  every `PublicAPI.{Shipped,Unshipped}.txt`.
- For a captured local, **every shipped `SymbolDisplayFormat`** — including `FullyQualifiedFormat` —
  returns the bare name `minId`; a maximal custom format with all qualification flags adds only the
  type (`int minId`). `GetDocumentationCommentId()` returns `null` (locals are not metadata
  entities) and `ContainingSymbol` is the user's own method.

Also checked and negative: `ControlFlowGraph` / `IFlowCaptureOperation` (a lambda capturing three
variables produces **zero** flow captures — they are spilling temporaries); `SymbolKey` (internal,
encodes source spans, contains no `DisplayClass` substring, and lambda keys stop resolving when an
unrelated statement is inserted); the Portable PDB local-variable table (**captured locals are absent
from it** — capture turns them into display-class fields); `EmitBaseline.SynthesizedMembers`
(internal, and only ever populated from a *prior* emit).

**Upstream status — corrected.** Two issues are commonly cited as evidence this was refused. Both are
the *opposite* direction, and one is still open:

| Issue | Title | State | Direction |
|---|---|---|---|
| [roslyn#11565](https://github.com/dotnet/roslyn/issues/11565) | Provide a public API to **parse** generated names | closed / not_planned | name → parsed parts |
| [roslyn#55651](https://github.com/dotnet/roslyn/issues/55651) | Support retrieving original type name **from mangled** type name | **open** | mangled → original |

The direction Quarry needs (symbol → emitted name) appears exactly once, in #55651's "Alternative
Designs", and drew no maintainer reply — so it has never actually been proposed, let alone refused.
The relevant maintainer position is on
[roslyn#50978](https://github.com/dotnet/roslyn/issues/50978): any such API must *"not leak
implementation details out, **so that we allow the compiler to change how code is emitted without
breaking the API consumers**."*

### Confirmed — the numbering really does move

- **`<Optimize>`.** `ClosureConversion.Analysis` calls `MergeEnvironments()` only when
  `OptimizationLevel == Release`; a merged-away environment never consumes an ordinal, so every later
  ordinal shifts down. This is #344, and it is why a fixture can pass under `dotnet test` (Debug) and
  fail in CI (`-c Release`).
- **Ongoing compiler changes.** [roslyn#82430](https://github.com/dotnet/roslyn/issues/82430)
  (Feb–Mar 2026) defers display-class allocation for async local functions: `IntroduceFrame` skips
  frame creation for eligible environments, again gated on optimized builds. Fewer frames created
  means later ordinals renumber.
- **Unrelated edits.** Adding one `private string _extra;` renumbers every display class in the type
  (the #310 failure mode), because `methodOrdinal` is the `GetMembers()` index.

### No precedent to copy

No .NET ORM translates a plain `Func<T,bool>` to SQL. Dapper.AOT (also interceptor-based) uses
explicit parameters. ASP.NET Core's request-delegate generator just invokes the delegate. EF Core
precompiled queries read captured values from a runtime `Expression` tree and identify display classes
*structurally* (`NestedPrivate` + `CompilerGenerated`) rather than by name. The `<Prop>k__BackingField`
guessing seen in protobuf-net/TUnit/EF Core is a pure function of a name you already have — a
different risk class from reconstructing two ordinals.

## Gathered Information

Full write-ups on the #333 branch, both with reproducible experiments:
`_sessions/333-nested-lambda-captures/_research-roslyn-closures.md` and
`_research-symbol-to-name.md`. Scope→display-class ground truth is tabulated in the "Display Class
Prediction" section of `src/Quarry.Generator/llm.md`.

Related: **#310** (prediction robustness — cross-partial ordinal shifts, generic containing types),
**#339** (multi-scope captures, blocked on runtime#119664), **#344** (the `<Optimize>` break),
**#342** (switch-expression arms, guard input, unanalysed sentinel).

## Suggested Approach

Two tracks. The first protects code that already exists; the second removes the guessing for new code.
They are independent and can land in either order.

### Track 1 — detect a wrong guess at build time (protects existing code)

Ship a **post-compile verifier**: an MSBuild target after `CoreCompile` that reads the emitted assembly
with in-box `System.Reflection.Metadata` and checks every `[UnsafeAccessorType]` string against the
real typedefs and every `Name =` against the real fields.

Prototyped during #333: **667 accessors / 168 distinct display classes on the real `Quarry.Tests.dll`
in 0.72 s, zero false positives**, and a negative-test assembly with one wrong ordinal and one wrong
field name caught both. It does not fix a bad prediction — it converts the entire *class* of failure
(this issue, #310, #344, any future compiler change) from a runtime exception into a build error
naming the offending accessor and its source location. Ship it in the generator's `build/*.targets` so
**consumers** get it too; they are the ones compiling with their own SDK and configuration.

### Track 2 — remove the closure so there is nothing to name

A non-capturing lambda emits **no display class at all** (verified). So an overload that takes the
values as an argument sidesteps prediction entirely. The design question is how to do that without
reintroducing the positional-parameter mix-up that Quarry's lambda API exists to avoid.

**Rejected — positional parameters:**

```csharp
.Where((u, p1, p2) => u.UserId > p1 && u.UserId < p2, maxId, minId)   // swapped, compiles silently
```

Verified: swapping two same-typed arguments produces **no diagnostic**. This is precisely the Dapper
hazard and is not acceptable.

**Preferred — named `ValueTuple` argument:**

```csharp
.Where(u => u.UserId > minId)                                        // today: predicted closure name
.Where((u, p) => u.UserId > p.MinId, (MinId: minId))                 // proposed: nothing predicted
```

Verified properties:
- **Zero display classes emitted** — the lambda captures nothing.
- The argument type is `System.ValueTuple<…>`, a real framework type. Nothing to predict, so
  `<Optimize>`, environment merging, `GetMembers()` ordering and future compiler changes are all
  irrelevant *by construction*.
- **Mix-ups are compile errors**, not silent: `p.Limit` on `(MinId, MaxId)` gives
  `error CS1061: '(int MinId, int MaxId)' does not contain a definition for 'Limit'`. There is one
  argument, so there is no ordering to get wrong.

Work required, and the open questions:
1. New overloads on `Where` / `Set` / `Having` / the join variants, plus a single-value form
   (`ValueTuple<T>` with one element has ergonomic quirks worth checking).
2. **Generator work, not yet measured.** The translator classifies identifiers as columns or captured
   values; it must learn to treat a *lambda parameter member access* (`p.MinId`) as a parameter slot.
   Roslyn exposes `INamedTypeSymbol.TupleElements` with names, so mapping `p.MinId` → `Item1` should be
   direct — but this has **not** been prototyped, and it is the main cost unknown.
3. Ergonomics regress: `(u, p) => u.UserId > p.MinId` reads worse than `u => u.UserId > minId`. This
   is opt-in, not a replacement.
4. An **analyzer + code fix** to migrate a capturing clause to the tuple form would make adoption
   mechanical — and is the only way to move user code, since generators cannot rewrite it.

### Track 3 — ask upstream (cheap, long shot)

The symbol → emitted-name direction was never actually proposed. File a focused Roslyn issue asking
either for that mapping or for a documented stability contract on closure naming, citing this use case.
Given the #50978 position it will probably be declined, but the cost is one issue and the current
absence is partly an accident of nobody asking.

### Explicitly not proposed

Telling users to promote locals to fields to get a guess-free path. `private int _minId` instead of
`var minId` is bad code and breaks down for anything loop- or request-scoped.
