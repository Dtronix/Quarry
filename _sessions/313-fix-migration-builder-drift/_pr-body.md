## Summary
- Closes #313

Fixes the live CLI bug where `quarry migrate add`/`diff` silently degraded to an empty-baseline diff (scaffolding `CREATE TABLE` for every existing table) whenever the latest snapshot used a column default, collation, or character set — and removes the structural cause by single-sourcing the duplicated migration model types.

- **Builder API reconciled** — shared `ColumnDefBuilder` had drifted from the runtime copy (`Default(string)` vs `DefaultValue(string)`, `Nullable()` vs `Nullable(bool = true)`); unified on the runtime API that generated snapshot code targets. `SnapshotCompiler.AllowedMethods` gains the missing `DefaultValue`/`Collation`/`CharacterSet`, drops dead `Default`.
- **Loud failure** — a snapshot or migration that exists but fails validation/recompilation/invocation now throws (tool exits 1) instead of returning `null`. `migrate add`/`diff` can no longer diff against an accidental empty baseline; `migrate script` aborts instead of embedding `-- ERROR` comments inside the emitted SQL; `TargetInvocationException` is unwrapped with snapshot/migration context.
- **Single-sourced model** — the 11 duplicated types (8 models + 3 builders) now compile from one set of files in `Quarry.Shared/Migration` into Quarry.dll (public `Quarry.Migration`, via new `QUARRY_RUNTIME` define), Quarry.Generator (internal `Quarry.Shared.Migration`), and Quarry.Tool (public `Quarry.Shared.Migration`), following the existing `SqlDialect.cs` gating pattern. The runtime duplicates are deleted (net −544 lines). Drift of this class is now structurally impossible.
- **FK action emission bug fixed (found in review)** — both `SnapshotCodeGenerator` and `MigrationCodeGenerator` emitted foreign-key actions positionally, so `OnDelete == NoAction` with `OnUpdate != NoAction` silently emitted the update action into the delete-action parameter. Now emitted as named arguments.
- **Whitelist hardening (found in review)** — the snapshot recompile now uses a minimal reference set (never the user project's reference graph) and rejects object creations other than `SchemaSnapshotBuilder`; the whitelist itself is a private `FrozenSet`.

## Reason for Change
The migration schema model existed as two hand-synced copies (`src/Quarry/Migration` runtime vs `src/Quarry.Shared/Migration` for generator/CLI). The copies drifted, and the CLI's `SnapshotCompiler` — which recompiles generated snapshot code against the shared builders — rejected or failed to compile any snapshot exercising the drifted/missing methods, returning `null`. `SchemaDiffer.Diff` treats a `null` previous snapshot as an empty schema, so the failure was silent and destructive-by-suggestion.

## Impact
- `quarry migrate add`/`diff` now work correctly on projects whose snapshots use column defaults, collation, or character sets.
- Every schema-model change is now made once; all three assemblies compile the same source.
- 12 new regression tests (snapshot round-trip, whitelist coverage, compiler failure/success paths, command-level guards).
- Test suite: 3402 + 201 passing (baseline was 3388 + 201, zero pre-existing failures).

## Plan items implemented as specified
1. Builder API unification + whitelist fix (`cf81505`)
2. Loud snapshot-recompile failure + `SnapshotCompilerTests` (`a8aac12`)
3. Single-sourcing via `QUARRY_RUNTIME` namespace gating (`34459af`)
4. Round-trip + whitelist-coverage regression tests (`5db036d`)
5. Recompile-seam audit + `MigrationCompiler` loud failure + `MigrationCompilerTests` (`9c65a7a`)
6. Docs touch-up (generator `llm.md` project-boundaries table) (`9c65a7a`)

## Deviations from plan implemented
- `ValidateBuildMethod` was renamed to `FindDisallowedCall` returning the offending name (rather than just made `internal`) so throw messages can name the disallowed call; later extended to also reject object creations.
- The round-trip test asserts zero *error* diagnostics (not all diagnostics), matching how the production compilers gate emit success.

## Gaps in original plan implemented
From the review pass (14 findings → 6A/3B/5D, all A/B addressed in `b629593`):
- FK action positional-argument bug in both code generators (review F4) + mixed-action FK round-trip coverage (F10).
- `TargetInvocationException` unwrapping with context in both compilers (F3).
- Snapshot recompile hardening: minimal reference set + constructor gating (F6); whitelist frozen/private (F7).
- True success-path test through the full production `SnapshotCompiler` path via a `QuarrySnapshotCompilation` IVT on the generator (F8), plus a command-level guard test with `MigrateCommands.cs` compile-included into tests (F9).
- Release-notes staging entry for the CLI contract change (F13); Tool README file map updated for the deleted runtime files (F11).

## Security Considerations
The whitelist guarding in-process execution of recompiled snapshot code is now stronger: name-based invocation checks are supplemented by constructor rejection, and the recompile no longer receives the user project's full reference graph, so types outside core runtime + the builders cannot resolve even if crafted code slips past the syntax scan. Residual risk (name-only matching) is unchanged from before this PR.

## Breaking Changes
- Consumer-facing
  - **CLI exit-code contract**: `migrate add`/`diff`/`squash`/`script` abort with exit 1 when an existing snapshot/migration fails to recompile (previously stderr warning + exit 0, or silent empty-baseline output). Intentional; captured in `docs/articles/releases/release-notes-unreleased.md`.
  - **Regenerated snapshot/migration code** for FKs with only a non-default `OnUpdate` action changes shape (named arguments) — the old emission was semantically wrong.
  - Runtime `Quarry.Migration` public API surface is unchanged (verified member-by-member in review). `GetHashCode` values of model types change (accumulator instead of `HashCode.Combine`); never persisted.
- Internal
  - Shared `ColumnDefBuilder.Default(string)` removed, `Nullable()` gained an optional `bool` (zero callers; surface exists only inside the Tool executable and generator internals).
  - `Quarry.Generator` adds `InternalsVisibleTo("QuarrySnapshotCompilation")` (test-enablement; production builders are public in the Tool).
