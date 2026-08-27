# Workflow: 345-accessor-verifier
## Config
platform: github
base-branch: master
## State
phase: IMPLEMENT
status: suspended
issue: #345
pr:
## Problem Statement
Issue #345, Track 1 — ship a **post-compile verifier** for the `[UnsafeAccessor]` display-class names
the generator predicts.

Quarry emits accessors whose `[UnsafeAccessorType("…+<>c__DisplayClass{M}_{C}")]` string is a
*prediction*. A wrong prediction is not a build error: the string is resolved at runtime, so it
surfaces as `TypeLoadException` / `MissingFieldException` / `InvalidCastException` on first execution
of the chain. `Quarry.Tests.dll` ships 180 such strings, 174 with a guessed ordinal.

The verifier reads the **emitted assembly** after `CoreCompile` and checks every predicted name against
reality — turning the whole class of misprediction (#310 ordinal shifts, #344 `<Optimize>` merging, and
future compiler changes such as roslyn#82430) into a build error.

It does not fix a bad prediction. It makes one impossible to ship silently.

### Baseline
Branched from `f25879f` (master, immediately after #343 merged). Master CI green at that commit; full
suite verified locally green in **both** Debug and Release: 201 / 146 / 3540, zero skips.

## Decisions

- **2026-08-27 — Hosting: inline `RoslynCodeTaskFactory` task.** User chose this over a separate
  `Quarry.BuildTasks` assembly and over a repo-only-first rollout. Rationale given for the option: no
  new project, no packaging change, and it matches the existing `StripImgTags` precedent in
  `Directory.Build.targets`. Trade-off flagged at the time and accepted: the task body cannot be
  unit-tested, and `RoslynCodeTaskFactory` recompiles it on every build.
  **This decision is now in question — see Working Notes; the user was asked and the session was
  suspended before an answer.**

- **2026-08-27 — On failure: error always, no opt-out.** User chose this over warn-by-default and over
  error-with-an-opt-out property. Consequence accepted: a verifier false positive would block a
  consumer's build with no escape hatch, so the check must be scoped narrowly and must not fault.
  **This decision is unaffected by the blocker below and should be kept.**

## Working Notes

### Design constraints established (verified by inspection)

- `src/Quarry.Generator/build/**` is packed via
  `<None Include="build\**" Pack="true" PackagePath="build\" />`, and NuGet auto-imports
  `build/{PackageId}.props` / `.targets` — so a `Quarry.Generator.targets` there reaches consumers
  with no further packaging work.
- The generator assembly is packed to `analyzers/dotnet/cs` and loaded by **Roslyn**, not MSBuild. It
  cannot host an MSBuild task without shipping its dependencies alongside it.
- In-repo, projects do **not** get `build/*.targets` automatically (they use `ProjectReference`, not
  the nupkg). `Quarry.Tests.csproj` already imports `build\Quarry.Generator.props` explicitly, so the
  matching `.targets` import was added the same way. Other generator-consuming projects
  (`Quarry`, `Quarry.Analyzers`, `Quarry.Benchmarks*`, `Samples/*`) would each need the same import.
- `NetCoreRoot` = `C:\Program Files\dotnet\`, `BundledNETCoreAppPackageVersion` = `10.0.10`,
  `NetCoreTargetingPackRoot` = `C:\Program Files\dotnet\packs`, `MSBuildRuntimeType` = `Core`. All are
  set in a real project build but **empty in a bare `.proj`** (they come from SDK props) —
  that cost a debugging cycle.
- `BundledNETCoreAppTargetFramework` is **not** set in this context; the TFM folder has to be derived
  from the version.
- MSBuild property functions may **not** call `RuntimeEnvironment.GetRuntimeDirectory()`
  (`MSB4212` — type not on the allowlist), so the runtime directory cannot be obtained that way.

### BLOCKER — inline task cannot resolve the references it needs

The algorithm is written and the target is wired, but the inline task **does not compile**. Five
attempts, in order:

| Attempt | Result |
|---|---|
| `<Reference Include="System.Reflection.Metadata" />` | `MSB3755` — not resolvable by simple name |
| `<Reference Include="System.Reflection.MetadataLoadContext" />` | **resolves** (it is in the SDK root), but its API surface needs `System.Runtime` types → `CS0012` |
| + shared-framework implementation assemblies (`$(NetCoreRoot)shared\…`) | `CS0012` — they type-forward to `System.Private.CoreLib` |
| + `System.Private.CoreLib.dll` | `CS0518` "Predefined type 'System.Void' is not defined" — two corlibs |
| targeting-pack ref assemblies (`$(NetCoreTargetingPackRoot)\Microsoft.NETCore.App.Ref\…\ref\net10.0\`) | `CS0518` again |

**Diagnosis (high confidence, not fully proven):** adding *any* explicit `<Reference>` appears to
**replace** `RoslynCodeTaskFactory`'s implicit BCL reference set rather than augment it. That is
consistent with the existing `StripImgTags` task working — it declares **no** `<Reference>` at all and
uses only `System.IO`/`Regex` from the implicit set. Our verifier fundamentally needs an
assembly-reading API that is not in that set.

Not yet tried, if someone wants to continue down this path: supply a *complete* self-consistent
reference set (every BCL contract the task body touches, from the ref pack, including whatever the
implicit set was providing), or hand-roll a dependency-free PE/metadata reader. Both were judged
brittle across SDK versions and hosts — the wrong property for a component whose whole purpose is to be
a trustworthy safety net.

### Recommendation put to the user (unanswered — this is the resume point)

Keep "error always"; revisit hosting. Options offered:

1. **`Quarry.BuildTasks` assembly** — multi-target `netstandard2.0;net472`, packed under `tasks/`,
   `UsingTask` + target in `build/Quarry.Generator.targets`. Conventional, references resolve normally,
   and the logic lives in ordinary C# that `Quarry.Tests` can unit-test.
2. **Console tool invoked via `Exec`** — a plain project, equally testable, no MSBuild task API and no
   reference-set fighting; packed under `tools/`, run with `dotnet exec`. Costs one process launch per
   build.
3. Push further on inline.

Either 1 or 2 restores unit-testability, which matters more than usual here: this component hard-fails
builds, so a false positive is expensive and the negative cases (wrong ordinal, wrong field) need real
tests.

### The algorithm itself is done and carries over unchanged

Written against `MetadataLoadContext` (chosen over `System.Reflection.Metadata` because
`CustomAttributeData` exposes named arguments directly, which removed an earlier fragile
attribute-blob-scanning implementation). It:

1. Loads the emitted assembly in a `MetadataLoadContext` with a `PathAssemblyResolver` over the output
   directory + the host runtime directory.
2. Walks every method whose name starts with `__ExtractVar_` or `__ExtractThis_` — **deliberately
   scoped to generator-emitted accessors only**, because a consumer's own `[UnsafeAccessor]` may
   legitimately name a type in another assembly, which a single-assembly check would falsely report.
   This scoping is load-bearing given "error always, no opt-out".
3. Reads `Name =` from `[UnsafeAccessor]` and the type string from `[UnsafeAccessorType]`; skips
   assembly-qualified names (not checkable here) and methods with no `[UnsafeAccessorType]` (nothing
   was predicted).
4. Resolves the predicted type against the assembly's real types; on failure lists sibling display
   classes sharing the prefix. On success, checks the field exists; on failure lists the actual fields.
5. Reports a verifier fault (exception) as *itself*, never as a Quarry codegen bug, and never passes
   silently.

Current draft is in `src/Quarry.Generator/build/Quarry.Generator.targets` and should be lifted into a
real C# file when the hosting decision changes.

### Still to do after hosting is resolved

- Negative tests: an assembly with a wrong ordinal and one with a wrong field name must both fail.
- Positive coverage: the repo's own build exercises ~667 accessors on every build; confirm zero false
  positives in **both** Debug and Release (the `<Optimize>` axis is exactly what #344 was about).
- Decide whether the other generator-consuming projects import the targets, or whether the repo wires
  it once centrally.
- Measure build-time cost (the #333 throwaway prototype was 0.72 s over `Quarry.Tests.dll`).

## Suspend State

- **Phase/position:** IMPLEMENT, blocked at the first step — getting the inline task to compile.
- **In progress:** the post-compile verifier for #345 Track 1.
- **Immediate next step:** get the user's answer on hosting (task assembly / console tool / keep
  pushing on inline). If task assembly or console tool: create the project, move the algorithm out of
  the `.targets` CDATA into a normal C# file unchanged, and wire `UsingTask`/`Exec`. The algorithm
  needs no redesign.
- **WIP commit:** see the `[WIP]` commit at the tip of `345-accessor-verifier`.
- **Test status:** `dotnet build src/Quarry.Tests` **FAILS** — by design of the current blocker, the
  inline task does not compile (`CS0518`). No Quarry code was changed, so nothing is regressed; the
  failure is entirely the new `.targets` file. Reverting or removing the one-line import in
  `Quarry.Tests.csproj` restores a green build immediately.
- **Unrecorded context:** none beyond this file. The manifest goldens show as modified in the worktree
  but the diff is **pure CRLF with zero content change** (`--numstat` empty) — a pre-existing
  line-ending artifact, not a real edit, and safe to `git checkout --` at any time.

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-08-27 | INTAKE | Branched `345-accessor-verifier` from f25879f; surveyed packaging + task conventions |
| 2026-08-27 | DESIGN | User chose inline RoslynCodeTaskFactory hosting + error-always-no-opt-out |
| 2026-08-27 | IMPLEMENT | Algorithm written against MetadataLoadContext; blocked on inline-task reference resolution after 5 attempts; recommended switching hosting; suspended awaiting that decision |
