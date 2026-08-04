# Plan (revised): 333-nested-lambda-captures

Supersedes the first plan, whose steps 2–6 assumed chained display-class access was buildable. The
step-1 gate disproved that (see workflow.md). Re-planned on evidence from ten realistic end-user shapes:
the failures are **three different bugs**, only one of which is actually blocked upstream.

## What the evidence says

Ten ordinary Quarry shapes, run against the current generator (`GuardScopeProbeTests`):

| Shape | Today |
|---|---|
| all captures in one scope | works |
| instance field alone | works |
| `foreach` variable alone | works |
| `if`-block local alone | works |
| `foreach` var + method local, one lambda | `InvalidCastException` |
| `for` body local + method local, one lambda | `InvalidCastException` |
| `if`-block local + method local, one lambda | `InvalidCastException` |
| instance field + method local | `MissingFieldException: '..._minId'` |
| nested lambda capturing both levels | `MissingFieldException: '...name'` |
| `foreach` var + method local, **separate clauses** | `MissingFieldException: '...name'` |

The single-scope rows pass only because with one capture scope ordinal 0 is correct **by accident** —
the mis-scoping is invisible until a second scope exists.

Three distinct causes:

1. **Mis-scoped declarations.** `FindDeclaringScope` walks a variable up to the nearest enclosing
   `BlockSyntax`. For a `foreach`/`for` variable the declaring syntax is the loop statement, whose parent
   is the *enclosing* block — so the loop variable is predicted into the method scope. Ground truth
   (`MechanismProbeTests`) shows both `foreach` and `for` emit
   `_0 { minId }` and `_1 { name, CS$<>8__locals1 → _0 }`: the loop variable has its **own** per-iteration
   display class. Same class of bug as the lambda-parameter mis-scoping already fixed for #333.
   This is what breaks the **separate-clauses** row, where no lambda is multi-scope at all.

