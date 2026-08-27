# Research: can Roslyn tell us the display-class name?

Research only — no `src/` changes were made. Every "VERIFIED" claim below was produced by running code
on this machine (.NET SDK 10.0.110, `Microsoft.CodeAnalysis.CSharp` 4.11.0 / 4.14.0 / 5.0.0); the raw
programs are described inline so they can be rebuilt. Claims marked "SOURCE" were read in
dotnet/roslyn or dotnet/runtime source; claims marked "INFERRED" are reasoning, not observation.

---

## 1. Verdict on the primary question

**No. There is no Roslyn API — public or reachable — from which a source generator can learn a
display class's name. The hypothesis is correct and I could not falsify it.**

Three independent lines of evidence.

### 1a. VERIFIED — the types do not exist in the object model a generator sees

A probe compiled a source file containing four capturing lambdas and then enumerated *everything*
reachable from the `Compilation` before `Emit`:

```
=== PROBE 1: all type symbols reachable from the Compilation (pre-emit) ===
  MyNs.Repo  metadataName=Repo  implicitlyDeclared=False
  -> any name containing 'DisplayClass': False

  GetSymbolsWithName(all) count=10; any 'DisplayClass': False
  GetTypeByMetadataName("MyNs.Repo+<>c__DisplayClass3_0") => null
```

The same compilation, after `Emit`, read back through `System.Reflection.Metadata`:

```
=== PROBE 5: EMITTED metadata (post-lowering ground truth) ===
  Emit success=True
  Repo+<>c__DisplayClass10_0   fields=[harnesses]
  Repo+<>c__DisplayClass10_1   fields=[index, CS$<>8__locals1]
  Repo+<>c__DisplayClass10_2   fields=[name]
  Repo+<>c__DisplayClass11_0   fields=[<>4__this, local]
  Repo+<>c__DisplayClass9_0    fields=[minId]
  Repo+<>c__DisplayClass9_1    fields=[name, CS$<>8__locals1]
```

Six display classes exist in the emitted PE. Zero exist in the symbol table. They are created during
`Emit`, and a generator has already finished by then.

### 1b. VERIFIED — the symbol/dataflow APIs the generator already uses carry nothing extra

`IMethodSymbol` for a lambda:

```
  lambda `u => u > minId && name.Length > 0`
     symbol=lambda expression kind=AnonymousFunction name='' metadataName=''
     ContainingType=MyNs.Repo  ContainingSymbol=MyNs.Repo.Query(int)
```

`Name` and `MetadataName` are the empty string; `ContainingType` is the *user's* type. There is no
`AssociatedSynthesizedType`, no frame, no environment. `SymbolDisplayFormat` cannot help — it formats
a symbol that does not exist.

`DataFlowAnalysis`'s complete public surface is:

```
VariablesDeclared, DataFlowsIn, DataFlowsOut, DefinitelyAssignedOnEntry, DefinitelyAssignedOnExit,
AlwaysAssigned, ReadInside, WrittenInside, ReadOutside, WrittenOutside, Captured, CapturedInside,
CapturedOutside, UnsafeAddressTaken, UsedLocalFunctions, Succeeded
```

It tells you *which variables* are captured — which is exactly what `DisplayClassNameResolver` already
uses — and nothing about *where they will be put*.

### 1c. VERIFIED — the machinery exists in the compiler, and is internal-only

Reflecting over the shipped `Microsoft.CodeAnalysis.CSharp.dll` (5.0.0):

```
  Microsoft.CodeAnalysis.CSharp: PUBLIC types matching closure/frame/environment/display/lambda = []

  ... NON-PUBLIC ...
      Microsoft.CodeAnalysis.CSharp.SynthesizedClosureEnvironment
      Microsoft.CodeAnalysis.CSharp.SynthesizedClosureEnvironmentConstructor
      Microsoft.CodeAnalysis.CSharp.SynthesizedClosureMethod
      GeneratedNames: IsPublic=False visibility=NotPublic
        MakeStaticLambdaDisplayClassName(Int32 methodOrdinal, Int32 generation)
        MakeLambdaDisplayClassName(Int32 methodOrdinal, Int32 generation, Int32 closureOrdinal, Int32 closureGeneration)
        MakeLambdaMethodName(String methodName, Int32 methodOrdinal, Int32 methodGeneration, Int32 lambdaOrdinal, Int32 lambdaGeneration)
        MakeLambdaCacheFieldName(Int32 methodOrdinal, Int32 generation, Int32 lambdaOrdinal, Int32 lambdaGeneration)
```

`Microsoft.CodeAnalysis` (the common layer) also has `Microsoft.CodeAnalysis.CodeGen.ClosureDebugInfo`
— non-public. **Every closure concept in Roslyn is internal.** There is no public projection, no
`InternalsVisibleTo` a third party can obtain, and no analyzer-facing hook.

Note also that even if `GeneratedNames.MakeLambdaDisplayClassName` were public, it would not help: it
*takes* `methodOrdinal` and `closureOrdinal` as inputs. The hard part is not formatting the string, it
is knowing the two numbers — and those are computed inside `ClosureConversion`, from the **bound**
tree, during `Emit`.

### 1d. Phase ordering — SOURCE

- Generators run from `CommonCompiler.RunGenerators`
  (`src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs`, ~L1160), whose own comment reads
  *"At this point we have a compilation with nothing yet computed."* It calls
  `GeneratorDriver.RunGeneratorsAndUpdateCompilation`, which ends in
  `outputCompilation = compilation.AddSyntaxTrees(trees);` — purely additive, at the syntax-tree
  level. `docs/features/source-generators.md`: *"Explicitly additive only. Generators can add new
  source code to a compilation but may **not** modify existing user code."*
