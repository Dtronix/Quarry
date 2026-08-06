# Workflow: 333-nested-lambda-captures
## Config
platform: github
base-branch: master
## State
phase: REVIEW
status: active
issue: #333
pr:
## Problem Statement
Issue #333 — "Chains inside doubly-nested lambdas emit interceptors that fail to compile (CS0103 on captured locals)".

A Quarry chain written inside a lambda that is itself nested in another lambda makes the generator emit
an interceptor that references the enclosing method's locals directly. Those locals live in a
compiler-generated display class the interceptor cannot see, so the generated file fails to compile with
`CS0103: The name 'name' does not exist in the current context` in `*.Interceptors.*.g.cs`. It is a build
break with no Quarry diagnostic.

Repro shape from the issue:

```csharp
var tasks = harnesses.Select((h, i) => Task.Run(async () =>
{
    var name = $"Worker{i}";
    await h.Lite.Users()
        .Update()
        .Set(u => u.UserName = name)
        .Where(u => u.UserId == 1)
        .ExecuteNonQueryAsync();
}));
```

Suspect area: `src/Quarry.Generator/Parsing/DisplayClassEnricher.cs` and
`src/Quarry.Generator/Parsing/DisplayClassNameResolver.cs`.

Issue's suggested approach, in order of preference:
1. Resolve the capture correctly — walk the nested-lambda chain so the interceptor reads the local through
   the display-class instance it actually lives on.
2. Fail loudly — disqualify the shape in `ChainAnalyzer.CheckDisqualifiers` to `RuntimeBuild` with a QRY
   diagnostic naming the shape and the "hoist into a named method" workaround.

Either way: add the minimal repro to `src/Quarry.Tests/Generation/`.

Existing workaround in the repo: `src/Quarry.Tests/Integration/ConcurrencyTests.cs` hoists each worker body
into a named `private static async Task<T> Run…WorkerAsync(...)` method; `llm-testing.md` "Common gotchas"
documents the limitation.

### Baseline test results
`dotnet test Quarry.sln` on b03e246, 2026-08-04 — **fully green, no pre-existing failures**:

| Assembly | Passed | Failed | Skipped |
|---|---|---|---|
| Quarry.Migration.Tests | 201 | 0 | 0 |
| Quarry.Analyzers.Tests | 146 | 0 | 0 |
| Quarry.Tests | 3501 | 0 | 0 |

Docker was available, so no container fixtures were `Assert.Ignore`d.

## Decisions

- **2026-08-04 — Fix direction: option 1 (resolve the capture correctly), not option 2 (disqualify).**
  The issue offered "fail loudly with a QRY diagnostic" as a fallback. Rejected: empirical work showed
  correct resolution is achievable and validated end-to-end (compiles *and* executes) across 13 shapes.

- **2026-08-04 — Scope includes a full fix for multi-scope captures, not just a guard.**
  Asked the user given the gap is pre-existing and lambda-independent. Options offered were
  (a) #333 only + file separately, (b) #333 + a QRY disqualifier guard, (c) #333 + full multi-scope fix.
  **User chose (c): fully fix multi-scope** via chained display-class access, rather than rejecting the
  shape. This is a deliberate scope extension beyond issue #333.

- **2026-08-04 — Revert the `ConcurrencyTests` workaround and correct the `llm-testing.md` gotcha.**
  User approved. Worker bodies go back to inline lambdas, which validates the fix end-to-end under real
  concurrency. Caveat accepted: those use `h.Lite` (member-access chain root), which may hit the separate
  pre-existing context-misattribution bug; where it does, the named method stays and the reason is stated
  explicitly rather than silently left in place.