2. **`this` reached through the display class.** When a lambda mixes an instance field with a local, the
   display class holds `<>4__this` (verified live: mutating the field changed the closure's result
   103 → 1004) and the field is read off it. Quarry instead emits a display-class extractor named for the
   field, which is not there. **Fixable:** `<>4__this`'s type is the user's own class — accessible and
   nameable — so a plain `[UnsafeAccessor(Field, Name = "<>4__this")]` returning `ref TContaining`
   works. Verified end to end: returns the same instance (`ReferenceEquals` true) and reads live.
   No `[return: UnsafeAccessorType]`, so [runtime#119664](https://github.com/dotnet/runtime/issues/119664)
   does not apply.

3. **One lambda capturing locals from two closure scopes.** Genuinely blocked — reaching the outer scope
   needs the `CS$<>8__locals` link field, whose type is another display class, and
   [runtime#119664](https://github.com/dotnet/runtime/issues/119664) is open/`Future` because
   `ref object` field returns are not memory safe. The `Unsafe.As` overlay alternative is UB per
   [runtime discussion #111049](https://github.com/dotnet/runtime/discussions/111049). Only this subset
   gets a guard.

## Steps

- [x] **1. Gate: is chained access expressible?** No. Four signature shapes fail; the restriction is
  blanket (reproduced on ordinary private nested types) and matches an open upstream issue. Recorded in
  workflow.md.

- [x] **2. Scope resolution for declaration forms that own a scope.** Extend `FindDeclaringScope` so a
  variable declared by a `foreach`, `for`, `using`, or `switch` section resolves to the scope the compiler
  actually gives it (the loop/using body), not the enclosing block. Parameters (already fixed for #333)
  stay as they are.
  *Tests:* `DisplayClassEnricherTests` — predicted ordinal for a `foreach` variable alongside a
  method-scope local matches ground truth `_1`/`_0`.

- [x] **3. Count the distinct capture scopes per clause.** Originally specified as "pick the innermost
  scope as the Target". Simplified: since step 5 guards *every* genuinely multi-scope clause, the clauses
  that survive all capture from exactly one scope, where the existing first-match lookup is already
  correct. So the useful output is not a better Target but the **number of distinct closure scopes** a
  clause captures from, carried on `RawCallSite` (excluded from `Equals`, like the other enricher-set
  members) as the guard's input. Smaller and lower-risk than re-deriving the Target.
  Note `this` is already excluded from the capture set, so a field+local clause counts as ONE scope and
  correctly does not trip the guard — it is step 4's case.
  *Tests:* `DisplayClassEnricherTests` — scope count is 1 for single-scope and field+local clauses, 2 for
  the loop-var-plus-method-local and nested-lambda clauses.
  *Depends on:* 2.

- [x] **4. `<>4__this` indirection for field captures mixed with locals.** When a clause lambda captures
  both an instance field and a local, emit a `<>4__this` accessor returning `ref TContaining` and read the
  field from that instance, instead of emitting a display-class extractor named for the field. Keep the
  existing pure-FieldCapture path (no display class) unchanged. Fall back to step 5's guard if the
  containing type is not nameable/accessible from the interceptor.
  *Tests:* codegen assertion on the emitted accessor + a runtime test asserting rows, including that a
  field mutated after chain construction is observed.
  *Depends on:* 3.

- [x] **5. Guard the genuinely blocked subset.** Detect a clause lambda capturing **locals/parameters from
  two or more distinct closure scopes** and disqualify with a QRY diagnostic. Message must name the shape
  and give the real workaround — *split the predicate into separate `.Where(...)` clauses* — which steps
  2–3 make work. Must NOT fire for: single-scope captures, pure field captures, or field+local (step 4).
  *Tests:* diagnostic fires for the four multi-scope shapes and does **not** fire for the six others.
  *Depends on:* 4.

- [x] **6. Permanent regression tests.** Convert the scratch probes into real fixtures:
  compile-level matrix (no CS0103) for the #333 lambda shapes, plus **runtime execution** tests for every
  row of the table above — passing rows must execute correctly, guarded rows must produce the diagnostic.
  Compilation alone proves nothing here; that is exactly how the first spike passed and then threw.
  Include a note that enclosing lambdas must be *invocation arguments* — `new Func<…>(…)` trips QRY032
  and masks the bug.
  *Depends on:* 5.

- [x] **7. Revert the `ConcurrencyTests` workaround; correct the docs.** Inline the worker bodies again.
  Where the separate pre-existing member-access-root bug blocks it, keep the named method and say so in
  the fixture `<remarks>`. Rewrite the `llm-testing.md` gotcha, and update "Display Class Prediction" in
  `src/Quarry.Generator/llm.md` with the scope rules, the `<>4__this` rule, the ground-truth tables, and
  the guarded shape with its upstream justification.
  *Depends on:* 6.

- [x] **8. Clean up and verify.** Delete every scratch fixture (bisect, runtime probe, flat multi-scope,
  display-class dump, chained-accessor spike, hop experiments, guard-scope probe, mechanism probe).
  Rebuild so manifest goldens regenerate from the real chain set, and commit them — CI runs
  `git diff --exit-code -- src/Quarry.Tests/ManifestOutput`. Full `dotnet test Quarry.sln` green against
  the INTAKE baseline (201 / 146 / 3501).
  *Depends on:* 7.

- [ ] **9. File the separate issues.**
  - Member-access chain root (`t.Lite.Users()`) emits an interceptor bound to the wrong context
    (CS9144 + CS0029). Pre-existing, reproduced with the fix stashed.
  - Multi-scope captures, blocked on [runtime#119664](https://github.com/dotnet/runtime/issues/119664) —
    what the guard rejects, why, and what to revisit if that issue ships.

## Risks

- **Steps 2–3 change existing predictions.** Any chain whose captures are in a loop/using scope shifts
  display class. The full suite is necessary but not sufficient — only the runtime tests in step 6 prove
  a prediction, since a wrong one still compiles.
- **Scope creep is real and deliberate.** Only step 5 plus the #333 core were in the original issue;
  steps 2–4 fix pre-existing silent runtime failures found while probing. User approved this expansion.
- **Prediction remains prediction.** Undocumented Roslyn detail; the ground-truth tables going into
  `llm.md` are what make the next break diagnosable.
