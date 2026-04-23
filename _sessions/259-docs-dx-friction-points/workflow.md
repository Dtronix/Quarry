# Workflow: 259-docs-dx-friction-points

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REVIEW
status: active
issue: #259
pr: #260
session: 2
phases-total: 5
phases-complete: 5

## Problem Statement
Issue #259 reports three DX/docs friction points hit while adopting Quarry 0.3.0 in a .NET 10 / PostgreSQL 17 project. None are blocking bugs — each has a workaround — but each cost time because `llm.md` doesn't warn the author and the failure modes are cryptic Roslyn errors rather than Quarry diagnostics.

1. **Row entity constraints not documented.** `RawSqlAsync<T>` requires `T` to have a parameterless constructor and `public get; set;` properties. Positional records and init-only properties cause `CS7036` / `CS8852` at compile time. The generator calls `new T()` and assigns each column to a settable property.
    - Doc fix: note under Raw SQL in `llm.md` that row types need parameterless ctor + get/set props; immutable shapes must come from `Select(... => new Dto { ... })` projections.
    - Bonus analyzer: **QRY090** — row entity type has no accessible default constructor / has init-only props.

2. **Nested row entity types break the generated interceptor.** Moving a row record inside an enclosing class causes the generator to emit `using <EnclosingType>;` which fails with `CS0138` (using-directive on a type). Workaround: declare row types at namespace level.
    - Generator fix: detect when containing symbol is a type and either emit fully-qualified names or emit **QRY091** (or use the existing QRY-series convention) diagnostic.

3. **`InterceptorsNamespaces` MSBuild opt-in is undocumented.** Authors hit `CS9137` twice (once for `Quarry.Generated`, once for their context's namespace). The guide says nothing about it.
    - Doc fix: add a **Project setup** section to `llm.md` before Context Setup, documenting the two namespaces that must be added.
    - Better: ship an MSBuild `.targets` file in the Quarry NuGet that auto-adds `Quarry.Generated`, and emit **QRY092** naming the caller namespace.

### Baseline
All tests pass on master at d16d125:
- Quarry.Analyzers.Tests: 103 passed
- Quarry.Migration.Tests: 201 passed
- Quarry.Tests: 2938 passed
- Total: 3242 passed, 0 failed, 0 skipped.

No pre-existing failures to exclude.

## Decisions

### 2026-04-22 — Scope: full (docs + diagnostics + .targets + analyzer)
All three friction points addressed. Rationale: issue is a bundled DX complaint; fixing only one would leave the other two as recurring paper cuts. Splitting would add bookkeeping (3 PRs for related changes) without real independence between them.
- **Friction 1:** new generator diagnostic `QRY043` for invalid row-entity shape (no parameterless ctor / init-only properties).
- **Friction 2:** support nested row types by emitting fully-qualified names in generated interceptors (rather than rejecting them with a diagnostic). Bigger but proper fix.
- **Friction 3a:** ship `build/Quarry.targets` that auto-appends `Quarry.Generated` to `InterceptorsNamespaces`. The consumer's own context namespace still requires manual add (the targets file can't know that ahead of time).
- **Friction 3b:** new analyzer `QRY044` in `Quarry.Analyzers` detecting `[QuarryContext]` whose containing namespace is not in `InterceptorsNamespaces`. Descriptive diagnostic only (no automated code fix — Roslyn `CodeFixProvider` targets source, not `.csproj`; a diagnostic with the exact line to paste is simpler and less brittle than attempting to rewrite project files).
- **Friction 3c:** update `llm.md` with a dedicated Project Setup section covering InterceptorsNamespaces (both names), plus row-entity shape constraints under Raw SQL.

### 2026-04-22 — Nested type representation
Add `IsNestedType` (bool) and `FullyQualifiedResultTypeName` (string) to `RawSqlTypeInfo`. Add `EntityNamespace` (string?) to `RawCallSite`. Discovery-time data from the symbol. Keeps `ResultTypeName` as the short form for backward compatibility; emitters branch on `IsNestedType` to pick the FQN. `FileEmitter` uses `EntityNamespace` directly for `using` directives — no more string-parse guesswork in `GetNamespaceFromTypeName` for the RawSql path.

### 2026-04-22 — Diagnostic IDs
- `QRY042` is already used (in `Quarry.Analyzers/AnalyzerDiagnosticDescriptors.cs` for `RawSqlConvertibleToChain`). Skip it.
- `QRY043` — row entity not materializable (generator-side). Placed in `Quarry.Generator/DiagnosticDescriptors.cs`.
- `QRY044` — `[QuarryContext]` namespace not opted into `InterceptorsNamespaces` (analyzer-side). Placed in `Quarry.Analyzers/AnalyzerDiagnosticDescriptors.cs` (matches where the other QRY0xx analyzer diagnostic lives).


## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-22 | - | INTAKE→DESIGN→PLAN: baseline green (3242 tests), design decisions recorded, plan.md approved. Auto-transition to IMPLEMENT. |
| 2 | 2026-04-23 | - | Resume. Worktree had been pruned from disk; recreated at `../259-docs-dx-friction-points/` from `origin/259-docs-dx-friction-points`. PR #260 verified MERGEABLE/CLEAN, CI run 24820105388 SUCCESS. Branch 8 ahead, 0 behind origin/master — no rebase needed. Remediation already committed (`be224dd`, `25f0b5e`). REMEDIATE step 8: awaiting user finalize confirmation. |
| 3 | 2026-04-23 | - | Back-step REMEDIATE → REVIEW at user request (declined finalize). Keep review.md + Decisions intact; re-run analysis pass over the full branch diff including remediation commits `be224dd` and `25f0b5e`. Classifications reset — rerun classification pass after. No targeted focus — general re-review. |