- Display classes are created inside `Compilation.Emit`:
  `Emit` → `CSharpCompilation.CompileMethods` → `MethodCompiler.CompileMethodBodies` →
  `CompileNamedType` (the `memberOrdinal` loop) → `CompileMethod` → `LowerBodyOrInitializer` →
  `ClosureConversion.Rewrite(..., methodOrdinal, ...)` → `SynthesizeClosureEnvironments` →
  `new SynthesizedClosureEnvironment(...)` → `MakeName`. `SerializeToPeStream` runs after.
- There is no observable lowering stage. `src/Compilers/Core/Portable/Compilation/CompilationStage.cs`
  is `internal enum CompilationStage { Parse, Declare, Compile, }` — three stages, all pre-lowering,
  and the enum itself is internal. Lowering produces `BoundNode` trees, which are internal.
- A generator can never observe lowered code. The generator's output is *input* to the compilation
  whose lowering creates the closures; the dependency runs one way only. Display classes never enter
  the symbol table at all — they go straight to Cci via
  `ModuleBuilderOpt.AddSynthesizedDefinition(ContainingType, frame.GetCciAdapter())`.

Independent confirmation from the people who designed `UnsafeAccessorType`
([dotnet/runtime#90081](https://github.com/dotnet/runtime/issues/90081)), where EF Core asked for
closure-frame support:

> **roald-di:** "is there even a way to get the DisplayClass and other generated type names with
> roslyn? From what I have seen those names are generated during lowering and are not available
> through the api."
> **AndriySvyryd (EF Core):** "Right. To avoid having to reference those names we would need to
> create an interface for every one of these types."

### 1e. `IOperation` / `ControlFlowGraph` — VERIFIED, does not substitute

`ControlFlowGraph` capture ids are **spilling temporaries** (for `?.`, compound assignment, `??=`,
etc.), not closure environments. Direct falsification from the probe — the method with *three*
captured variables has *zero* flow captures:

```
  method Concurrent: cfg blocks=7, localFunctions=0     <- lambda captures harnesses, index, name
    IFlowCaptureOperation ids: [] (n=0)
    IFlowCaptureReference n=0
    IFlowAnonymousFunctionOperation n=1
      anon fn symbol=lambda expression name='' metadataName='' containingType=MyNs.Repo

  method Query: cfg blocks=8, localFunctions=0          <- lambda captures minId, name
    IFlowCaptureOperation ids: [0] (n=1)                <- the ONE capture is the foreach temp
```

`CaptureId`'s entire public surface is `Equals`, `Equals`, `GetHashCode` — the ordinal is not even
readable. `IFlowAnonymousFunctionOperation.Symbol` is the same nameless lambda symbol as 1b, and
`ControlFlowGraph.GetAnonymousFunctionControlFlowGraph` returns a graph whose `OriginalOperation.Kind`
is `AnonymousFunction` — still pre-lowering. **There is no documented relationship between CFG
capture ids and emitted display classes, and the counts do not even correlate.**

---

## 2. Is the numbering specified anywhere? No — and it is explicitly disclaimed

### 2a. What the compiler actually does — SOURCE

`methodOrdinal` **is** the `GetMembers()` index, so the generator's `ComputeMethodOrdinal` mirrors
Roslyn correctly in principle.
`src/Compilers/CSharp/Portable/Compiler/MethodCompiler.cs` (~L500):

```csharp
var members = containingType.GetMembers();
for (int memberOrdinal = 0; memberOrdinal < members.Length; memberOrdinal++) {
    ...
    CompileMethod(method, memberOrdinal, ...);
```

and `ClosureConversion.cs` L222 documents it:
`/// <param name="methodOrdinal">Index of the method symbol in its containing type member list.</param>`

VERIFIED against emitted metadata — for `MyNs.Repo`:

```
=== PROBE 6: containing type GetMembers() ordering ===
  [0] Field      _field
  [1] Method     A
  [2] Field      <Prop>k__BackingField
  [3] Property   Prop
  [4] Method     get_Prop
  [5] Method     set_Prop
  [6] Method     add_Ev
  [7] Method     remove_Ev
  [8] Event      Ev
  [9] Method     Query          -> emitted <>c__DisplayClass9_0 / 9_1
  [10] Method    Concurrent     -> emitted <>c__DisplayClass10_0 / 10_1 / 10_2
  [11] Method    Mixed          -> emitted <>c__DisplayClass11_0
```

The consequence is worse than "another lambda was added": **any** member added earlier in the type —
a field, a property, an event, a nested type, a partial-file reordering, another generator's
contribution to the same partial type — shifts every subsequent method's ordinal. That is the failure
mode already tracked as issue #310. Demonstrated on SDK 10.0.110 with a type whose only change is one
*unreferenced private field*:

```
                        WITHOUT field (Debug)      WITH field (Debug)
  A(int a)              <>c__DisplayClass0_0       <>c__DisplayClass1_0
  B(int b)              <>c__DisplayClass1_0       <>c__DisplayClass2_0
```

Same class of break as [roslyn#68542](https://github.com/dotnet/roslyn/issues/68542), where adding
`private string Read;` renamed an iterator state machine `<All>d__0` to `<All>d__1`. And
`INamespaceOrTypeSymbol.GetMembers()` carries **no documented ordering guarantee** — its doc comment
promises only "all the members of this symbol… Never returns Null."

`closureOrdinal` comes from `ClosureConversion.Analysis`, a walk over the **bound** tree. Its value is
`closureDebugInfo.Count` at the moment the environment is created
(`Analysis.GetClosureId`), where `closureDebugInfo` is allocated fresh per top-level method in
`MethodCompiler.CompileMethod` and filled in scope-tree traversal order via `Analysis.VisitScopeTree`
— **not** source order, and **not** the syntactic pre-order `AssignOrdinalsPreOrder` computes. Two
sentinel values live in the same space (`Core/Portable/CodeGen/LambdaDebugInfo.cs`):
`StaticClosureOrdinal = -1`, `ThisOnlyClosureOrdinal = -2`.

### 2b. There is no stability contract, and Roslyn says so explicitly

The single most direct statement, from the Roslyn lead
([dotnet/roslyn#55758](https://github.com/dotnet/roslyn/issues/55758#issuecomment-904862914),
jaredpar, 2021):

> The compiler makes **zero guarantees** about how we emit closure / lambda / async code to metadata.
> We will, and previously have, changed our emit strategy when doing so provided substantial
> performance wins. Customers who depend on the structure of our metadata are doing so at their own
> risk and should expect to be broken from time to time.

The only normative text anywhere that touches closure ordinals is the Portable PDB spec — and it
exists to disclaim them.
[`dotnet/runtime docs/design/specs/PortablePdb-Metadata.md`](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md),
§ "Edit and Continue Lambda and Closure Map":

| terminal | description |
|---|---|
| _method-ordinal_ | **Implementation specific number** derived from the source location of Parent method. |

> The exact algorithm used to calculate syntax offsets and the algorithm that maps lambdas/closures to
> their implementing methods, types and syntax nodes is **language and implementation specific and may
> change in future versions of the compiler**.

Roslyn's own design doc (`docs/compilers/Design/Closure Conversion.md`) uses `<>_Env1` / `<>_Env2` in
its worked example — the internal design doc's names do not even match shipped output. There is no
entry for closure naming in any Compiler Breaking Changes doc, because it was never a contract.

Requests for a public API have been made twice and refused:
- [roslyn#11565](https://github.com/dotnet/roslyn/issues/11565) "Provide a public API to parse generated names" — **closed, not planned**.
- [roslyn#55651](https://github.com/dotnet/roslyn/issues/55651) (ASP.NET Core) — open since 2021, unimplemented.
- [roslyn#50978](https://github.com/dotnet/roslyn/issues/50978) — jaredpar: mapping IL symbols back to source "is a non-goal of the compiler."

### 2c. It has changed before — four shipped instances, none in a breaking-change doc

| Change | PR / issue | Effect |
|---|---|---|
| Merge display classes when safe | [PR#32092](https://github.com/dotnet/roslyn/pull/32092) | **Changes how many display classes exist → renumbers `closureOrdinal` in Release.** See §3. |
| Local function name terminator | [#21822](https://github.com/dotnet/roslyn/issues/21822) / [PR#21848](https://github.com/dotnet/roslyn/pull/21848) | `<M>g__Name5001_1` → `<M>g__Name\|1_1` |
| Top-level statements renamed | [#45564](https://github.com/dotnet/roslyn/issues/45564) / [PR#45930](https://github.com/dotnet/roslyn/pull/45930) | `$Program`/`$Main` → `<Program>$`/`<Main>$` |
| Primary-ctor backing field | [#67103](https://github.com/dotnet/roslyn/issues/67103) | `<name>PC__BackingField` → `<name>P` |
| **Extension block ordinal scheme** | [#78416](https://github.com/dotnet/roslyn/issues/78416) / [PR#78523](https://github.com/dotnet/roslyn/pull/78523), 2025 | jcouv: "we made an adjustment to the numbering scheme for `<>E__...` type names, so that the numbering only counts extension blocks (but not other declarations)" |

That last one is structurally the *same bug* Quarry has — an ordinal counting all declarations rather
than only the relevant ones — and Roslyn's response was to change the numbering, not to stabilise it.

jaredpar's own written case against ordinal-based synthesized names
([csharplang#9457](https://github.com/dotnet/csharplang/pull/9457), merged 2025):

> The current metadata strategy for declaration members relies on ordinals … **any use of ordinals
> tied to source ordering means that adding or removing elements can create unnecessary, and possibly
> unresolvable, conflicts.**

**Answer to "is this a bug worth reporting upstream?" — No.** It is documented, intentional,
repeatedly reaffirmed behaviour. Filing it would be closed as by-design. The only upstream issue with
any bearing is [dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664) (already
tracked as #339) and, more usefully, [efcore#33418](https://github.com/dotnet/efcore/issues/33418),
where EF Core is asking for the same capability and has been waiting since 2024.

---

## 3. NEW FINDING: the SDK-version story is probably a red herring — it is Debug vs Release

This is the most actionable result of the research and it changes the shape of issue #344.

### 3a. SOURCE — the compiler merges closure environments only when optimizing

`src/Compilers/CSharp/Portable/Lowering/ClosureConversion/ClosureConversion.Analysis.cs`,
`Analysis.Analyze`:

```csharp
analysis.MakeAndAssignEnvironments();
analysis.ComputeLambdaScopesAndFrameCaptures();
if (compilationState.Compilation.Options.OptimizationLevel == OptimizationLevel.Release)
{
    // This can affect when a variable is in scope whilst debugging, so only do this in release mode.
    analysis.MergeEnvironments();
}
analysis.InlineThisOnlyEnvironments();
```

That single `if` is the entire gate — there is no EnC, debuggability, `DebugType`, sequence-point or
slot-allocator condition anywhere in the merge walk. (`InlineThisOnlyEnvironments()` is *not* gated,
which is why the `this`-only case in §6.4 below behaves identically in both configurations.)

`MergeEnvironments()` doc comment, verbatim:

> In order to reduce allocations, merge environments into a parent environment when it is safe to do
> so. This must be done whilst preserving semantics. We also have to make sure not to extend the life
> of any variable. This means that we can only merge an environment into its parent if exactly the
> same closures directly or indirectly reference both environments.

The renumbering chain is exact:

1. `MergeEnvironments` folds the child's captures into the parent and sets `scope.DeclaredEnvironment = null;`
2. `ClosureConversion.SynthesizeClosureEnvironments` only creates a frame for a scope that still has one:
   `if (scope.DeclaredEnvironment is { } env) { var frame = MakeFrame(scope, env); … }`
3. `MakeFrame` → `_analysis.GetClosureId(...)`, whose ordinal is `closureDebugInfo.Count` — incremented
   **only per surviving environment**.

A merged-away environment therefore never consumes an ordinal, and **every later `closureOrdinal`
shifts down by one**. It stops at a scope with `CanMergeWithParent == false` (set only by a backward
`goto` — `ScopeTreeBuilder.CheckCanMergeWithParent`) or where the set of capturing closures differs,
and it skips struct environments (`if (scopeEnv.IsStruct) continue;`).

Provenance: [PR#32092](https://github.com/dotnet/roslyn/pull/32092) "Optimise DisplayClass
Allocations", merged 2019-04-25, fixing [#29965](https://github.com/dotnet/roslyn/issues/29965)
"Closure may be unnecessarily split". No breaking-change doc entry was ever filed.

> **The knob is `<Optimize>`, not the configuration name.** `OptimizationLevel` maps to `/optimize+` /
> `/optimize-` (`src/Compilers/Core/Portable/Compilation/OptimizationLevel.cs`), i.e. the MSBuild
> `<Optimize>` property — **not** `<DebugType>`, `<DebugSymbols>`, or `$(Configuration)`. A
> configuration named "Debug" with `<Optimize>true</Optimize>` will merge; one named "Release" with
> `<Optimize>false</Optimize>` will not. Anyone reasoning about this must look at `<Optimize>`.

### 3b. VERIFIED — reproduced locally, one SDK, one compiler build

Same source, same `Microsoft.CodeAnalysis.CSharp` 5.0.0, only `OptimizationLevel` differs:

```csharp
int a = 1;
if (a > 0) { var b = 2; Use(u => u > a && u > b); }
foreach (var c in new[]{1,2}) { Use(u => u > c); }
```

```
Debug:    <>c__DisplayClass1_0 [a]      <>c__DisplayClass1_1 [b, CS$<>8__locals1]   <>c__DisplayClass1_2 [c]
Release:  <>c__DisplayClass1_0 [a, b]   <>c__DisplayClass1_1 [c]
```

Driving Quarry's **own** `DisplayClassNameResolver` (loaded by reflection from
`src/Quarry.Generator/bin/Debug/netstandard2.0/Quarry.Generator.dll`) over the identical trees:

```
--- optimization=Debug ---
  Quarry ComputeMethodOrdinal = 1
  lambda `u => u > a && u > b`  -> PREDICTED Repo+<>c__DisplayClass1_0   (captureScopes=2)
  lambda `u => u > c`           -> PREDICTED Repo+<>c__DisplayClass1_2   (captureScopes=1)   OK

--- optimization=Release ---
  lambda `u => u > c`           -> PREDICTED Repo+<>c__DisplayClass1_2   (captureScopes=1)   WRONG (actual _1)
```

Note `captureScopes=1` on the failing clause — **the existing multi-scope guard does not fire.** The
clause that gets mispredicted is a perfectly ordinary single-scope capture; it is *another,
unrelated* lambda elsewhere in the same method that causes the merge and the renumbering. Quarry
cannot guard on a lambda it never looks at.

### 3c. VERIFIED — this reproduces issue #344's exact symptom

Rebuilding the `ConcurrencyTests.ParallelHarnesses_MixedReadWrite_DoNotShareParameterState` method
shape from commit `9d3aaf2` (Quarry chain replaced by same-shaped stubs, `Assert.Multiple(() => …)`
lambda retained because it is what makes the `try` block a capture scope):

```
--- optimization=Debug ---   Quarry methodOrdinal=3
   `u => u.UserName = name`  PREDICT <>c__DisplayClass3_3  scopes=1
   ACTUAL:
     <>c__DisplayClass3_0  [harnesses]
     <>c__DisplayClass3_1  [results, CS$<>8__locals1]
     <>c__DisplayClass3_2  [index, CS$<>8__locals2]
     <>c__DisplayClass3_3  [name]                          <- prediction correct, test PASSES

--- optimization=Release ---   Quarry methodOrdinal=3
   `u => u.UserName = name`  PREDICT <>c__DisplayClass3_3  scopes=1
   ACTUAL:
     <>c__DisplayClass3_0  [harnesses, results]            <- try-block env merged into method env
     <>c__DisplayClass3_1  [index, CS$<>8__locals1]
     <>c__DisplayClass3_2  [name]                          <- _3 does not exist -> TypeLoadException
```

This is the reported failure exactly: prediction `_3`, no `_3` in the assembly,
`TypeLoadException: Could not resolve type '…+<>c__DisplayClass3_3'`.

**And the environments line up with the reported environments:** `dotnet test` defaults to **Debug**
(what a developer runs locally); `.github/workflows/ci.yml` L47/L50 runs `dotnet build -c Release` and
`dotnet test -c Release`. The "local passes / CI fails" split in `_issue-version.md` is fully explained
by configuration, with no SDK difference required.

### 3d. VERIFIED — the compiler versions I could test do NOT diverge

Same 25-shape matrix (`foreach`/`for`/`using`/`await using`/`switch`-section/`switch`-expression/
`catch`/`lock`/query-expression/interpolated-handler/iterator/local-function/3-deep-nesting/
async-lambda-in-loop/primary-ctor/tuple-deconstruction), compiled through
`Microsoft.CodeAnalysis.CSharp` **4.11.0, 4.14.0 and 5.0.0**:

```
===== diff 4.11 vs 5.0 (debug) =====   (only the version banner differs)
===== diff 4.14 vs 5.0 (debug) =====   (only the version banner differs)
```

Zero closure-ordinal differences across three compiler generations. The Debug/Release axis, by
contrast, changed **7 of 25 shapes**. That is a strong (not conclusive — see §7) indication that the
`10.0.110` vs `10.0.302` framing in `_issue-version.md` mis-attributes the cause, and that
**`global.json` pinning would not have helped at all.**

Live corroboration on the real assembly — `Quarry.Tests.dll` built both ways from the current commit:

```
Debug   : type defs: 6372   display classes: 531
Release : type defs: 6368   display classes: 527
```

Four display classes vanish in Release **in the shipping test project today**. The current fixtures
happen not to sit downstream of any of them.

---

## 4. Alternatives

| # | Mechanism | Feasible here? | What it costs | What it breaks |
|---|---|---|---|---|
| A | **Post-compile verification** (MSBuild target after `CoreCompile` reads the emitted PE with `System.Reflection.Metadata`, checks every `[UnsafeAccessorType]` string against real typedefs and every `Name=` against real fields) | **Yes — prototyped and working** | ~150 LOC + an MSBuild task; 0.7 s on a 6 372-type assembly | Nothing. Does not fix a wrong guess, converts it from a production `TypeLoadException` into a build error |
| B | **Runtime self-check in the generated carrier** (compare `func.Target.GetType()` / catch `TypeLoadException` once, throw a diagnostic naming predicted vs actual) | Yes | one type-identity check per call site, first execution only | Uses `object.GetType()` — trim/AOT-safe, but it *is* reflection-lite. Still fails at runtime, just legibly |
| C | **Guard the shapes Release can merge** — extend the existing QRY032-style disqualifier so a clause is rejected whenever *any* mergeable scope (`if`/`using`/`lock`/`switch`-section/`catch`/`try`) sits between it and the method root | Yes | rejects working code; users must hoist captures to method scope | False positives. And it is unsound in principle: merging is the compiler's call, not a syntactic property |
| D | **Explicit state parameter** — `Where((u, p) => u.UserId > p, minId)` | Yes, cleanly | public API churn; caller boilerplate | Nothing technically — the lambda becomes non-capturing (`Static`/`Singleton` closure kind, **no display class at all**), so there is nothing to predict. Precedent: `ConcurrentDictionary.GetOrAdd(key, factory, arg)`, `string.Create(state, action)` ([runtime#13978](https://github.com/dotnet/runtime/issues/13978)) |
| E | **`Expression<Func<…>>` instead of `Func<…>`** | Technically yes, architecturally no | an `Expression` tree allocation **per call** — the exact cost Quarry exists to avoid | The closure arrives as `MemberExpression(ConstantExpression(displayClassInstance), FieldInfo)` with a Roslyn-emitted `FieldInfo`, so no name is ever predicted — but reading the leaf means `FieldInfo.GetValue` or the LINQ interpreter, i.e. reflection. This is exactly what EF Core does (§5) |
| F | **Invoke the delegate — `Set(Action<T>)`** | **Yes, and it plainly works** | one probe-entity allocation per call | Nothing, for `Set`. Quarry generates the entity, so `Set(u => u.UserName = name)` against a fresh instance and reading `UserName` back recovers the value with no closure involved. Breaks only on non-property assignment targets or side-effecting setters |
| G | **Recording proxy for `Where(Func<T,bool>)`** | Partially — **prototyped, see below** | every entity property becomes a symbolic wrapper type (or predicates take a parallel `User.Filter` type); a probe invocation per execution | Silently wrong for `?:`, `if`/block bodies, and any `&&`/`\|\|` whose left operand is a plain `bool` |
| H | **Get the value in at the call site** | **No** | — | An interceptor replaces a *call expression* only; it cannot introduce locals into the enclosing method, and the argument is already a delegate. Unity DOTS does this (`ExecuteMethodWriter` emits `{jobField} = {originalLocalName}` in the enclosing scope) precisely because it is a full syntax rewriter, not an interceptor |
| I | **Post-compile IL rewriting** (Mono.Cecil/Fody: read the real display class names, patch the `UnsafeAccessorType` CA blobs) | Technically yes | a weaving step in every consumer's build; breaks incrementality, deterministic builds, signing order, and IDE inner loop | Turns a naming risk into a build-pipeline dependency. No .NET ORM does this |
| J | **Struct `IFunction<T,bool>` predicates** (StructLinq / Hyperlinq / LinqGen pattern) | Yes | `readonly struct MinIdPredicate : IPredicate<User>` instead of a lambda | Kills the `u => u.Id > minId` ergonomic entirely |

### G in detail — VERIFIED prototype

The recording-proxy idea *does* recover captured **values**, because a captured local arrives as an
ordinary argument to an overloaded operator. Running a `Func<ProbeUser,bool>` against an entity whose
properties are symbolic wrappers (`minId=42`, `hi=99`, `nm="bob"`):

```
simple >                    -> [UserId > 42]
&& two columns              -> [UserId > 42 | [op_false] | Age < 99 | AND]
|| two columns              -> [UserId > 42 | [op_true]  | Age < 99 | OR]
! negation                  -> [UserId > 42 | NOT]
string equality             -> [UserName = 'bob']
method call                 -> [UserName LIKE 'bob%']
captured expr result        -> [UserId > 141]
```

`&&` and `||` work: defining `operator true`/`operator false` to always return `false` makes the
compiler's short-circuit test never short-circuit, so **both** operands are evaluated and recorded.

The failures are the problem, because they are **silent**:

```
ternary                     -> [UserId > 42 | Age < 99]      <- only the taken branch; other term lost
block body + if             -> [UserId > 42 | Age < 99]      <- same
bool local short-circ       -> [Age < 99]                    <- `minId > 0` is a plain bool; never recorded
null-check on entity        -> [UserId > 42]                 <- `u != null` is reference comparison
nondeterministic            -> [UserId > 9]                  <- DateTime.Now.Second; different every call
```

A wrong *parameter value* silently sent to the database is strictly worse than a `TypeLoadException`.
This is survivable only if the compile-time SQL translator's accepted grammar is a **subset** of what
the proxy can faithfully replay — and Quarry's `SqlExprParser` does handle `ConditionalExpressionSyntax`
(`src/Quarry.Generator/IR/SqlExprParser.cs:105`), so today it is not. The scheme also requires either
changing every entity property's type or introducing a parallel filter type in `Where`'s signature.

### A in detail — VERIFIED prototype, and it works on the real repo

A ~150-line `System.Reflection.Metadata` reader run against the current `Quarry.Tests.dll`:

```
=== …/bin/Debug/net10.0/Quarry.Tests.dll ===
  type defs: 6372   display classes: 531
  [UnsafeAccessorType] strings: 667 (168 distinct)
  --> unresolvable UnsafeAccessorType strings: 0
  --> field-level checks: 667, mismatches: 0

=== …/bin/Release/net10.0/Quarry.Tests.dll ===
  type defs: 6368   display classes: 527
  [UnsafeAccessorType] strings: 667 (168 distinct)
  --> unresolvable UnsafeAccessorType strings: 0
  --> field-level checks: 667, mismatches: 0
```

Clean today, as expected — the risky fixture was reverted in `502396c`. Negative test, an assembly
deliberately built with one wrong ordinal and one wrong field name:

```
Good     = 7
WrongOrd threw TypeLoadException: Could not resolve type 'Repo+<>c__DisplayClass1_7' …
WrongFld threw MissingFieldException: Field not found: '<>c__DisplayClass1_0.nosuchfield'.

===== verifier =====
    MISSING  Repo+<>c__DisplayClass1_7   (used 1x, e.g. param t)
  --> unresolvable UnsafeAccessorType strings: 1
    FIELD MISSING  Repo+<>c__DisplayClass1_0 has no field 'nosuchfield'
  --> field-level checks: 3, mismatches: 1
```

Both failure classes caught, at build time, from metadata alone. Runtime **0.72 s** including process
start on the 6 372-type assembly.

Two implementation notes for whoever builds it:
- Read the attribute blob directly (`prolog uint16`, then `ReadSerializedString`). The `Name=` on
  `UnsafeAccessorAttribute` is a *named* argument after one `int32` fixed argument.
- Metadata type names use `+` for nesting and the namespace lives on the outermost type only, which is
  already the form `CarrierEmitter` emits.

---

## 5. How other projects solve it

**Nobody solves it the way Quarry does.** GitHub code search for `UnsafeAccessorType` + `DisplayClass`
returns exactly one non-trivial repository: `Dtronix/Quarry`. Searching `"c__DisplayClass"
UnsafeAccessor` returns only Quarry. There is no blog post, library, or runtime sample doing this.

**No .NET ORM translates a plain `Func<T,bool>` to SQL.** Surveyed and verified by reading source:

| Project | Strategy |
|---|---|
| **Dapper.AOT** (interceptors) | Explicit parameters. Repo-wide grep for `DisplayClass`, `closure`, `Delegate.Target`, `Expression<Func` → zero hits each |
| **ASP.NET Core RDG** (largest production interceptor generator) | Just *invokes* the delegate. `Cast<T>(Delegate d, T _) where T : Delegate => (T)d;` |
| **EF Core** | `Expression<>` only; no `Func<T,bool>` overload exists |
| **linq2db**, **RepoDb**, **OrmLite**, **sqlite-net**, **Dommel**, **FreeSql**, **SqlSugar** | `Expression<>` + reflection (`FieldInfo.GetValue` on the `ConstantExpression.Value`, which *is* the display class), and most also `Compile()`/`DynamicInvoke()` |
| **nanorm**, **Norm.net**, **SqlMarshal** | Explicit parameters / interpolated-string handlers. `nanorm`'s `$"… WHERE Title = {title}"` gets the local's value through `AppendFormatted` with no display class and no expression tree |
| **LinqGen / StructLinq / Hyperlinq / ZLinq / Cistern.Linq** | LINQ-to-objects only; all recommend struct value-delegates over `Func<>` |
| **DelegateDecompiler** (both forks) | The *only* library that reads captured state from a `Func<>`. `Expression.Constant(@delegate.Target)` then decompiles IL via `MethodBase.GetMethodBody()` — AOT-fatal (IL2026) |

**The closest real precedent is EF Core 9/10 precompiled queries** — the only production .NET system
that extracts captured-variable values at runtime from generated interceptor code.
`PrecompiledQueryCodeGenerator.GenerateCapturedVariableExtractors()` emits:

```csharp
queryContext.Parameters.Add("__minId_0",
    Expression.Lambda<Func<object?>>(Expression.Convert(<path>, typeof(object)))
        .Compile(preferInterpretation: true).Invoke());
```

where `<path>` is a walk over the **runtime `Expression` object** —
`((MemberExpression)((BinaryExpression)((LambdaExpression)predicate).Body).Right)`. EF never names a
display class and never predicts anything; the `FieldInfo` was emitted by Roslyn at the call site. It
identifies a display class *structurally*, not by name
(`ExpressionTreeFuncletizer.VisitConstant`):

```csharp
var isCapturedVariable =
    (constant.Type.Attributes.HasFlag(TypeAttributes.NestedPrivate)
        && Attribute.IsDefined(constant.Type, typeof(CompilerGeneratedAttribute), inherit: true))
    || constant.Type == typeof(ValueBuffer);
```

The entire difference is one API decision: `Expression<>` vs `Func<>`.

**Where the `[UnsafeAccessor]` name-guessing pattern *does* have precedent** is
`<Prop>k__BackingField` — protobuf-net (`Getter.output.cs`), TUnit
(`TestMetadataGenerator.cs`: `$"<{property.Name}>k__BackingField"`), EF Core's
`LinqToCSharpSyntaxTranslator.GetUnsafeAccessorName`, and dotnet/runtime's own
`docs/design/features/unsafeaccessors.md`. That is a **pure function of a name you already have**.
`<>c__DisplayClassN_M` is a function of two ordinals reconstructed from compilation state you cannot
see. Same mechanism, different risk class.

**DI containers, serializers, mocking libs:** none crack open closures. Pure.DI transplants the
lambda's *syntax* into generated code (`FactoryRewriter : CSharpSyntaxRewriter`) so no delegate is ever
formed. Jab makes lambda factories syntactically impossible. Rocks (AOT-safe mocking) takes
`Predicate<T>` and simply calls it — it never leaves the process, which is exactly the axis on which
Quarry differs.

---

## 6. Recommendation, ranked

### 1 — Ship the post-compile verifier (alternative A). Do this first, unconditionally.

It is the only option that removes the *class* of failure rather than one instance. It converts every
wrong guess — from any cause: Release merging, an added field shifting `methodOrdinal`, a future
compiler change, a partial-class reordering — into a build error naming the exact accessor. It is
prototyped, it runs in 0.72 s on the largest assembly in the repo, it needs only
`System.Reflection.Metadata` (in-box), and it has no runtime cost, no API change, and no false
positives. Wire it as an MSBuild target after `CoreCompile` in `Quarry.Generator`'s shipped
`build/*.targets`, so **consumers** get it too — they are the ones compiling with their own SDK and
their own configuration.

Reporting quality matters here: the message should name the clause's source location (the generator
already has it) and say "predicted `X`, assembly contains `Y`, `Z`" — because #344 cost a full CI
cycle to diagnose from a bare `TypeLoadException`.

### 2 — Re-diagnose #344 as an `<Optimize>` bug, and add the configuration axis to CI.

The evidence in §3 says the SDK framing is very likely wrong. Concretely:
- Update `_issue-version.md` / #344 with the `MergeEnvironments()` mechanism and the reproduction.
  Describe the axis as **`<Optimize>`**, not "Debug vs Release" — the gate reads
  `OptimizationLevel`, which is `/optimize±`, not the configuration name (§3a).
- Do **not** add `global.json` as a mitigation — §3d shows it would not have helped.
- Add a non-optimized leg to CI, or at minimum run the `LambdaCaptureExecutionTests` suite with both
  `<Optimize>` settings. Every one of those tests compiles either way; only *execution* distinguishes
  them, and today only one setting is executed.
- The generator can read `Compilation.Options.OptimizationLevel` — so if prediction is kept, the
  scope-merging rules could at least be *modelled* per optimization level rather than ignored. That is
  a hardening idea, not a fix: `MergeEnvironments` depends on the capturing-closure set and on
  backward `goto` analysis over the bound tree, which a syntactic walk cannot reproduce faithfully.

### 3 — Add the runtime self-check (alternative B) as defence in depth for consumers.

The verifier covers builds that run it. A consumer who disables the target, or a scenario the verifier
does not see, still benefits from a legible exception. Cheap, once per call site.

### 4 — Offer alternative D (`Where((u, p) => …, minId)`) as a documented escape hatch.

Not as a replacement for the current API — as the thing the QRY diagnostic tells people to use when a
capture is rejected. It is the only alternative that removes the problem rather than detecting it: a
non-capturing lambda gets `Static`/`Singleton` closure kind and **no display class is emitted at all**.
VERIFIED: `Use(u => u > Seed)` (capturing only `this`) emitted zero display classes —
`InlineThisOnlyEnvironments()` lowers it straight onto the containing type.

### 5 — Do *not* pursue E, G, H, or I.

- **E (`Expression<>`)** costs a tree allocation per call and still needs reflection at the leaf. It
  trades Quarry's central value proposition for EF Core's design.
- **G (recording proxy)** fails silently on `?:`, block bodies, and plain-`bool` short-circuits, and
  needs the entity surface rebuilt. Silent wrong parameters are worse than a loud crash.
- **H** is structurally impossible for interceptors.
- **I (IL rewriting)** trades a naming risk for a build-pipeline dependency in every consumer's build.

### "Do nothing but improve diagnostics" — is it the best available option?

Close, and it is essentially recommendation 1 + 3. There is no mechanism that makes the guess correct,
because the information does not exist at generator time. But "improve diagnostics" undersells the
verifier: a build-time check that reads the actual emitted metadata is not a better error message, it
is a **completeness proof** for the guessing scheme on that particular build. That is a materially
stronger position than Quarry is in today, and it is the only one available without changing the API.

---

## 7. What I could not determine

- **Whether SDK 10.0.110 and 10.0.302 genuinely differ.** I only have 10.0.110 installed, and NuGet
  `Microsoft.CodeAnalysis.CSharp` 4.5.0/4.8.0 would not resolve alongside `Basic.Reference.Assemblies`
  (NU1107). I tested 4.11.0 / 4.14.0 / 5.0.0 and found **zero** divergence across 25 shapes, and I
  reproduced the reported symptom exactly from the Debug/Release axis alone — but I have not *proved*
  the two SDKs agree. Someone with both installed should run the §3c repro under each, in each
  configuration. Until then, treat "SDK-dependent" as unconfirmed and "configuration-dependent" as
  confirmed.
- **Whether the real `ConcurrencyTests` numbers were exactly `_3`→`_1`.** My reproduction uses
  same-shaped stubs and yields `_3`→(`name` on `_2`). The mechanism is identical; the exact ordinals
  depend on details of the real fixture (`User.Patch`, `DisposeHarnessesAsync`) I did not replicate
  byte-for-byte.
- **Hot Reload.** Resolved partly: `AppendOptionalGeneration` only appends the `#N` generation suffix
  when `generation > 0`, and on a fresh compile `CurrentGenerationOrdinal` is 0 and `_slotAllocator`
  is null — so `#N` can never appear in a normal build, and Quarry's strings are safe from it. But an
  EnC/Hot Reload *delta* does carry a generation, and `EncVariableSlotAllocator.TryGetPreviousClosure`
  reuses prior ordinals keyed on syntax offset. I did **not** test whether a Quarry chain survives a
  Hot Reload session, and I would expect renamed frames (`…_0#1`) to break every accessor in the
  edited method. Worth an explicit experiment before anyone relies on Hot Reload with Quarry.
- **Struct closure environments.** `SynthesizedClosureEnvironment.cs` sets
  `TypeKind = isStruct ? TypeKind.Struct : TypeKind.Class`, and `[UnsafeAccessorType]` rejects value
  types (`if (replacementType.IsValueType) return SetTargetResult.NotSupported;`). I did not construct
  a case where Quarry would hit one — every shape I tried that involves a lambda produced a class. If
  a shape exists where a Quarry clause's target is a struct environment, it is unreachable by any
  mechanism, and I have not ruled it out.
- **Whether `GetMembers()` ordering is stable across generator-driven partial types.** #310 already
  tracks this. I confirmed the *mechanism* (`memberOrdinal` is the `GetMembers()` index) but did not
  test what happens when a second source generator contributes members to the same partial type, nor
  whether generator execution order is deterministic across runs.
- **Whether an internal Roslyn API is reachable in practice.** `GeneratedNames`,
  `SynthesizedClosureEnvironment` and `SynthesizedClosureMethod` are all `NotPublic`. I did not
  investigate private reflection into the compiler host from inside a generator — it would be
  fragile, would break under any Roslyn version bump, and would still not work because the *ordinals*
  are only computed during `Emit`, after the generator has returned. I am confident this is a dead end
  but did not attempt it.
- **Whether Quarry's `SqlExprParser` grammar could be narrowed to the recording-proxy-faithful
  subset.** I established that it currently handles `ConditionalExpressionSyntax`, which the proxy
  cannot replay faithfully. I did not survey how much real usage depends on that.

---

## Appendix — how to rebuild the experiments

All experiment sources were written under the session scratchpad and are not part of the repo.
Each is a small `net10.0` console app:

| Experiment | What it does |
|---|---|
| `probe` | Compiles a source string with a chosen `Microsoft.CodeAnalysis.CSharp` version (`-p:RoslynVersion=…`); dumps all symbols, lambda `IMethodSymbol`s, `DataFlowAnalysis`, `ControlFlowGraph` captures, the public/non-public API surface matching closure concepts, the emitted display classes via `PEReader`, and `GetMembers()` ordering |
| `matrix` | 25 source shapes × {Debug, Release} × {4.11, 4.14, 5.0}; prints `<>c__DisplayClassM_N{fields}` per shape for diffing |
| `predict` | Loads `Quarry.Generator.dll` by reflection, calls `DisplayClassNameResolver.AnalyzeMethodClosures` / `LookupClosureOrdinal` / `CountCaptureScopes` / `ComputeMethodOrdinal`, and prints prediction beside the emitted ground truth. Includes the `ConcurrencyTests` repro |
| `alt` | The recording-proxy prototype (`SqlInt`/`SqlStr`/`SqlBool` with overloaded operators and `op_true`/`op_false`) |
| `verify` | The post-compile verifier prototype — reads `[UnsafeAccessorType]` strings and `[UnsafeAccessor(Name=…)]` from a PE and checks both against real typedefs and fields |
| `bad` | Negative-test assembly with one wrong ordinal and one wrong field name |
