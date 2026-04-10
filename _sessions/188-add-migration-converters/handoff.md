# Work Handoff: 188-add-migration-converters

## Key Components

- **Quarry.Migration** (`src/Quarry.Migration/`) — The target project. Currently has a complete Dapper converter pipeline.
- **Quarry.Shared SQL Parser** (`src/Quarry.Shared/Sql/Parser/`) — Shared SQL parser, used by Dapper converter and will be reused by ADO.NET converter.
- **Quarry.Migration.Tests** (`src/Quarry.Migration.Tests/`) — Test project using NUnit, compilation-based testing pattern.

### Existing Dapper Pipeline (reference pattern for all new converters):
| File | Role |
|------|------|
| `DapperDetector.cs` | Finds Dapper method invocations via Roslyn |
| `DapperCallSite.cs` | DTO for detected call site (SQL, params, method, location) |
| `ChainEmitter.cs` | Translates SQL AST → Quarry chain C# code |
| `DapperConverter.cs` | Public facade orchestrating detector → parser → emitter |
| `DapperMigrationAnalyzer.cs` | Roslyn DiagnosticAnalyzer reporting QRM001-003 |
| `DapperMigrationCodeFix.cs` | CodeFixProvider for QRM001/002 |
| `MigrationDiagnosticDescriptors.cs` | Diagnostic ID definitions |
| `SchemaResolver.cs` | Discovers Quarry schema classes in compilation |
| `SchemaMap.cs` | Table→entity mapping used by emitters |
| `ConversionResult.cs` | Internal result DTO (chain code + diagnostics) |

## Completions (This Session)
- Explored full architecture of existing Dapper converter pipeline
- Confirmed design decisions (ISqlCallSite interface, diagnostic ID ranges, detection strategies)
- Created 7-phase implementation plan in `plan.md`

## Previous Session Completions
(none — first session)

## Progress
- INTAKE: Complete
- DESIGN: Complete (6 decisions recorded)
- PLAN: Complete (7 phases, approved)
- IMPLEMENT: Not started (0/7 phases)

## Current State
No code changes made yet. Working tree is clean. All 3112 baseline tests pass.

## Known Issues / Bugs
None.

## Dependencies / Blockers
- #185 (Dapper converter): CLOSED — architecture is established
- #182 (Shared SQL parser): CLOSED — parser exists in Quarry.Shared

## Architecture Decisions

1. **ISqlCallSite interface** — ChainEmitter currently takes `DapperCallSite` directly. Phase 1 extracts a small interface (`Sql`, `ParameterNames`, `MethodName`) so both `DapperCallSite` and `AdoNetCallSite` can be used. This is the only refactoring of existing code.

2. **EF Core uses a new emitter** — LINQ-to-chain translation (C# to C#) is fundamentally different from SQL-to-chain. Lambda bodies are preserved as-is since Quarry uses the same expression pattern. `EfCoreEmitter` handles method chain walking and mapping.

3. **ADO.NET reuses ChainEmitter** — Since ADO.NET starts from raw SQL (CommandText), it follows the same SQL→parse→emit path as Dapper. The detector is more complex (multi-statement tracking within a method body) but the emitter is the same.

4. **SqlKata needs a new emitter** — String-based fluent API → lambda-based chain API. The key transformation is resolving string column names to property access via SchemaMap.

5. **Diagnostic IDs at 100-offset** — Dapper=QRM001-003, EF Core=QRM101-103, ADO.NET=QRM201-203, SqlKata=QRM301-303. Leaves room for expansion within each converter.

6. **ADO.NET detection scoped to single method body** — Track DbCommand variable, collect CommandText assignment and Parameters.Add calls within the same method. Cross-method tracking is out of scope.

## Open Questions
None — all design questions resolved.

## Next Work (Priority Order)

1. **Phase 1: ISqlCallSite extraction** — Create `ISqlCallSite.cs` interface. Make `DapperCallSite` implement it. Update `ChainEmitter.Translate()` and related methods to use `ISqlCallSite` instead of `DapperCallSite`. Run all existing tests to confirm no regressions.

2. **Phase 2: EF Core Detector** — Create `EfCoreCallSite.cs` and `EfCoreDetector.cs`. Create `EfCoreDetectorTests.cs`. Detection strategy: find terminal LINQ methods, walk chain backward to DbSet<T> root, collect intermediate calls.

3. **Phase 3: EF Core full pipeline** — Create emitter, facade, analyzer, code fix. Add QRM101-103 descriptors. Create converter and emitter tests.

4. **Phase 4: ADO.NET Detector** — Create `AdoNetCallSite.cs` (implements `ISqlCallSite`) and `AdoNetDetector.cs`. Create `AdoNetDetectorTests.cs`. Detection: find Execute* calls, trace back for CommandText + Parameters.

5. **Phase 5: ADO.NET full pipeline** — Create facade, analyzer, code fix. Add QRM201-203 descriptors. Create converter tests.

6. **Phase 6: SqlKata Detector** — Create `SqlKataCallSite.cs` and `SqlKataDetector.cs`. Create `SqlKataDetectorTests.cs`. Detection: find terminal methods, walk back to `new Query("table")`.

7. **Phase 7: SqlKata full pipeline** — Create emitter, facade, analyzer, code fix. Add QRM301-303 descriptors. Create converter and emitter tests.
