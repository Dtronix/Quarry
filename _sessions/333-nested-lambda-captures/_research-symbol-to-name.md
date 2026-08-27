# Research: is there a `symbol → emitted metadata name` API in Roslyn?

**Question.** Our generator emits `[UnsafeAccessor]` externs that read captured variables out of
compiler-generated display classes, and it names the display class by *guessing* a string
(`MyNs.Repo+<>c__DisplayClass3_1`). Is there any shipped or reachable Roslyn API that gives a
captured local's **emitted storage identity** starting **from the symbol**?

Two issues — **dotnet/roslyn#11565** and **dotnet/roslyn#55651** — had been cited in earlier notes
as evidence that "a public API for this was asked for and refused". This document tests that claim,
surveys the symbol→name direction, enumerates every related issue found, and gives a verdict.

Evidence is tagged:

- **[RUN]** — verified by executing code in this session; real output pasted.
- **[SRC]** — read in Roslyn source or in a spec/doc; path or URL cited.
- **[INF]** — inferred from the above, not directly executed.

Empirical work used a `net10.0` console app referencing `Microsoft.CodeAnalysis.CSharp` **4.14.0**
and `Microsoft.CodeAnalysis.CSharp.Workspaces` **4.14.0** (scratch, since deleted). Public-API
`.txt` claims were cross-checked against `dotnet/roslyn` `main` via GitHub code search.

Four facts established in earlier sessions are taken as given and are **not** re-derived here:
display classes do not exist in the `Compilation` before `Emit`; no public Roslyn type exposes
closure environments; `ControlFlowGraph`/`IFlowCaptureOperation` is unrelated to closures; and for a
captured local every shipped `SymbolDisplayFormat` returns just the bare identifier.

---

## 1. The two cited issues — corrected characterisation

**The earlier characterisation was wrong, and the hypothesis under test is correct.** Neither issue
asks for symbol → emitted name. Both ask for the *opposite* direction: given a mangled string that
already exists, recover the original source name. Neither is evidence that our direction was
refused, because **our direction was never requested in either issue.**

### dotnet/roslyn#11565 — "Provide a public API to parse generated names"

| Field | Value |
| --- | --- |
| State | **CLOSED**, `stateReason = NOT_PLANNED` |
| Opened | 2016-05-25 by `pharring` (Paul Harrington) |
| Closed | 2022-10-28 |
| Labels | `Concept-API`, `Area-Compilers`, `Feature Request` |
| Direction | **name → source name** (parsing) |

Opening text, verbatim **[SRC, `gh issue view 11565`]**:

> I'd like to have a public API to **parse** compiler generated names, such as
> `PlatformBlobStore+<>c__DisplayClass52_0+<<TryReferenceInternalAsync>b__0>d.MoveNext` so that I
> can build more user-friendly names.
>
> It would appear that [`GeneratedNames.TryParseGeneratedName`] is a good start.

The cited helper is the **parser** half of Roslyn's name machinery, not the **maker** half. The
scenarios listed are all display/formatting: PerfView stack frames, the xUnit runner's async
stacks, scripting, Xamarin Workbooks. Every one starts from a string that already exists in a
stack trace.

Only substantive maintainer comment, verbatim **[SRC]**:

> **CyrusNajmabadi:** This would need to go through an API proposal. in general, the reason we
> don't just make things public is because it increases maintenance costs and locks down our
> ability to change things in the future. We can open things up, but we have a process that
> requests need to go through to make sure the appropriate people weigh in and the right API shape
> is determined and shipped.

Note what this is and is not. It is a *process* objection ("go through an API proposal"), not a
refusal on the merits, and it is explicitly open to the idea ("We can open things up"). The
`NOT_PLANNED` close six years later is a triage close with no accompanying rationale. Reading
#11565 as "Roslyn refused to expose naming" overstates it twice over: wrong direction, and not a
refusal.

### dotnet/roslyn#55651 — "Support retrieving original type name from mangled type name"

| Field | Value |
| --- | --- |
| State | **OPEN** (still open as of this research) |
| Opened | 2021-08-16 by `captainsafia` (Safia Abdalla) |
| Closed | — |
| Labels | `Area-Compilers`, `Feature Request` |
| Direction | **name → source name** (the title says so outright) |

Motivation, verbatim **[SRC, `gh issue view 55651`]**:

> However, because `Greeting` is compiler generated, the computed type name is mangled as
> `<Program>$.<<Main>$>g__Greeting|0_0()`. Which makes it impossible to produce the desired
> metadata at runtime.

The proposed API is `CompilerGeneratedAttribute.OriginalName` — a *runtime reflection* affordance
for un-mangling, not a compile-time naming API.

`tmat` (Tomáš Matoušek), maintainer, verbatim **[SRC]**:

> I don't think we should emit attribute with information that is already in the metadata. That
> would just increase the size of the generated assemblies.
>
> Instead Roslyn can publish a source NuGet package that provides APIs for **parsing** mangled
> names (as listed in the alternative designs above). This package would contain
> [`GeneratedNameParser`] and a couple of dependent types.

and:

> Seems like instead of bloating the metadata we can just enable **reading** of the existing data.

**That package was never shipped [RUN].** A NuGet search for `GeneratedNameParser` returns no
Roslyn package (`https://azuresearch-usnc.nuget.org/query?q=GeneratedNameParser` — seven unrelated
hits, none from Microsoft). `GeneratedNameParser` remains `internal` in
`src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNameParser.cs`.

