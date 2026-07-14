## Summary
- Closes #324

Makes the migration tool's `ProjectSchemaReader` honor the two column-naming mechanisms the runtime
source generator already honors — the `NamingStyle` override and per-column `MapTo("physical")` — so
`quarry migrate add`/`diff` produce snapshots and DDL whose column names match the physical names the
runtime actually queries.

## Reason for Change
The migration reader detected the naming style from a property literally named `Naming` (the real API is
`NamingStyle`) and never parsed `MapTo`. As a result:
- Snapshots/DDL used C# **property** names (`UserName`, `CreditLimit`) while the runtime emits SQL using
  **physical** names (`user_name`, `credit_limit`) — a generated migration created columns the runtime
  never queries.
- Adding or removing a mapping between two schema versions produced **zero** diff steps — the change was
  silently ignored, so the physical column the runtime targets was never created or renamed.

## Impact
- `src/Quarry.Tool/Schema/ProjectSchemaReader.cs`
  - `ExtractTableDef` now detects the real `NamingStyle` override, mirroring the runtime
    `SchemaParser.ExtractNamingStyle` **exactly**: it gates on `!IsStatic && IsOverride` and reads only an
    expression-bodied member access (`protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;`).
    Honoring any looser form than the runtime would let the two disagree on column names again.
  - `ExtractColumnDef` now parses `MapTo` — both the chained `.MapTo("x")` and the standalone generic
    `MapTo<T>("x")` (via a new `GenericNameSyntax` case, mirroring runtime `GetMethodName`). The physical
    column name becomes `MapTo ?? ToColumnName(prop, style)` and `ColumnDef.MappedName` is populated so the
    snapshot re-emits `.MapTo(...)` and `SchemaHasher`/`RenameMatcher` stay faithful.
  - A `Ref`'s foreign-key constraint column now reuses the resolved column name, so it stays consistent
    when a Ref uses `MapTo`/`NamingStyle`.
- This fixes both `migrate add` and `migrate diff` (both call `ProjectSchemaReader.ExtractSchemaSnapshot`).

No changes were needed to `SchemaDiffer`, `DdlRenderer`, `MigrationCodeGenerator`, `SnapshotCodeGenerator`,
or the `ColumnDef` model — they already consume `Name`/`MappedName` correctly (the migration DDL name is
generated from `ColumnDef.Name`). The runtime `SchemaParser` and the `Quarry.Migration/SchemaResolver`
(SchemaMap for result materialization) already honored both mechanisms and are untouched.

## Plan items implemented as specified
- Honor `NamingStyle` and `MapTo` in `ProjectSchemaReader`; de-mask the three naming tests in
  `ProjectSchemaReaderIndexTests` (fictional `NamingStyle Naming` → the real `override NamingStyle`).
- Unit coverage for `NamingStyle` and `MapTo` (chained, standalone-generic, override-vs-style precedence,
  and Exact regression).
- Diff coverage proving a `MapTo` add/remove produces a rename-or-drop+add, never a silent no-op.
- End-to-end guard compiling the real committed `AccountSchema` (`credit_limit`) and asserting the
  extracted name and the generated migration code carry the physical name — matching the runtime SQL
  already covered by `CrossDialectSchemaTests.Select_MapToColumn_CreditLimit`.

## Gaps in original plan implemented
- The issue's third location bullet (`DatabaseSchemaReader.cs` / `NormalizeForDiff`) was **stale** — no such
  file/method exists. The diff already keys columns off `ColumnDef.Name` in `SchemaDiffer`, so once `Name`
  carries the physical name, add/remove-`MapTo` diffs stop being no-ops with no change needed there.
- Review found the initial `NamingStyle` detection was *more permissive* than the runtime (it also read
  initializer/getter-arrow forms and skipped the override guard) — itself a divergence risk. Tightened to
  mirror the runtime exactly, with added parity tests for getter-arrow, non-override, and non-literal-`MapTo`
  forms.

## Breaking Changes
- Consumer-facing: A project that already ran `migrate add` on a schema using `NamingStyle` or `MapTo` has
  snapshots recorded with **PascalCase** (property) column names. After this fix, its **next** `migrate add`
  will show a physical-name diff — column rename (or drop+add), foreign-key constraint-name shifts
  (`FK_{table}_{physicalName}`), and a changed snapshot hash chain (`SchemaHasher` includes `MappedName`).
  Those projects were already broken (their migrations diverged from what the runtime queries), so this is a
  one-time **correction**, not a regression. Projects using `Exact` naming and no `MapTo` are unaffected
  (verified: the committed `1_SimpleMigration`, `Quarry.Sample.Aot`, `Quarry.Sample.WebApp` migrations use
  `Exact`; the snake_case and `MapTo` samples have no committed migrations).
- Internal: none.
