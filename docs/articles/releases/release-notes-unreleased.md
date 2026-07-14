# Quarry vNext (unreleased)

_Staging notes for the next release. Not linked from toc.yml until released — fold these into the versioned release-notes file and delete this one._

## Pending entries

### Migration model single-sourcing + CLI loud-failure (#313)

- **CLI behavior change — `quarry migrate add`/`diff`/`squash`/`script` now abort with exit code 1** when an existing schema snapshot or migration fails to recompile, instead of warning on stderr and continuing with exit 0. Previously, a snapshot the CLI could not recompile (any snapshot using a column default, collation, or character set) was silently treated as an **empty baseline**, so `migrate add`/`diff` scaffolded `CREATE TABLE` for every existing table; `migrate script` embedded `-- ERROR` comments inside the emitted SQL instead of failing. CI pipelines that tolerated exit-0-with-warning behavior will now fail loudly — intentionally.
- **Fixed: generated snapshots using `DefaultValue`, `Collation`, or `CharacterSet` recompile correctly** — the CLI's whitelist and the shared builder API had drifted from the runtime builders that snapshot code is generated against.
- **Fixed: foreign keys with a non-default `OnUpdate` action but default `OnDelete`** no longer have the update action silently emitted into the delete-action position when snapshots/migrations are regenerated.
- **Internal: the migration schema model types are now single-sourced** (`Quarry.Shared/Migration/Models` + `Builders` compile into Quarry.dll, Quarry.Generator, and Quarry.Tool from one set of files via `QUARRY_RUNTIME`/`QUARRY_GENERATOR` gating), eliminating the duplicated-copy drift that caused the above. Runtime `Quarry.Migration` public API surface is unchanged.
- **Hardened: snapshot recompilation** now rejects object creations other than the snapshot builder and compiles against a minimal reference set instead of the user project's full reference graph.
