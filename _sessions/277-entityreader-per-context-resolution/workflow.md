# Workflow: 277-entityreader-per-context-resolution

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #277
pr:
session: 1
phases-total: 6
phases-complete: 1

## Problem Statement

`[EntityReader(typeof(MyReader))]` on a Schema resolves the reader's `T` once, in the schema's
namespace. When the same schema is referenced by multiple `QuarryContext` subclasses living in
different namespaces, each context generates its own per-context entity type
(e.g., `Quarry.Tests.Samples.Pg.Product`, `My.Product`, `Ss.Product`), but the interceptor emits the
shared schema-namespace entity (`Quarry.Tests.Samples.Product`) as the Select projection's output
type. The chain shape becomes `IQueryBuilder<Pg.Product, Quarry.Tests.Samples.Product>`, which does
not match what the lambda `p => p` (where `p : Pg.Product`) would naturally produce. The chain
compiles via `Unsafe.As<>` casts inside Quarry's runtime helpers, but the static type at the call
site does not match the context's entity, so any consumer code expecting `Pg.Product` semantics
(extensions, partial extensions like `DisplayLabel`, downstream type checks) breaks.

This blocked Phase 10 of PR `cross-dialect-test-coverage` — the conversion of
`Integration/EntityReaderIntegrationTests.cs` to a 4-dialect file. The Integration file remains in
place as SQLite-only coverage until this issue lands.

### Surface area (per issue #277)
- `src/Quarry.Generator/Parsing/SchemaParser.cs:294` — `ResolveEntityReaderAttribute`.
- `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs` ~ lines 175–193.
- `src/Quarry.Generator/IR/EntityRef.cs`, `IR/QueryPlan.cs`, `Models/EntityInfo.cs`,
  `Models/ProjectionInfo.cs`.
- Build output diagnostic: `obj/GeneratedFiles/.../PgDb.Interceptors.*.g.cs` line 33 (bad chain),
  line 97 (wrong reader instance).

### Baseline
- Build: succeeds (8 pre-existing warnings — NU1903 cryptography vulnerabilities + 4 CS0649
  unused-field warnings on generated `Chain_6.P1` for CrossDialectDistinctOrderByTests).
- Tests: 3340/3340 passing (Quarry.Analyzers.Tests 117, Quarry.Migration.Tests 201,
  Quarry.Tests 3022).
- No pre-existing test failures.

### Scope (per issue #277 Suggested Approach)

Layer 1 (test/sample side, mechanical):
- Per-context `Product` partial extensions in `Pg`, `My`, `Ss` namespaces (mirror the global
  `DisplayLabel`).
- Per-context Reader classes (`Pg.ProductReader`, `My.ProductReader`, `Ss.ProductReader`).

Layer 2 (generator side, structural):
- `SchemaParser.ResolveEntityReaderAttribute`: capture `ReaderClassSimpleName` alongside FQN.
- IR threading: add `CustomEntityReaderSimpleName` to `EntityRef`, `QueryPlan`, `EntityInfo`,
  `ProjectionInfo`.
- `InterceptorCodeGenerator`: per-context FQN lookup `<context-namespace>.<reader-simple-name>`;
  fall back to original FQN.
- Projection output type (`TOut`) bound to per-context entity when per-context reader is present.
- Generator integration tests (per-context resolution, fallback, error case if per-context reader
  doesn't inherit `EntityReader<PerContextEntity>`).
- Manifest output (`quarry-manifest.{dialect}.md`) verification.

After both layers:
- Convert `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs` to
  `SqlOutput/CrossDialectEntityReaderTests.cs` (Phase 4–9 verbatim conversion pattern from the
  cross-dialect-test-coverage workflow).
- Delete the Integration file.

## Decisions

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-26 | Source: existing repo issue #277 | User selected new workflow for #277 over resuming cross-dialect-test-coverage. |
| 2026-04-26 | Per-context reader resolution becomes the default — no fallback, no Compilation lookup, no new diagnostics. | Lines up with how Quarry already emits per-context entity types (Pg.Product / My.Product / Ss.Product). Single-context-same-namespace consumers see zero behavior change (per-context FQN == schema-namespace FQN). Multi-context-different-namespace consumers were silently buggy before (Unsafe.As papered over a static-type mismatch); the change converts that into an explicit C# compile error on the missing class — a strict improvement. Drops most of the surface the issue body proposed: no `ReaderClassSimpleName` IR slot (derive from existing FQN), no Compilation threading into FileEmitter, no pre-collected name set, no fallback path, no QRY-level "missing reader" / "mis-declared reader" diagnostics (the C# compiler enforces both via the generated code). |
| 2026-04-26 | Keep QRY027 (`InvalidEntityReaderType`) as-is — it continues to validate the schema-level `[EntityReader(typeof(R))]` against the schema-namespace entity. | Preserves the meaningful pre-build error for the most common case (single context in schema namespace). Per-context mis-declarations surface as ordinary C# compile errors via the generated reference; no analyzer needed. |
| 2026-04-26 | PR scope includes the deferred Phase 10 conversion: `Integration/EntityReaderIntegrationTests.cs` → `SqlOutput/CrossDialectEntityReaderTests.cs`, delete the original. | End-to-end validation on PG/My/Ss/Lite proves the generator fix in real interceptor emission, not just in unit-style pipeline tests. Closes the cross-dialect-test-coverage workflow's deferred Phase 10 in the same PR. |
| 2026-04-26 | Generator-test fixture: reuse existing PgDb / MyDb / SsDb + ProductSchema. | Matches the QRY070/QRY071 pipeline-test pattern landed in cross-dialect-test-coverage Phase 3. Avoids fixture duplication; the existing per-context contexts are exactly the multi-namespace setup the fix targets. |

## Suspend State

## Session Log

| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | INTAKE | DESIGN | Loaded issue #277 from GitHub. Created branch + worktree from master @ aebf88d. Baseline 3340/3340 passing, no pre-existing failures. Auto-transitioned to DESIGN. |
| 1 | DESIGN | PLAN | Explored generator surface — confirmed FQN flows from `[EntityReader]` attribute through SchemaParser → EntityInfo → EntityRef → ProjectionInfo → SelectProjection. Identified two codegen seams (`CollectEntityReaderInstances` + `GenerateReaderDelegate`) and `CarrierEmitter`'s TOut binding. Locked direction: per-context reader resolution becomes default — no IR changes, no Compilation threading, no new diagnostics. PR scope includes Phase 10 conversion. |
| 1 | PLAN | IMPLEMENT | Wrote 6-phase plan in plan.md (Layer 1 → generator codegen → tests → Phase 10 conversion → manifest verification → docs). User approved. |
| 1 | IMPLEMENT P1 | IMPLEMENT P1 | Phase 1 complete — added `src/Quarry.Tests/Samples/PerContextProductReaders.cs` with `Pg`/`My`/`Ss` Product partials adding `DisplayLabel` plus per-context `ProductReader` classes mirroring the global one. Build clean (8 pre-existing warnings). Tests: 3340/3340 passing (count unchanged; per-context readers are dead code until Phase 2). |
