# Plan: 324 — Migration tool honors NamingStyle and MapTo

## Goal
Make the migration tool's `ProjectSchemaReader` honor the `NamingStyle` override and per-column
`MapTo("physical")`, so `quarry migrate add`/`diff` produce snapshots and DDL whose column names match
the runtime's physical names — and so adding/removing a mapping produces a real diff step, not a silent
no-op.

## Representation (the key concept)
`ColumnDef.Name` becomes the **physical** column name (`MapTo-arg ?? ToColumnName(prop.Name, style)`),
and `ColumnDef.MappedName` is set to the `MapTo` argument (or null). This is required because the
migration DDL is generated from `col.Name` (`MigrationCodeGenerator` → `DdlRenderer`), which ignore
`MappedName`. `SchemaDiffer` keys columns by `Name`, so a changed physical name naturally yields
add/drop/rename steps instead of a no-op. Populating `MappedName` keeps the snapshot artifact's
`.MapTo(...)` and the `SchemaHasher`/`RenameMatcher` inputs faithful. See workflow.md Decisions.

## Steps

- [x] **Step 1 — `ProjectSchemaReader`: honor `NamingStyle` + `MapTo` (+ Ref FK consistency), and de-mask the 3 tests.**
  - `ExtractTableDef`: change the naming-style detection from the fictional `prop.Name == "Naming"` to the
    real `prop.Name == "NamingStyle"`. Parse the value from the expression body (and getter-arrow) as a
    member access (`NamingStyle.SnakeCase` etc.) → `NamingStyleKind`, keeping `Exact` as default. Mirror
    runtime `SchemaParser.ExtractNamingStyle`.
  - `ExtractColumnDef`: add a `MapTo` case to the fluent-chain walk that extracts the first string-literal
    argument → `mappedName`. Extend the inline method-name extraction to also handle `GenericNameSyntax`
    (so standalone `MapTo<T>("x")` is recognized, not only chained `.MapTo("x")`). Compute
    `columnName = mappedName ?? ToColumnName(prop.Name, namingStyle)` and pass `mappedName:` to the
    `ColumnDef` constructor.
  - `ExtractTableDef` Ref branch: use the resolved column name (`colDef?.Name ?? ToColumnName(...)`) for the
    FK constraint column so a MapTo'd/styled Ref column and its FK stay consistent.
  - Update the 3 masking tests (`ProjectSchemaReaderIndexTests.cs` lines ~90, ~377, ~397):
    `public NamingStyle Naming => NamingStyle.SnakeCase;` → `protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;`.
    (These must change together with the reader fix, else they'd fail.)
  - Tests: full existing suite stays green; the de-masked tests now exercise the real API.
  - Depends on: nothing.

- [ ] **Step 2 — Unit coverage: `NamingStyle` and `MapTo` honored (new test file `ProjectSchemaReaderNamingMapToTests.cs`).**
  - `NamingStyle.SnakeCase` via the real override → `TableDef.NamingStyle == SnakeCase`, columns styled
    (`UserName` → `user_name`), `MappedName == null`.
  - `MapTo` chained (`Col<T> X => Mapped<…>().MapTo("x")` and a plain `Identity().MapTo(...)`/`Length().MapTo(...)`)
    → `Name == "x"`, `MappedName == "x"`.
  - `MapTo` standalone generic (`Col<string> X => MapTo<string>("x")`) → `Name == "x"`, `MappedName == "x"`.
  - `MapTo` overrides `NamingStyle` (snake-case schema + a column with `MapTo("explicit")`) → `Name == "explicit"`.
  - No-MapTo / `Exact` → `Name == "PropertyName"`, `MappedName == null` (regression guard).
  - Depends on: Step 1.

- [ ] **Step 3 — Diff coverage: add/remove `MapTo` is not a no-op.**
  - Extract v1 (column with `MapTo("credit_limit")`) and v2 (same column without `MapTo`) via
    `ProjectSchemaReader`; run `SchemaDiffer.Diff(v1, v2)` and assert the result is **non-empty** and
    contains a `RenameColumn` (or a drop+add) for that column — never zero steps. Assert the reverse
    direction (add a mapping) likewise produces steps.
  - Depends on: Step 1.

- [ ] **Step 4 — End-to-end guard: `AccountSchema`/`credit_limit` physical-name parity.**
  - Extract the **real committed** `AccountSchema` via `ProjectSchemaReader` (compile its source with the
    minimal supporting types it needs — `Money`, `MoneyMapping`, `UserSchema`; prefer the real sample
    sources, fall back to inline stubs only if the dependency graph is impractical — decision recorded in
    Working Notes at implementation time). Assert `CreditLimit` column `Name == "credit_limit"` &
    `MappedName == "credit_limit"`, and `Balance` (Mapped, no MapTo) `Name == "Balance"` & `MappedName == null`.
  - Also assert the generated migration/snapshot code for this schema contains `credit_limit` (ties the
    tool's DDL-bound column name to the runtime's physical name; the runtime side is already covered by
    `CrossDialectSchemaTests.Select_MapToColumn_CreditLimit`).
  - Depends on: Step 1.

## Non-goals / notes
- No change to `SchemaDiffer`, `DdlRenderer`, `MigrationCodeGenerator`, `SnapshotCodeGenerator`, or the
  `ColumnDef` model — they already consume `Name`/`MappedName` correctly.
- `Quarry.Migration/SchemaResolver` and `Quarry.Generator/SchemaParser` already honor NamingStyle+MapTo;
  untouched.
- Behavioral-correction note (projects that previously ran `migrate add` on a NamingStyle/MapTo schema
  now see a physical-name diff on their next `migrate add`) goes in the PR body — no CHANGELOG file exists.