### The one place the *right* direction appears in #55651

The issue's own "Alternative Designs" section lists, verbatim **[SRC]**:

> - Expose an API that allows end-users to **derive how a mangled type is produced given an
>   original name**. Allows users to make the mangled name -> original name mapping themselves.

This is the closest anything in either issue comes to what we need — and even this is framed as a
*means to invert* the mapping, not as a compile-time naming contract. **No maintainer replied to
it.** It was never proposed, never triaged, never refused.

### Verdict on the two issues

| | #11565 | #55651 |
| --- | --- | --- |
| Direction actually requested | name → source name | name → source name |
| Direction we need (symbol → emitted name) | not requested | mentioned once in "Alternatives", never discussed |
| Was our direction refused? | **No** | **No** |
| Was *anything* refused on the merits? | No — closed `NOT_PLANNED` at triage, after a process ("needs an API proposal") comment | Not refused; still open, redirected to a parser package that was never shipped |

The honest statement is: **the symbol → emitted-name direction has essentially never been formally
requested in `dotnet/roslyn`, so there is no maintainer refusal to point at.** The absence of the
API is an absence of a proposal, not the outcome of one. (Section 3 finds the single partial
exception, #50978, which *did* ask for emitted names and *did* draw a real maintainer objection.)

---

## 2. The symbol → name survey

All rows below concern a **captured local** (`minId`) and a **lambda** in:

```csharp
namespace MyNs {
  public class Repo {
    private int _instanceField = 1;
    public void Query(int paramValue) {
      var minId = 5;
      Func<int,bool> f = u => u > minId && u > paramValue && u > _instanceField;
    }
  }
}
```

which, once emitted, becomes `Repo+<>c__DisplayClass1_0` with fields `minId`, `paramValue`,
`<>4__this` and method `<Query>b__0` **[RUN]**.

### 2.1 Survey table

| Candidate | What it does | Public / shipped? | Captured local | Lambda | Yields emitted identity? |
| --- | --- | --- | --- | --- | --- |
| `ISymbol.MetadataName` | Name as it would appear in metadata *for symbols that reach metadata* | Public, shipped | `"minId"` | `""` (empty) | **No** — returns the source identifier; neither symbol reaches metadata as itself |
| `ISymbol.ToDisplayString` (all shipped formats) | Human-readable rendering | Public, shipped | `"minId"` even under `FullyQualifiedFormat` | `"lambda expression"` | **No** (established previously; re-confirmed) |
| `ISymbol.ToMinimalDisplayString` | Context-minimised rendering | Public, shipped | `"int minId"` | `"lambda expression"` | **No** — strictly *less* qualified than `ToDisplayString` |
| `ISymbol.GetDocumentationCommentId()` | Doc-comment ID | Public, shipped | `null` | `"M:MyNs.Repo.(System.Int32)"` (degenerate — empty method name) | **No** |
| `DocumentationCommentId.CreateDeclarationId` | Doc ID from symbol | Public, shipped | `null` | `"M:MyNs.Repo.Query(System.Int32).(System.Int32)~System.Boolean"` | **No** — a *source*-shaped ID; no metadata name in it |
| `DocumentationCommentId.CreateReferenceId` | Reference-form doc ID | Public, shipped | `""` (empty string) | `""` (empty string) | **No** |
| `DocumentationCommentId.GetSymbolsForDeclarationId` / `GetFirstSymbolForDeclarationId` | **name → symbol.** The reverse half of the pair | Public, shipped | n/a | n/a | **No, and wrong direction.** Resolves an ID against a `Compilation`; returns `null` for any display-class name |
| **`Microsoft.CodeAnalysis.SymbolKey`** | Round-trip a symbol to a durable string and back | **Internal** — `IsNotPublic = true`, in `Microsoft.CodeAnalysis.Workspaces.dll`; 0 hits in every `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` in the repo | Handled (`SymbolKeyType.BodyLevel`) | Handled (`SymbolKeyType.AnonymousFunctionOrDelegate`) | **No** — encodes *source spans*, not emitted names. See §2.3 |
| `IMethodSymbol.PartialDefinitionPart` / `PartialImplementationPart` | Partial-method pairing | Public, shipped | n/a | `null` for both, on the lambda and on a local function | **No** — unrelated to lowering |
| `ISymbol.OriginalDefinition` | Uninstantiated definition | Public, shipped | `ReferenceEquals(sym, sym.OriginalDefinition) == true` | same | **No** — self for both; only strips generic substitution |
| `ISymbol.Accept(SymbolVisitor)` | Double dispatch | Public, shipped | dispatches to `VisitLocal` | dispatches to `VisitMethod` | **No** — dispatch mechanism, carries no extra data |
| `Microsoft.CodeAnalysis.CSharp.Symbols.*` | 578 types, **0 public** | All internal | — | — | Includes `SynthesizedClosureEnvironment`, `SynthesizedClosureMethod`, `GeneratedNames`, `GeneratedNameParser` — all internal |
| `Microsoft.CodeAnalysis.Emit.*` | 83 types, **11 public** | See §2.4 | — | — | The 11 public ones are options/results/EnC-edit types. `EmitContext` is internal |
| `Microsoft.CodeAnalysis.CodeGen.*` | 102 types, **0 public** | All internal | — | — | `ClosureDebugInfo`, `LambdaDebugInfo`, `VariableSlotAllocator` all live here |
| `Microsoft.Cci.*` | **0 types** in the public reflection surface | Internal | — | — | Cci types are not visible at all from a referencing assembly |
| `PEModuleBuilder.Translate(...)` | Symbol → Cci reference (the real projection) | `PEModuleBuilder` `IsPublic = false`; every `Translate` overload `IsPublic = false` | — | — | This *is* symbol→emitted-form, but entirely internal and requires an in-flight `EmitContext` (§2.4) |
| Portable PDB `LocalScope` / `LocalVariable` | Slot → name for **method-body locals** | Public reader (`System.Reflection.Metadata`) | **Absent — the captured local is not in the table** | n/a | **No** for captured locals. See §2.5 |
| Portable PDB `EncLambdaAndClosureMap` CDI | Method/closure/lambda ordinals | Blob is readable via public `MetadataReader`; the *decoder* is internal | Indirect | Indirect | Closest machine-readable closure-ordinal source — but debug-only and post-emit. See §2.5 |
| `ISymUnmanagedReader` | Windows-PDB equivalent | COM interop, legacy | same limits | same limits | **No** — same phase problem, worse format |
| `EmitBaseline.SynthesizedMembers` / `SynthesizedTypes` | **A real symbol → synthesized-members map** | `EmitBaseline` is public; **these two members are internal** | — | — | See §2.6 — the single genuine near-miss |

### 2.2 Raw output — `ISymbol` surface **[RUN]**

```
--- Local 'minId'  (CLR type LocalSymbol)
  Name                     : 'minId'
  MetadataName             : 'minId'
  MetadataToken            : 0
  IsImplicitlyDeclared     : False
  ContainingSymbol         : Method MyNs.Repo.Query(int)
  ToDisplayString(Fully..) : 'minId'
  ToMinimalDisplayString   : 'int minId'

--- Method ''  (the lambda)
  Name                     : ''
  MetadataName             : ''
  MetadataToken            : 0
  ContainingSymbol         : Method MyNs.Repo.Query(int)
  ToString()               : 'lambda expression'
  ToDisplayString(Fully..) : 'lambda expression'
```

`MetadataToken == 0` on both is worth stating plainly: these symbols have **no metadata token**,
because they are not metadata entities. `MetadataName` on a captured local is not "the name it will
have in metadata" — it is just `Name`, and nothing more.

Doc-comment IDs **[RUN]**:

```
--- Local 'minId'
  GetDocumentationCommentId()               : (null)
  DocumentationCommentId.CreateDeclarationId: (null)
  DocumentationCommentId.CreateReferenceId  :
--- Method '' (lambda)
  GetDocumentationCommentId()               : M:MyNs.Repo.(System.Int32)
  DocumentationCommentId.CreateDeclarationId: M:MyNs.Repo.Query(System.Int32).(System.Int32)~System.Boolean
```

Reverse direction, establishing which half serves which **[RUN]**:

```
  CreateDeclarationId(MyNs.Repo)                 = T:MyNs.Repo          <- symbol -> id
  GetFirstSymbolForDeclarationId("T:MyNs.Repo")  -> NamedType MyNs.Repo <- id -> symbol
  GetFirstSymbolForDeclarationId("T:MyNs.Repo.<>c__DisplayClass1_0"): (null)
  GetFirstSymbolForDeclarationId("T:MyNs.Repo+<>c__DisplayClass1_0"): (null)
```

So the pair is: `CreateDeclarationId` = symbol→name, `GetSymbolsForDeclarationId` /
`GetFirstSymbolForDeclarationId` = name→symbol. Both halves are public. **Neither half touches
emitted names**, and the name→symbol half cannot resolve a display class in either separator
spelling — because (established fact) the display class does not exist in the `Compilation`.

### 2.3 `SymbolKey` in depth

This deserved real attention as "the closest thing to symbol → durable identity". It is not what we
need, for three independent reasons.

**(a) It is internal. [RUN]**

```
Microsoft.CodeAnalysis (4.14.0.0):          SymbolKey type = NOT FOUND
Microsoft.CodeAnalysis.CSharp (4.14.0.0):   SymbolKey type = NOT FOUND
Microsoft.CodeAnalysis.Workspaces (4.14.0.0): SymbolKey type = FOUND,
    IsPublic=False, IsNotPublic=True, Visibility=NotPublic
```

Cross-checked on `main` **[RUN]** — `SymbolKey` appears **0 times** across all
`PublicAPI.Shipped.txt` and **0 times** across all `PublicAPI.Unshipped.txt` in `dotnet/roslyn`
(GitHub code search). It has been internal for its entire life. `Microsoft.CodeAnalysis.Workspaces`
is also not a dependency a source generator can take — generators reference
`Microsoft.CodeAnalysis.CSharp` only, and loading Workspaces into the compiler's ALC is not
supported.

**(b) It does handle locals and lambdas — but encodes source positions, not emitted names. [RUN]**

The `SymbolKeyType` enum includes `BodyLevel` and `AnonymousFunctionOrDelegate`, and `CanCreate`
returns `True` for every symbol we tried:

```
[Local 'minId']       CanCreate = True
  7 "C#" (B "minId" 8 (% 2  1 "" 168 5  1 "" 168 9) (M "Query" (D (N "MyNs" ...)) ...) 0 0)
[Method ''] (lambda)  CanCreate = True
  7 "C#" (Z 0  1 "" 210 54 0)
[Method 'Local']      CanCreate = True
  7 "C#" (B "Local" 9 (% 2  1 "" 283 5  1 "" 278 42) (M "Query" ...) 0 0)
```

`B` = BodyLevel, `Z` = AnonymousFunctionOrDelegate. The numbers `168 5`, `210 54`, `283 5` are
**source text span start/length**. Scanning all six keys **[RUN]**:

```
contains '<>c' : False   contains 'DisplayClass': False   len=260   (minId)
contains '<>c' : False   contains 'DisplayClass': False   len=233   (paramValue)
contains '<>c' : False   contains 'DisplayClass': False   len=27    (lambda)
```

Roslyn source agrees **[SRC,
`src/Workspaces/SharedUtilitiesAndExtensions/Compiler/Core/SymbolKey/SymbolKey.BodyLevelSymbolKey.cs`]**:
`Create` writes the symbol name, the kind as an integer, "locations for precision", the containing
symbol key, and "ordinal for resilience". The design comment reads:

> Store the body level symbol in two forms. The first, a highly precise form … The second, in a
> more query-oriented form that can allow the symbol to be found in some cases even if the solution
> changed.

Nothing about metadata or display classes.

**(c) It is not stable, and Roslyn says so. [SRC + RUN]**

Type doc **[SRC, `SymbolKey.cs`]** — note the declared accessibility is `internal`:

> A SymbolKey is a lightweight identifier for a symbol that can be used to resolve the "same"
> symbol across compilations.

> SymbolKeys are not guaranteed to work across different versions of Roslyn. They can be persisted
> in their `ToString()` form and used across sessions with the same version of Roslyn. However,
> future versions may change the encoded format and may no longer be able to Resolve previous keys.

> Interior-method-level symbols (i.e. `ILabelSymbol`, `ILocalSymbol`, `IRangeVariableSymbol` and
> `MethodKind.LocalFunction` `IMethodSymbol`s) can also be represented and restored in a different
> compilation.

> Symbol keys cannot be created for interior-method symbols that were created in a speculative
> semantic model.

Empirically, inserting **one unrelated statement earlier in the same method** changes the key text
for every body-level symbol, and breaks lambda resolution outright **[RUN]**:

```
=== Key strings for the same symbols computed from comp2 (one statement inserted) ===
  [Local 'minId']            DIFFERENT   (span 168 -> 230)
  [Parameter 'paramValue']   IDENTICAL
  [Method ''] (lambda)       DIFFERENT   (Z 0 1 "" 210 54 0  ->  Z 0 1 "" 272 54 0)
  [Method 'Local']           DIFFERENT   (span 283 -> 345)
  [Field '_instanceField']   IDENTICAL
  [NamedType 'Repo']         IDENTICAL

=== Does the comp1 key resolve in comp2? ===
  [Local 'minId']       -> Symbol=Local minId          (recovered via the ordinal fallback)
  [Method ''] (lambda)  -> Symbol=(null)               <-- FAILS
  [Method 'Local']      -> Symbol=Method Local
```

Two things follow. The BodyLevel "resilience" ordinal does rescue the local across a trivial edit;
the `AnonymousFunctionOrDelegate` key has no such fallback and simply fails. And **a SymbolKey is
strictly source-shaped** — it is designed for IDE features (find-all-references, rename, code
lenses) that need to re-find a symbol across snapshots of the *same* source, not for anything about
the emitted assembly.

There is a directly relevant issue here, covered in §3: **#27527 "Make SymbolKey API public"**,
opened 2018-06-06 by CyrusNajmabadi himself, **closed NOT_PLANNED 2022-10-31** — three days after
#11565 was closed the same way. Even if it had shipped, it would not answer our question.

### 2.4 Emit / Cci / `PEModuleBuilder.Translate` **[RUN]**

```
Microsoft.CodeAnalysis.Emit in Microsoft.CodeAnalysis: 83 types, 11 public
    DebugInformationFormat, EditAndContinueMethodDebugInformation, EmitBaseline,
    EmitDifferenceResult, EmitOptions, EmitResult, InstrumentationKind,
    MethodInstrumentation, RuntimeRudeEdit, SemanticEdit, SemanticEditKind

Microsoft.CodeAnalysis.CodeGen:              102 types, 0 public
Microsoft.CodeAnalysis.CSharp.Symbols:       578 types, 0 public
Microsoft.Cci:                                 0 types, 0 public

Microsoft.CodeAnalysis.Emit.EmitContext:            found, IsPublic=False
Microsoft.CodeAnalysis.CSharp.Emit.PEModuleBuilder: found, IsPublic=False
    IsPublic=False  Cci.INamedTypeReference Translate(NamedTypeSymbol, SyntaxNode, DiagnosticBag, bool, bool)
    IsPublic=False  Cci.IFieldReference    Translate(FieldSymbol, SyntaxNode, DiagnosticBag, bool)
    IsPublic=False  Cci.IMethodReference   Translate(MethodSymbol, DiagnosticBag, bool)
```

`PEModuleBuilder.Translate` **is** the symbol → emitted-form projection. It is internal on an
internal type, and — critically — it operates on `NamedTypeSymbol` / `FieldSymbol`, i.e. on the
*synthesized* symbols that lowering has already created. Even with unrestricted reflection you
could not call it usefully from a generator, because the `SynthesizedClosureEnvironment` you would
need to pass in does not exist yet at generator time (established fact). `Microsoft.Cci` is not
merely internal — **zero Cci types are visible in the reflection surface at all**, so the return
types are unusable.

Also confirmed **[RUN]** — the entire public surface of both compiler assemblies contains **no**
member whose name suggests emitted naming:

```
Public members mentioning 'MetadataName' / 'EmittedName' / 'MangledName':
    Compilation.GetTypeByMetadataName        (name -> symbol)
    Compilation.GetTypesByMetadataName       (name -> symbol)
    IAssemblySymbol.GetTypeByMetadataName    (name -> symbol)
    SyntaxValueProvider.ForAttributeWithMetadataName (name -> syntax)
    ISymbol.MetadataName                     (the property surveyed above)
```

Every one of the lookup APIs is **name → symbol**. That is the shape Roslyn's public surface has;
the inverse for synthesized entities simply is not modelled.

And the naming machinery itself **[RUN]**:

```
IsPublic=False  Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNames
IsPublic=False  Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameParser
IsPublic=False  Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind
IsPublic=False  Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameConstants
IsPublic=False  Microsoft.CodeAnalysis.CSharp.SynthesizedClosureEnvironment
IsPublic=False  Microsoft.CodeAnalysis.CSharp.SynthesizedClosureMethod
IsPublic=False  Microsoft.CodeAnalysis.CodeGen.LambdaDebugInfo
IsPublic=False  Microsoft.CodeAnalysis.CodeGen.VariableSlotAllocator
IsPublic=False  Microsoft.CodeAnalysis.Emit.EncVariableSlotAllocator
IsPublic=False  Microsoft.CodeAnalysis.Symbols.CommonGeneratedNames
```

`GeneratedNames` is `internal static class GeneratedNames`, and the exact function we would want —
`internal static string MakeLambdaDisplayClassName(int methodOrdinal, int generation,
int closureOrdinal, int closureGeneration)` — is internal **[SRC,
`src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNames.cs:48`]**. `GeneratedName*`
appears **0 times** in the shipped public-API files **[RUN]**.

### 2.5 Portable PDB — what phase, and does it even help?

**Phase: strictly post-`Emit`, and the generator cannot get there.** The PDB is produced *by*
`Compilation.Emit`. A generator runs during the compile stage, before lowering. There is no
`MethodDebugInformation` to read at generator time because there is no PDB.

But the more interesting result is that **even if a generator could read the PDB, the local-scope
tables would not answer the question.** Emitting the sample with
`DebugInformationFormat.PortablePdb` and dumping `LocalScope`/`LocalVariable` **[RUN]**:

```
OptimizationLevel = Debug
Pre-Emit: any type anywhere whose metadata name contains DisplayClass = 0

Post-Emit metadata TypeDefs:
    MyNs.Repo
        field: _instanceField
        method: Query, .ctor
    Repo+<>c__DisplayClass1_0
        field: minId
        field: paramValue
        field: <>4__this
        method: .ctor, <Query>b__0

Portable PDB LocalScope / LocalVariable tables:
    method 'Query' scope[0..64] locals: CS$<>8__locals0@slot0, notCaptured@slot1, f@slot2
```

Read that last line carefully. The captured locals `minId` and `paramValue` are **not in the local
table at all**. What *is* in slot 0 is `CS$<>8__locals0` — the display-class *instance*. Once a
local is captured it stops being a local slot and becomes a display-class **field**; the PDB
records the hoisting only implicitly, by naming the instance. So the "PDB maps locals to slots"
intuition is exactly inverted for the case we care about: **the PDB maps every local except the
captured ones.** To get from `minId` to its storage you would still have to parse
`<>c__DisplayClass1_0` and match the field by source name — i.e. do the guessing anyway.

In `Release`, the PDB has **no `LocalScope` rows at all** **[RUN]** — the whole table is empty.

The one genuinely useful blob is the EnC closure map **[RUN]**:

```
Portable PDB CustomDebugInformation (Debug):
    <755f52a8-...>                 on method 'Query'  (7 bytes) 1F 01 01 30 01 80 8A
    EncLambdaAndClosureMap         on method 'Query'  (7 bytes) 02 01 01 01 80 93 02
    CompilationOptions             on ModuleDefinition 1 ...

Portable PDB CustomDebugInformation (Release):
    <no EncLambdaAndClosureMap, no EncLocalSlotMap>
```

`EncLambdaAndClosureMap` (GUID `A643004C-0240-496F-A783-30D64F4979DE`) is the closure/lambda ordinal
map — the very ordinals that go into `<>c__DisplayClass{methodOrdinal}_{closureOrdinal}`. The
first compressed integer of the blob is `02`, i.e. `methodOrdinal + 1`, giving `methodOrdinal = 1`,
which matches the emitted `<>c__DisplayClass**1**_0` **[INF — the leading-field decode is
consistent with the emitted name; I did not fully decode the remaining bytes]**. But:

- It is **debug-only**. Absent entirely in `Release` **[RUN]** — which is the configuration where
  the naming actually shifts.
- It is **post-emit**, in the PDB, produced by the very `Emit` a generator precedes.
- The decoder is internal: `EditAndContinueMethodDebugInformation` and the reader
  `internal abstract class EditAndContinueDebugInfoReader` **[SRC,
  `src/Features/Core/Portable/EditAndContinue/EditAndContinueDebugInfoReader.cs`]**, which lives in
  the *Features* layer and takes an already-emitted `MethodDefinitionHandle`. The kind GUIDs are in
  `src/Dependencies/CodeAnalysis.Debugging/PortableCustomDebugInfoKinds.cs`.

`ISymUnmanagedReader` is the Windows-PDB path to the same data (`EditAndContinueDebugInfoReader`
has a `Native` subclass over `ISymUnmanagedReader5` **[SRC]**). Same phase problem, plus COM
interop and a legacy format. No advantage.

### 2.6 The genuine near-miss: `EmitBaseline.SynthesizedMembers` **[RUN]**

`Microsoft.CodeAnalysis.Emit.EmitBaseline` **is public**. Its entire public surface:

```
IsPublic = True
  EmitBaseline CreateInitialBaseline(ModuleMetadata, Func<MethodDefinitionHandle, EditAndContinueMethodDebugInformation>)
  EmitBaseline CreateInitialBaseline(ModuleMetadata, Func<...>, Func<...>, bool)
  EmitBaseline CreateInitialBaseline(Compilation, ModuleMetadata, Func<...>, Func<...>, bool)
  ModuleMetadata OriginalMetadata { get; }
```

Underneath, it carries exactly the map we want — and it is internal:

```
IsPublic=False  Emit.SynthesizedTypeMaps SynthesizedTypes { get; }
IsPublic=False  IReadOnlyDictionary<Symbols.ISymbolInternal, ImmutableArray<Symbols.ISymbolInternal>> SynthesizedMembers
IsPublic=False  int GetNextAnonymousTypeIndex(bool)
IsPublic=False  int GetNextAnonymousDelegateIndex()
```

`SynthesizedMembers` is a **symbol → synthesized-members** dictionary: the closest thing in the
whole codebase to the API this task went looking for. Three reasons it does not help:

1. It is keyed on `ISymbolInternal`, not `ISymbol` — a different, internal symbol model.
2. It is populated **only from a prior emit**: EnC constructs a baseline from an already-emitted
   module so generation *N+1* can reuse generation *N*'s synthesized names. It is by construction a
   post-emit artifact.
3. It is internal on a public type, so even the public `EmitBaseline` handle is opaque —
   `OriginalMetadata` is all a caller gets.

The existence of this map is the strongest evidence that **the compiler does track symbol →
synthesized-name internally and has done for years** (EnC would be impossible otherwise). It has
simply never been projected onto the public surface.

---

## 3. All related dotnet/roslyn issues and PRs

Searched via the GitHub search API for: public APIs over compiler-generated / synthesized names;
stable or documented closure and display-class naming; `GeneratedNames`; generators needing lowered
constructs; symbol → metadata-name mapping; `UnsafeAccessor` with generators; `SymbolKey`; PDB
`MethodDebugInformation` for generators. Closed and `not-planned` items are included.

| # | Title | State | Date (open / close) | Direction | Relevance |
| --- | --- | --- | --- | --- | --- |
| **11565** | Provide a public API to parse generated names | **CLOSED / NOT_PLANNED** | 2016-05-25 / 2022-10-28 | **name → source name** | The primary cited issue. Requests `GeneratedNames.TryParseGeneratedName` be made public. Not our direction |
| **55651** | Support retrieving original type name from mangled type name | **OPEN** | 2021-08-16 / — | **name → source name** | The second cited issue. Proposes `CompilerGeneratedAttribute.OriginalName`. Not our direction |
| **50978** | Needs Review: Proposal: Emitting compiler details | CLOSED (stale-bot, `COMPLETED`) | 2021-02-03 / 2021-09-15 | **symbol → name** (partly) | **The only issue that asks for our direction.** Proposes the compiler emit a mapping table including "source symbol metadata name / emitted symbol name / emitted symbol metadata name". Drew a real maintainer objection — see quotes below |
| **27527** | Make SymbolKey API public | **CLOSED / NOT_PLANNED** | 2018-06-06 / 2022-10-31 | symbol → durable *source* id | Would have made `SymbolKey` public. Closed `NOT_PLANNED` three days after #11565. Would not have answered our question anyway (§2.3) |
| **27581** | [WIP] Public SymbolKey API | CLOSED (PR, unmerged) | 2018-06-07 / 2021-08-24 | as above | The abandoned implementation of #27527 |
| **55558** | Generated names refactoring & support for function breakpoints on local functions | MERGED (PR) | 2021-08-11 / 2021-08-12 | name → source name | The refactor that split `GeneratedNames` (making) from `GeneratedNameParser` (parsing). Both stayed internal |
| **79073** | Add parsing of StateMachine GeneratedNames to StackFrameParser | MERGED (PR) | 2025-06-20 / 2026-02-12 | name → source name | Roslyn keeps building *parsers* internally for its own IDE features. Still nothing public |
| **50931** | [EE] Prettify compiler-generated field names with `IDkmClrFullNameProvider.GetClrMemberName` | CLOSED (PR) | 2021-02-01 / 2021-02-02 | name → source name | Debugger-side un-mangling for the watch window. Ships as a debugger interface, not a Roslyn API |
| **60522** | [EE] Implement `IDkmClrFullNameProvider2` in Roslyn's ResultProvider Formatter | MERGED (PR) | 2022-04-01 / 2022-05-14 | name → source name | As above |
| **68542** | Question to synthesized `GeneratedNames` for yield statement | CLOSED / COMPLETED | 2023-06-11 / 2023-06-16 | name → source name | User question about mangled names; answered, no API |
| **45564** | Do not use `$Program` and `$Main` for generated top-level code type/method names | CLOSED / COMPLETED | 2020-06-30 / 2020-07-14 | naming policy | Roslyn *changing* a generated name post-hoc — evidence these names are treated as freely changeable |
| **73365** | EnC: display class might have no synthesized members | OPEN | 2024-05-07 / — | internal | Display-class identity handling inside EnC; internal machinery only |
| **73366** | EnC: workaround for empty display class | CLOSED | 2024-05-07 | internal | As above |
| **82430** | Defer display class allocation for async local functions to call site | CLOSED | 2026-02-17 | codegen change | Recent, live change to *when* display classes are created — reinforces that closure lowering is unstable ground |
| **18569** | Display class allocation issues | CLOSED | 2017-04-09 | codegen change | Historical closure-lowering churn |
| **76573** | Display Class used when not required + related flow analysis | CLOSED | 2024-12-27 | codegen change | As above |
| **83089** | [API Proposal]: `RegisterPreCompilationSourceOutput` | **OPEN** (reopened) | 2026-04-07 / — | generator phasing | Moves generator output *earlier*, not later. Explicitly states the compilation is not available in that phase. Does not help |
| **68993** | Generators: `ForCompilationReferences` | OPEN | 2023-07-11 / — | generator inputs | Generator access to references; nothing about lowered constructs |
| **44929** | Feedback from writing a source generator | OPEN | 2020-06-07 / — | generator ergonomics | General feedback thread; no closure/naming ask |

**No issue anywhere in `dotnet/roslyn` or `dotnet/csharplang` was found asking for a public
symbol → display-class-name API.** Repeated searches across `display class`, `GeneratedNames`,
`closure` + `ordinal`, `hoisted`, `UnsafeAccessor` + generator, `synthesized` + `public API`, and
`<>c__DisplayClass` in issue bodies turned up nothing. #50978 is the closest, and it asked for a
side-channel *file*, not an API.

### Authoritative maintainer statements

**jaredpar**, on #50978 **[SRC]** — the strongest statement bearing on our direction:

> > It is quite difficult (nearly impossible) to map the symbols in the generated IL back to the
> > original code in an automated way
>
> **This is a non-goal of the compiler.**

> It is not the job of the DLL to map instructions back to code, that is the job of the PDB. The
> PDB contains all the necessary information to do this (consider this is how all debugging works).

Read precisely, jaredpar is again answering the IL→source direction, and his answer is "the PDB
already does that". But the surrounding position — that the compiler does not owe consumers a
symbol/IL correspondence, and that the PDB is where such correspondence lives — bears directly on
our case, and §2.5 shows the PDB does *not* in fact carry it for captured locals.

**tmat**, on the same issue **[SRC]** — the most encouraging statement found anywhere:

> Roslyn's debugger components perform various mappings like the ones requested — this is essential
> for EnC. It is definitely possible using information in the DLL and the PDB. **Unfortunately, the
> code is currently all internal.** It'd definitely be interesting to make it a public API that
> does not leak implementation details out (so that we allow the compiler to change how code is
> emitted without breaking the API consumers) but still provides high-level metadata to source
> mapping APIs.
>
> I'm not sure however when/if we are going to be able to expose such APIs due to our priorities.

Note the design constraint tmat states: any such API must **not leak implementation details, so
that the compiler stays free to change how code is emitted**. An API that handed out
`<>c__DisplayClass3_1` as a durable string is precisely the kind of leak he is ruling out. This is
the clearest available signal on why our direction is unlikely to ship in the shape we would want.

**tmat**, on #55651 **[SRC]**:

> Rather then adding another extensibility point into Emit I believe it'd be better if Roslyn
> provided public APIs that would give you the ability to map from mangled names back to source
> names of the symbol. It would be much easier to implement.

Again: back to source names. The consistent maintainer preference across five years is
**name → source**, never **source → name**.

**CyrusNajmabadi**, on #11565 and #27527 **[SRC]** — the same sentence on both:

> This would need to go through an API proposal.

---

## 4. Verdict

**No. There is no shipped or reachable API that gives a captured local's emitted storage identity
from its symbol.** Not public, not internal-but-reflectable, not at any phase a generator runs in.

The reasoning, compactly:

- The captured local's emitted identity is `MyNs.Repo+<>c__DisplayClass{m}_{c}::minId`. The
  `{m}_{c}` ordinals are chosen by `ClosureConversion` during lowering, which runs inside `Emit`,
  after generators.
- Every public symbol→string API returns a *source*-shaped string: `MetadataName` returns
  `"minId"`, `MetadataToken` is `0`, doc IDs are `null`, and every `SymbolDisplayFormat` returns
  the bare identifier **[RUN]**.
- The one public symbol→durable-identity mechanism that handles body-level symbols at all —
  `SymbolKey` — is internal, encodes source spans rather than emitted names, contains no `<>c` or
  `DisplayClass` substring, is not stable across trivial edits, and lives in an assembly a
  generator cannot load **[RUN]**.
- The correct answer *does* exist internally, in three places: `GeneratedNames.MakeLambdaDisplayClassName`
  (the name maker), `PEModuleBuilder.Translate` (the symbol→Cci projection), and
  `EmitBaseline.SynthesizedMembers` (a symbol → synthesized-members map). All three are internal,
  and all three require artifacts that do not exist until `Emit` has run.
- The PDB, the usual fallback, does not carry the mapping either: a captured local is *absent* from
  `LocalVariable` — what occupies the slot is the display-class instance `CS$<>8__locals0` **[RUN]**.
  The closure-ordinal data does exist, in the `EncLambdaAndClosureMap` custom debug info, but it is
  debug-only, post-emit, and internally decoded **[RUN]**.

### Closest partial answer

**A generator can call `compilation.Emit()` on the compilation it is handed and read the real
display-class names out of the resulting metadata.** This is not a thought experiment — it runs
**[RUN]**:

```
=== Does Emit() inside a generator even work? ===
  generator: EmittingGen exception=(none)
  generated 'probe.g.cs':
      // self-Emit inside generator: success=True
      // found <>c__DisplayClass1_0 fields[minId,paramValue,<>4__this]
```

And in the tested case the names it recovers match what the compiler ultimately emits, in both
configurations **[RUN]**:

```
OptimizationLevel = Debug
[what a generator sees]      Repo+<>c__DisplayClass1_0 fields[minId, paramValue, <>4__this]
[what is actually emitted]   Repo+<>c__DisplayClass1_0 fields[minId, paramValue, <>4__this]
                             Repo+<>c__DisplayClass2_0 fields[extra, q]

OptimizationLevel = Release   (identical)
```

Adding a generated tree to the *same partial type* did not perturb the user method's ordinal —
generated trees are appended, so user methods keep their position.

It is nonetheless a bad trade, for reasons that are also measured rather than assumed:

- **It fails outright whenever the user's code depends on the generator's own output** — the
  overwhelmingly common case for a generator like ours **[RUN]**:

  ```
  === Self-Emit of a compilation that needs the generator's own output ===
    Emit success = False, bytes written = 0
      Src.cs(11,31): error CS0103: The name 'GeneratedHelper' does not exist in the current context
  ```

  Zero bytes. No metadata, no partial recovery, nothing to read.

- It doubles compilation cost, on every generator pass, including every keystroke in the IDE.
- It sees the compilation *without* any generated trees, so ordinals for methods in generated code
  are wrong by construction, and any future change to how generated trees are ordered would shift
  user ordinals too.
- It is an undocumented reentrancy that Roslyn does not promise to keep working.

The correct conclusion for the design is unchanged: **the display-class name is not a durable
contract and cannot be obtained, only predicted.** Any approach that names
`<>c__DisplayClass{m}_{c}` is guessing, whether the guess is computed by string-building or
recovered by a speculative self-emit. tmat's constraint on #50978 — an API "that does not leak
implementation details out, so that we allow the compiler to change how code is emitted without
breaking the API consumers" — is the maintainers explicitly reserving the right to keep changing
exactly the thing our generator depends on, and #82430 (2026-02, "Defer display class allocation
for async local functions to call site") shows they are still exercising it.

---

## 5. What I could not determine

- **Why #11565 and #27527 were closed `NOT_PLANNED` within three days of each other in
  Oct 2022.** Neither close carries a comment. It has the shape of a bulk triage sweep, but I found
  no announcement confirming that, and I did not check whether other `Concept-API` issues were
  closed in the same window.
- **The full byte layout of the `EncLambdaAndClosureMap` blob.** I decoded the leading compressed
  integer as `methodOrdinal + 1 = 2`, consistent with the emitted `<>c__DisplayClass1_0`, but the
  remaining six bytes (`01 01 01 80 93 02`) I did not fully decode. I looked for
  `docs/features/EnC-Lambda-and-Closure-Map.md` and an equivalent under `docs/`; neither exists.
  The authoritative source is
  `src/Compilers/Core/Portable/PEWriter/MetadataWriter.PortablePdb.cs` and
  `EditAndContinueMethodDebugInformation.cs`, which I located but did not read line-by-line. This
  does not affect the verdict — the blob is debug-only and post-emit regardless of its layout.
- **Whether the self-emit workaround survives adversarial generator ordering.** I tested one
  generated tree appended to the same partial type, which was stable. I did not test multiple
  generators, generated partial *method bodies* on the target method, or a generator that adds
  members earlier in a type's member order. Given the workaround is already ruled out by the
  compile-failure case, I did not pursue this.
- **Whether `SymbolKey`'s visibility differs in Roslyn 5.x.** My runtime probe used 4.14.0. I
  cross-checked `main` by confirming `SymbolKey` appears in zero `PublicAPI.{Shipped,Unshipped}.txt`
  files, which is strong but is a static check rather than a run against a 5.x assembly.
- **Whether any *non-Roslyn* channel exposes this.** I checked the one channel worth checking —
  `ExternalAccess`, the sanctioned route by which first-party partners reach Roslyn internals.
  Repo-wide code search for `DisplayClass`, `SynthesizedClosure`, `MakeLambdaDisplayClassName`,
  `GeneratedNames`, `GeneratedNameParser` and `SymbolKey` under any `ExternalAccess` path returns
  **0 hits for every term** **[RUN]**; `src/Tools/ExternalAccess` contains only `RazorCompiler`
  and `RazorCompilerTest`. So closure identity is not exposed even to first-party partners. What I
  did *not* check is whether some unrelated Microsoft-internal contract (an EnC/debugger service,
  a Hot Reload agent interface) carries it — but any such thing would be unavailable to us anyway.