- **2026-08-04 — REVISED after the step-1 gate failed and end-user shapes were measured.**
  The "fully fix multi-scope" decision is not implementable: chained display-class access is blocked by
  [runtime#119664](https://github.com/dotnet/runtime/issues/119664) (open, `Future` — `ref object` field
  returns are not memory safe) and the `Unsafe.As` overlay alternative is UB per
  [runtime#111049](https://github.com/dotnet/runtime/discussions/111049). Measuring ten realistic end-user
  shapes then showed the failures are **three** different bugs, not one:
  (a) mis-scoped `foreach`/`for` declarations — fixable, no hops, and it alone breaks the *separate
  clauses* shape where no lambda is multi-scope; (b) `<>4__this` indirection when a field is mixed with a
  local — fixable, verified, since `<>4__this`'s type is the user's own accessible class; (c) one lambda
  capturing locals from two closure scopes — genuinely blocked, gets the guard.
  **User approved re-planning on this basis.** The guard now covers only (c), and its message points at
  the workaround that (a)+(b) make work: split the predicate into separate `.Where(...)` clauses.

- **2026-08-04 — The two pre-existing bugs found while probing are out of scope and get their own issues:**
  member-access chain root → wrong context (CS9144/CS0029), filed as **#338**. Multi-scope captures could
  not be fixed after all (upstream-blocked), so the guard shipped instead and the follow-up is tracked as
  **#339**.

## Working Notes

### Root cause (confirmed empirically, 2026-08-04)

Two independent defects in display-class resolution combine to produce the CS0103:

1. **The site is never enriched.** `DisplayClassEnricher.EnrichAll` resolves the enclosing method via
   `semanticModel.GetEnclosingSymbol(lambda.SpanStart)`, then unwraps **only** `MethodKind.LocalFunction`.
   When the chain is inside a lambda, that returns the lambda's `MethodKind.AnonymousFunction` symbol,
   which is not unwrapped. `ComputeMethodOrdinal` then searches for it in `containingType.GetMembers()`,
   finds nothing, returns `-1`, and the site is skipped. It keeps `CaptureKind.None` /
   `DisplayClassName == null`, so `CarrierAnalyzer` builds no extraction plan
   (`CarrierAnalyzer.cs:348`), `CarrierEmitter` never emits the
   `var name = Chain_0.__ExtractVar_name_0(__target);` local, and the raw `ValueExpression` — the bare
   `name` — is emitted at `CarrierEmitter.cs:328`. That is the CS0103.

2. **The predicted closure ordinal is wrong for nested lambdas.** Fixing (1) alone makes the code
   compile but fail at runtime with `MissingFieldException: Field not found: '<>c__DisplayClass1_1.name'`.
   `DisplayClassNameResolver.FindDeclaringScope` walks a captured variable up to the nearest enclosing
   `BlockSyntax`. For a *lambda parameter* that walks straight past the lambda into the enclosing method
   body block, so the lambda's parameter scope collides with the method's own scope and every later
   closure ordinal shifts down by one.

### Ground truth for scope → display-class mapping

Dumped from the compiled test assembly via reflection (scratch `DisplayClassDumpTests`):

| Source shape | Emitted display classes |
|---|---|
| lambda param + its body-block local, both captured | `<>c__DisplayClass0_0 { p, bodyLocal }` — **ONE class** |
| method local / lambda param / inner-lambda local | `1_0 { methodLocal }`, `1_1 { p, CS$<>8__locals1 }`, `1_2 { innerLocal, CS$<>8__locals2 }` |
| nested `if`-block local inside a lambda body | `2_0 { blockLocal }` |
| local-function param + its body local | `3_0 { lfParam, lfLocal }` — **ONE class** |

Rule the compiler follows: a closure scope is a block; a lambda's / local function's / method's
**parameters live on the same display class as the top-level locals of its own body**. So a parameter
must resolve to its *owner's body block*, never to the block enclosing the owner. Expression-bodied
owners have no body block and key on the owner node itself.

### Why the issue's "doubly-nested" framing understates it

`UsageSiteDiscovery.DetectLambdaCaptureAncestor` (`UsageSiteDiscovery.cs:1702`) walks the invocation's
ancestors and disqualifies (QRY032) on any enclosing lambda **except** one that is a direct argument to
an invocation. Consequences:

- `Select(… => Task.Run(async () => …))` — both lambdas are invocation arguments, so nothing is
  disqualified, the chain is analyzed, and the bug surfaces as CS0103. This is the issue's repro.
- `new Func<…>(lambda)` — an *object-creation* argument, so QRY032 fires first and masks the bug.

So the trigger is **any enclosing lambda that is an invocation argument**, at depth 1 or deeper — not
double nesting specifically. A bisect over 9 shapes confirmed single-lambda, single-async-lambda,
nested, nested-without-outer-capture, capture-of-outer-param, `Update().Set`, and
local-function-inside-lambda all fail identically. This cost real time: the first two runtime probes
were written with `new Func<…>(…)` and with `var lite = t.Lite`, and both were masked by QRY032 rather
than reproducing the bug.

### Known-remaining gap: multi-scope captures (PRE-EXISTING, not #333)

A clause lambda that captures variables from **two different closure scopes** resolves to whichever
scope `CapturedInside` happens to enumerate first (`LookupClosureOrdinal` returns on the first match)
and fails at runtime. Verified both ways:

- With lambda nesting: `Where(u => u.UserName == name && u.UserId != i)` where `name` is an inner
  lambda local and `i` an outer lambda param → `MissingFieldException: '<>c__DisplayClass0_0.name'`.
- **With no lambda at all**: a method-level local plus an `if`-block local captured by the same clause
  lambda → `InvalidCastException: Unable to cast '<>c__DisplayClass0_1' to '<>c__DisplayClass0_0'`.
  Reproduced on the **unmodified** generator (verified by stashing the fix), so it is pre-existing and
  independent of this issue. Fixing it needs chained display-class access
  (`__target.CS$<>8__locals2.i`) or a disqualifier. Candidate separate issue.

### Step-1 spike result: chained display-class access is NOT expressible (2026-08-04)

Plan step 1 was a gate on whether a display-class **link field** can be read through an
`[UnsafeAccessor]` extern. It cannot. Four signature shapes tried against a hand-written three-scope
closure (`0_0 { lvl0 }`, `0_1 { lvl1, CS$<>8__locals1 }`, `0_2 { lvl2, CS$<>8__locals2 }`), reading
`CS$<>8__locals2` off the delegate's Target:

| Variant | Result |
|---|---|
| `ref object` + `[return: UnsafeAccessorType(...)]` | `NotSupportedException: Invalid usage of UnsafeAccessorTypeAttribute` |
| `ref object`, no return attribute | `MissingFieldException: '<>c__DisplayClass0_2.CS$<>8__locals2'` |
| `object` (non-ref) + `[return: UnsafeAccessorType]` | `BadImageFormatException: Invalid usage of UnsafeAccessorAttribute` |
| `object` (non-ref), no attribute | `BadImageFormatException: Invalid usage of UnsafeAccessorAttribute` |

Control: the zero-hop read (`lvl2` off the Target) works — that is what ships today.

Diagnosis, confirmed by two follow-up probes on .NET 10.0.10:

- `UnsafeAccessorTypeAttribute` has `AttributeTargets = Parameter, ReturnValue`, so return values are
  supported **in principle**, and a `UnsafeAccessorKind.StaticMethod` accessor returning an inaccessible
  private nested type via `[return: UnsafeAccessorType(...)]` works fine (verified end to end).
- The restriction is specific to `UnsafeAccessorKind.Field`: field accessors must return **byref**, and a
  byref return cannot declare an inaccessible type. The no-attribute variant fails with
  *MissingFieldException* rather than a type error because field lookup matches on name **and exact
  type** — declaring `ref object` for a field typed `<>c__DisplayClass0_1` simply misses.

Display-class link fields are always typed as other display classes (inaccessible), so no
`[UnsafeAccessor]` signature can read one.

### Step-1 second pass: a no-reflection approach that DOES work (2026-08-04)

User asked to keep "no reflection" and to test more approaches, incl. `ValueTuple`. Results:

**E1/E2 — the `UnsafeAccessor` restriction is blanket, not a naming problem.** `[return:
UnsafeAccessorType]` on a `Field` accessor fails identically for *ordinary* private nested types
(`Outer2.Link` of type `Hidden2`), with both simple and assembly-qualified names. The same return
attribute works fine on `UnsafeAccessorKind.StaticMethod` returning an inaccessible type. So the gap is
specifically: field accessors must return byref, and a byref return cannot name an inaccessible type.

**E3/E7/E8 — `Unsafe.As` overlay works, and `ValueTuple` is the same mechanism.** Reinterpreting the
Target with a shadow class of matching field shape reads the link correctly. A
`ValueTuple<int, object>` overlay reads identically, and on a mixed `{int, string, int}` closure the
class-shadow and tuple-overlay agree with each other and with declaration order. So ValueTuple is
viable but adds nothing over a shadow class — both are *positional* reads.

**E5/E6 — positional reads are the hazard.** With two `string` fields, shadow order `AB` yields
`(alpha, beta)` and order `BA` yields `(beta, alpha)` — a silent swap. A mismatched shadow returns
either a valid-looking wrong reference or throws `NullReferenceException` from reading past the object.
So a purely positional design could bind `@p0` to the wrong variable and return wrong rows with no error.

**E9 — the hybrid removes that hazard.** Use the overlay *only* to fetch the link reference, then read
the actual variable with the existing name-based `[UnsafeAccessor]`, whose `[UnsafeAccessorType(...)]`
parameter is **type-checked at runtime**:

| Step | Result |
|---|---|
| overlay → link, then typed accessor for the correct parent | works |
| feed that reference to an accessor expecting the *grandparent* type | `InvalidCastException` — loud |
| feed an unrelated object (a `string`) | `InvalidCastException` — loud |
| full two-hop walk, each hop validated by the next typed accessor | works |

Every hop's result is immediately validated by the next typed accessor, so a misprediction surfaces as
`InvalidCastException`/`NullReferenceException` rather than silent wrong data. Variables themselves are
still read **by name**, never positionally.

### Upstream documentation on both approaches (2026-08-04)

Checked whether .NET documents these limits, rather than relying on local experiment alone. Both are
documented, and the second is decisive.

**1. The `UnsafeAccessor` field gap is deliberate and tracked upstream.**
[dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664) — *"`UnsafeAccessorTypeAttribute`
support for field accessors"*. **Open, milestone `Future`, unassigned.** Fields were excluded from the
initial `UnsafeAccessorTypeAttribute` implementation on purpose:

> "having a `ref object` return type for the accessor isn't memory safe, it would need `TypedReference`
> support for that."

So the local E1/E2 result is not a bug or a naming mistake — it is the runtime team's design decision,
with no ETA. Nothing to work around.

**2. The `Unsafe.As` overlay is explicitly undefined behaviour, and cannot be pinned.**
[dotnet/runtime discussion #111049](https://github.com/dotnet/runtime/discussions/111049):

> EgorBo (runtime): "All kinds of struct <-> class reinterpretation-like casts are UB and may lead to
> hard-to-reproduce crashes/gc holes."

> tannergooding (runtime): `Sequential` controls managed layout only for **blittable** types; for
> non-blittable types managed layout is treated as **`Auto`**. `Explicit` controls both.

Decisive for this design: display classes hold reference-type fields (strings, entity references, the
link itself), so they are **non-blittable → Auto layout → no guaranteed field offsets**, and there is no
attribute we could rely on to pin them. E3/E7/E8 passing is exactly the "works until it doesn't" profile
that description predicts.

**The E9 hybrid does not neutralise this.** Its type check runs *after* the read. If the overlay lands on
a misaligned slot the damage — handing the GC a non-reference as a reference — has already happened; that
is a crash or heap corruption, not an `InvalidCastException` the checksum can catch. The safety net only
helps when the read already produced a valid object reference.

Note the irony that settles it: the runtime team declined to expose `ref object` field access **because it
is not memory safe**. The overlay achieves that same effect with none of their type checking.

Roslyn display-class *field ordering*: no explicit upstream stability guarantee found either way; it is a
compiler-generated artifact. That is a further unpinned assumption the overlay would need on top of the
layout one.

**Conclusion: the multi-scope fix is not implementable safely.** Recommendation reverts to the
build-time guard (the fallback the issue itself proposed).

### Guard scoping gotcha: `CapturedInside` includes NESTED subquery lambdas (2026-08-04)

The first version of the multi-scope guard counted every entry in the clause lambda's
`dataFlow.CapturedInside`. That fired on a pile of **currently-passing** tests —
`CrossDialectNestedSubqueryTests`, `CrossDialectSetOperationTests`, `MySqlIntegrationTests` — because a
clause like `u => u.Orders.Any(o => o.Items.Any(i => …))` contributes the nested lambdas' own parameters
and locals to `CapturedInside`. Those live *inside* the clause, are handled by the SQL translator, and
are never extracted from a display class.

This also falsified the reasoning used to justify the guard as "safe by construction" — the earlier claim
that *any* genuine two-scope capture is already broken today. It is not: nested-subquery chains span
several syntactic scopes and work fine.

Fix: count only captured variables whose **declaration lies outside the clause lambda's span**
(`lambda.Span.Contains(declRef.Span)` → skip). After that the guard fires on exactly the four intended
shapes and no working chain. Caught only by running the suite — a guard written from reasoning alone
would have broken a lot of valid code.

### Unrelated pre-existing bug found while probing: context misattribution on a member-access root

A chain rooted at a member access — `t.Lite.Users()…` rather than the deconstructed
`var (Lite, …) = t; Lite.Users()…` that every existing test uses — emits an interceptor file for the
**wrong context**: `CteDb.Interceptors.…g.cs` containing `internal CteDb? Ctx;` but
`public static IEntityAccessor<User> Users_…(this TestDbContext @this)`, producing CS9144 (signature
mismatch) plus `CS0029: Cannot implicitly convert 'TestDbContext' to 'Cte.CteDb'`. Reproduced with the
generator fix stashed, so it is pre-existing and unrelated. Candidate separate issue.

## Suspend State
## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-08-04 | INTAKE | Loaded issue #333, created worktree/branch `333-nested-lambda-captures`, baseline green (201/146/3501) |
| 2026-08-04 | DESIGN | Bisected 9 shapes; root-caused to AnonymousFunction unwrap + parameter scope resolution; dumped display-class ground truth from emitted IL; found 2 pre-existing unrelated bugs |
| 2026-08-04 | PLAN | Wrote plan.md (10 steps); user chose full multi-scope fix + revert ConcurrencyTests workaround |
| 2026-08-04 | IMPLEMENT | Step-1 gate failed (chained access not expressible); measured 10 end-user shapes; user approved re-plan into 3 fixes + 1 guard |
| 2026-08-06 | IMPLEMENT | Steps 2-9 complete. Full suite green 201/146/3528. Filed #338 and #339 |
