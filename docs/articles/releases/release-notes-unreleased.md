# Quarry vNext (unreleased)

_Staging notes for the next release. Not linked from toc.yml until released — fold these into the versioned release-notes file and delete this one._

## Pending entries

### Migration model single-sourcing + CLI loud-failure (#313)

- **CLI behavior change — `quarry migrate add`/`diff`/`squash`/`script` now abort with exit code 1** when an existing schema snapshot or migration fails to recompile, instead of warning on stderr and continuing with exit 0. Previously, a snapshot the CLI could not recompile (any snapshot using a column default, collation, or character set) was silently treated as an **empty baseline**, so `migrate add`/`diff` scaffolded `CREATE TABLE` for every existing table; `migrate script` embedded `-- ERROR` comments inside the emitted SQL instead of failing. CI pipelines that tolerated exit-0-with-warning behavior will now fail loudly — intentionally.
- **Fixed: generated snapshots using `DefaultValue`, `Collation`, or `CharacterSet` recompile correctly** — the CLI's whitelist and the shared builder API had drifted from the runtime builders that snapshot code is generated against.
- **Fixed: foreign keys with a non-default `OnUpdate` action but default `OnDelete`** no longer have the update action silently emitted into the delete-action position when snapshots/migrations are regenerated.
- **Internal: the migration schema model types are now single-sourced** (`Quarry.Shared/Migration/Models` + `Builders` compile into Quarry.dll, Quarry.Generator, and Quarry.Tool from one set of files via `QUARRY_RUNTIME`/`QUARRY_GENERATOR` gating), eliminating the duplicated-copy drift that caused the above. Runtime `Quarry.Migration` public API surface is unchanged.
- **Hardened: snapshot recompilation** now rejects object creations other than the snapshot builder and compiles against a minimal reference set instead of the user project's full reference graph.

### `InsertBatch` and `ToDiagnostics()` now compile for consumers (#334)

- **Fixed: `InsertBatch(...)` did not compile in any project outside Quarry's own solution.** Its generated interceptor called `Quarry.Internal.BatchInsertSqlBuilder`, which was `internal`, so consumers got `error CS0122: 'BatchInsertSqlBuilder' is inaccessible due to its protection level` in the generated `*.Interceptors.*.g.cs`. The type is now `public`.
- **Fixed: `ToDiagnostics()` did not compile in any project outside Quarry's own solution, on any chain shape.** `QueryDiagnostics`'s only constructor was `internal`, so consumers got `error CS1729: 'QueryDiagnostics' does not contain a constructor that takes 23 arguments` — the form the compiler uses when a type's sole constructor is not an accessible candidate. The constructor is now `public`. This affected every documented use of `ToDiagnostics()`, including inspecting generated SQL from your own tests.
- **Added (not supported API):** `Quarry.Internal.BatchInsertSqlBuilder`, its `MaxParameterCount` field, and the `QueryDiagnostics` constructor are now public so generated code can reach them. All are marked `[EditorBrowsable(Never)]` and are **not** part of the supported surface — they exist for emitted interceptors and may change without notice. Both public entry points now validate their arguments.
- **Why this was invisible:** every project in the Quarry repository is on the `InternalsVisibleTo` list, so no in-repo build ever compiled generated interceptors the way a consumer does. `InterceptorBindingGuardTests` now compiles a matrix of chain shapes — joins, set operations, aggregates, CTEs, window functions, raw SQL, collection parameters, conditional masks, prepared and diagnostics terminals — in a deliberately non-friend assembly, and fails the build if a generated interceptor ever again names something a consumer cannot reach.
