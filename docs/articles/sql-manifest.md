# SQL Manifest

Quarry can emit a per-dialect markdown file listing every SQL statement the generator produced for your project. The manifest is opt-in, has zero build- or run-time overhead when disabled, and is designed for code review, query governance, schema audits, and AI-assisted reviews of query surface.

## Enabling Manifest Emission

Add a single MSBuild property to the project that owns your `QuarryContext`:

```xml
<PropertyGroup>
  <QuarrySqlManifestPath>$(MSBuildProjectDirectory)/sql-manifest</QuarrySqlManifestPath>
</PropertyGroup>
```

The next build writes one file per dialect your project targets:

```
sql-manifest/quarry-manifest.sqlite.md
sql-manifest/quarry-manifest.postgresql.md
sql-manifest/quarry-manifest.mysql.md
sql-manifest/quarry-manifest.sqlserver.md
```

The path is resolved relative to the project directory unless you supply an absolute path. The directory is created automatically if it does not exist.

## What the Manifest Contains

Each manifest groups chains by context, then by call-site signature:

- **Exact generated SQL** — the same string your interceptor executes.
- **Parameter table** — names, CLR types, and sensitivity flags. Includes `LIMIT` / `OFFSET` parameter rows.
- **Conditional variants** — chains with `if`/`else` branches produce one `Variant[0b…]` subsection per bitmask.
- **Per-context summary** — counts of chains, variants, and parameters.

Example excerpt from the Quarry test suite's committed manifest:

```markdown
## CteDb

### Users().With(...).Where(...).Select(...).Prepare().ToDiagnostics()

``​`sql
WITH "Order" AS (SELECT "OrderId", "UserId", "Total", ... FROM "orders" WHERE "Total" > @p0)
SELECT "UserId", "UserName" FROM "users" WHERE "IsActive" = 1
``​`

| Parameter | Type    |
|-----------|---------|
| `@p0`     | decimal |
```

The Quarry test suite commits its own manifests under [`src/Quarry.Tests/ManifestOutput/`](https://github.com/Dtronix/Quarry/tree/master/src/Quarry.Tests/ManifestOutput) as living documentation of the query surface.

## Recommended Workflow

- **Commit manifests to source control.** Reviewers see exactly what SQL changed when you add, remove, or modify a chain. A `WriteIfChanged` guard prevents spurious diffs when the generated output has not changed.
- **Require clean manifests in CI.** Add a `git diff --exit-code sql-manifest/` step so manifest drift fails the build before merge.
- **Multi-dialect projects** emit one file per dialect. All four files should be kept in source control so cross-dialect SQL differences are visible in PRs.

## How It Works

The `ManifestEmitter` stage runs after code generation. It iterates the `AssembledPlan` set, rendering each chain's SQL variants and parameter tables into markdown. No runtime code is involved — nothing ships in your assembly.

A `WriteIfChanged` routine compares the new content against the on-disk file and skips writing when byte-equal, so enabling the manifest on a stable codebase produces no git noise. Write failures (permission errors, read-only path) surface as a `QRY040` compile warning.

## Turning It Off

Remove the `<QuarrySqlManifestPath>` property from the `.csproj`. With no property set, the emitter is skipped entirely — no file I/O, no CPU cost, no dependency on filesystem layout.

## When to Use It

- You need a reviewable artifact of every SQL statement the generator emits, separate from interceptor source.
- Your team wants a snapshot of query surface evolution across commits.
- You audit for dialect-specific SQL differences when supporting multiple databases.
- You feed query inventories to other tools (LLM assistants, query plan analyzers, security scanners) and want them to read markdown instead of interceptor source.
- You want a living reference of the conditional SQL variants for an `if`/`else` chain — each mask bit is labeled.

If you just want to inspect a single chain at development time, [`.ToDiagnostics()`](diagnostics.md) returns the same SQL plus richer metadata at runtime.
