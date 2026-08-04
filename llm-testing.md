# Quarry Testing — LLM Reference

How the Quarry test suite is laid out and how to write new tests against it. Prerequisites: read `llm.md` for surface API and `src/Quarry.Generator/llm.md` for generator internals if you are writing codegen tests.

Two test projects: `Quarry.Tests` (runtime + generator) and `Quarry.Migration.Tests` (cross-ORM converters + analyzer code fixes). NUnit throughout. Tests are parallelizable — each test owns its harness/connection state.

## Test Project Layout (`src/Quarry.Tests/`)

| Folder | What lives there |
|---|---|
| `SqlOutput/` | Cross-dialect SQL verification + execution. The primary regression gate — `CrossDialect*.cs` files (one per feature area). Each test exercises **all 4 dialects**: SQL string equality plus a real execute-and-verify against each backend. |
| `Generation/` | Generator output assertions (`CarrierGenerationTests`, `ConditionalCarrierTests`, `MaskAwareTerminalBindingTests`, `ManifestEmitterTests`). Inspect generated interceptor source text — **no DB execution**. |
| `IR/` | Pipeline-stage unit tests for binders, translators, assemblers, carrier analysis, structural keys. |
| `Parsing/` | Discovery + closure analysis (`DisplayClassEnricherTests`, `VariableTracerTests`). |
| `Integration/` | Testcontainers infrastructure: `PostgresTestContainer`, `MySqlTestContainer`, `MsSqlTestContainer`, plus integration-specific suites that need their own DB lifecycle (parameter binding, smoke tests, ANSI-mode MySQL variants). |
| `Migration/` | `MigrationBuilder`, `SchemaDiffer`, `MigrationRunner` cross-dialect tests, snapshot codegen, DDL renderer. |
| `Scaffold/` | Reverse-engineer pipeline (`SqliteIntrospector`, `JunctionTableDetector`, `Singularizer`, etc.). |
| `ManifestOutput/` | Golden files (`quarry-manifest.{sqlite,postgresql,mysql,sqlserver}.md`) snapshotted via the SQL manifest pipeline. |
| `Samples/` | Schema classes (`UserSchema`, `OrderSchema`, …) plus per-dialect context types (`TestDbContext`, `Pg.PgDb`, `My.MyDb`, `Ss.SsDb`). |
| `Testing/` | `TestCallSiteBuilder`, `TestPlanHelper` — fluent builders for constructing pipeline IR objects directly in unit tests. |
| `Utilities/` | `TypeClassificationTests` and similar. |

Top-level files: `QueryTestHarness.cs` (4-dialect harness), `GeneratorTests.cs`, `RawSqlGeneratorPipelineTests.cs`, `TypeMappingGeneratorTests.cs`, plus type/SQL-parser/dialect smoke suites.

## Three Tiers of Test

| Tier | What it proves | Where | DB? |
|---|---|---|---|
| **Codegen unit** | Generator emits the expected interceptor source (carrier shape, mask variant count, SQL string body) | `Generation/`, `IR/`, `Parsing/` | No — synthetic in-memory compilation via `CSharpCompilation.Create` |
| **Cross-dialect SQL** | The same chain compiles to the right SQL on each of the 4 dialects | `SqlOutput/CrossDialect*.cs` | Yes (all 4) |
| **Integration** | End-to-end execution against a real database, asserting row state / affected counts | `SqlOutput/CrossDialect*.cs` (post-`AssertDialects` execute blocks), `Integration/*IntegrationTests.cs` | Yes |

The middle and bottom tiers share the same test methods — `CrossDialect*.cs` tests do both. **Pure codegen tests never execute SQL.**

## QueryTestHarness — the 4-dialect harness

`Quarry.Tests/QueryTestHarness.cs`. Self-contained per-test harness exposing four context properties:

| Property | Type | Backing |
|---|---|---|
| `Lite` | `TestDbContext` (SQLite dialect) | Real `SqliteConnection` to `Data Source=:memory:` |
| `Pg` | `Pg.PgDb` | Real `NpgsqlConnection` to shared Testcontainers PG 17 baseline |
| `My` | `My.MyDb` | Real `MySqlConnection` to shared Testcontainers MySQL 8.4 baseline |
| `Ss` | `Ss.SsDb` | Real `SqlConnection` to shared Testcontainers SQL Server 2022 baseline |
| `MockConnection` | `MockDbConnection` | Backs `SchemaQualified*` contexts for SQL-only inspection (no execution) |

Deconstructs to `(Lite, Pg, My, Ss)`. **Each test creates its own harness** via `await using var t = await QueryTestHarness.CreateAsync();`. No shared mutable state across tests.

### Schema isolation modes

`CreateAsync(useOwnPgSchema, useOwnMyDatabase, useOwnSsSchema)` — all default to `false`.

| Mode (default) | Mechanism | When to use |
|---|---|---|
| Transactional | Connection opens against shared baseline (`quarry_test` schema / database / user). Test wraps in transaction; dispose ROLLBACKs. | Almost every test — near-zero per-test cost, perfect isolation. |
| Owned schema | A uniquely-named schema is provisioned with its own DDL + seed; dropped on dispose. | Tests that issue their own `BEGIN`/`COMMIT` (migration runner), or that depend on COMMIT-visible state. |

SQLite is always in-memory and torn down on dispose (no transaction wrapper needed — disposal destroys the database).

### Baseline schema bootstrap

Each container helper (`PostgresTestContainer`, `MySqlTestContainer`, `MsSqlTestContainer`) starts **one container per test process** and seeds **one shared baseline** the first time `EnsureBaselineAsync` is called. Cross-process safety: PG uses `pg_advisory_lock`, MySQL uses `GET_LOCK`, SQL Server uses a sentinel-table probe + retry. The seed is identical across all dialects (port of the SQLite DDL/seed in `QueryTestHarness.CreateSchema`/`SeedData`).

| Table | Seeded rows |
|---|---|
| `users` | Alice (id=1, active, email), Bob (id=2, active, NULL email), Charlie (id=3, inactive, email) |
| `orders` | 3 rows belonging to users 1 & 2 |
| `order_items` | 3 rows tied to the 3 orders |
| `accounts` | 3 rows; balances and credit limits with non-zero decimal precision |
| `events`, `addresses`, `user_addresses`, `warehouses`, `shipments`, `products` | small fixed fixtures |

Notes:
- PG primary keys are `GENERATED BY DEFAULT AS IDENTITY`; after seeding with explicit IDs the IDENTITY sequence is bumped past the max so subsequent auto-inserts don't collide. MySQL/SS follow the same pattern with their own auto-id flavours.
- PG/MySQL boolean columns are `BOOLEAN` / `TINYINT(1)`; SQLite/SS use integer 0/1. The dialect formatter renders `TRUE/FALSE` on PG, `0/1` elsewhere.
- Decimals: `NUMERIC(18,2)` everywhere except SQLite (`REAL`).
- FOREIGN KEY constraints from the SQLite source are deliberately omitted on PG/My/Ss because SQLite runs with `PRAGMA foreign_keys = OFF` in the harness. Replicating them would break tests that mutate parent rows without cleaning up children.

### Per-dialect connection quirks

- **MySQL:** Connection string forces `IgnoreCommandTransaction=true`. Quarry-emitted `DbCommand`s do not set `DbCommand.Transaction`; MySqlConnector otherwise throws "transaction associated with this command is not the connection's active transaction". Production consumers pooling Quarry on a transacted MySQL connection need the same option.
- **SQL Server:** Transactional mode issues raw `BEGIN TRANSACTION` SQL rather than `SqlConnection.BeginTransaction()`. SqlClient requires every `SqlCommand` against a connection-with-`SqlTransaction` to have its `.Transaction` property assigned — Quarry doesn't, so a raw server-side transaction sidesteps the client-side check.
- **PG:** Search path is set to either `quarry_test` (baseline) or the unique owned schema. Connection is taken from Npgsql's pool — tests must not depend on the same physical connection across calls.
- **SQLite:** `:memory:` per-harness, FKs OFF by default. Override per-test with `harness.SqlAsync("PRAGMA foreign_keys = ON;")`.

### Test pattern (recipe)

```csharp
[Test]
public async Task Some_CrossDialect_Test()
{
    await using var t = await QueryTestHarness.CreateAsync();
    var (Lite, Pg, My, Ss) = t;

    // 1. Build the same chain on each dialect, terminating in .Prepare() for reuse.
    var lt = Lite.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == 1).Prepare();
    var pg = Pg.Users()  .Update().Set(u => u.UserName = "x").Where(u => u.UserId == 1).Prepare();
    var my = My.Users()  .Update().Set(u => u.UserName = "x").Where(u => u.UserId == 1).Prepare();
    var ss = Ss.Users()  .Update().Set(u => u.UserName = "x").Where(u => u.UserId == 1).Prepare();

    // 2. Assert exact SQL on all 4 dialects in one call (uses Assert.Multiple under the hood).
    QueryTestHarness.AssertDialects(
        lt.ToDiagnostics(), pg.ToDiagnostics(), my.ToDiagnostics(), ss.ToDiagnostics(),
        sqlite: "UPDATE \"users\" SET \"UserName\" = 'x' WHERE \"UserId\" = 1",
        pg:     "UPDATE \"users\" SET \"UserName\" = 'x' WHERE \"UserId\" = 1",
        mysql:  "UPDATE `users` SET `UserName` = 'x' WHERE `UserId` = 1",
        ss:     "UPDATE [users] SET [UserName] = 'x' WHERE [UserId] = 1");

    // 3. Execute and verify affected rows + row state.
    Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
    Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
    // … etc
}
```

`AssertDialects` overloads take either raw SQL strings or `QueryDiagnostics`. Both use `Assert.Multiple` so all four dialects report failures in one pass.

### Row order on the real providers

**PostgreSQL, MySQL InnoDB and SQL Server do not guarantee row order without a top-level `ORDER BY`.** SQLite's incidental insertion-order return shape is the deliberate reference — the SQLite side of a mirror test stays positional — but a passing `pgResults[0]` / `myResults[1]` / `ssResults[2]` assertion on an unordered query is a latent flake that a planner change (statistics refresh, parallel scan, a hash join chosen for a CTE) can break with no code change.

Sort the materialised list with `RowOrderExtensions.SortedByAsync` on the **real-provider sides only**:

```csharp
var results   = await lt.ExecuteFetchAllAsync();                              // SQLite: positional
var pgResults = await pg.ExecuteFetchAllAsync().SortedByAsync(r => r.UserId); // PG/My/Ss: sorted
```

It is an extension on `Task<List<T>>` rather than on `PreparedQuery<T>` so the chain analyzer still sees `.ExecuteFetchAllAsync()` as the literal terminal — wrapping the chain itself would hide the terminal and trip QRY036.

Rules of thumb when adding or converting a test:

- **The key must be a total order over the rows the query actually returns.** A join that yields the same user twice ties on `UserName`; use the primary key, or a composite tuple key: `.SortedByAsync(r => (r.ProductName, r.UserName))`.
- **Do not sort a query that already has a top-level `ORDER BY`.** Re-sorting in C# would mask a regression that drops the `ORDER BY` — which is the very thing those tests pin. An `ORDER BY` inside a window `OVER (...)`, a subquery, or a CTE body does *not* order the outer result set and does not count.
- **Sorting cannot fix nondeterministic row *selection*.** `LIMIT`/`OFFSET` with no `ORDER BY` returns an arbitrary subset, and `ExecuteFetchFirstAsync` over a multi-row predicate returns an arbitrary row. Those need a query-side `.OrderBy(...)` — note it goes *after* `Select` and takes the source-entity lambda: `Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserId)`. Ordering on a literal column adds no parameter, so existing parameter indices are unaffected.
- **Never reorder or rewrite assertions to fit a key.** If no ascending key reproduces the asserted sequence, the order is encoded in a column the projection does not carry — that is a query-side or assertion-side fix, not a sort. `CrossDialectJoinTests` has nine such tests, tracked in #332; its `<remarks>` block explains why a plausible-looking composite key silently reorders them.
- Order-independent assertions (`Is.EquivalentTo`, `Does.Contain`, `.First(predicate)`, `.All(...)`, count-only) need no sort at all — much of the suite is already written this way.

### Per-dialect entity types

Each dialect has its own context partial (`PgDb`, `MyDb`, `SsDb`, `TestDbContext`), so the generator emits **distinct CLR entity types per context** in the context's namespace:

- `Quarry.Tests.Samples.User` — SQLite
- `Quarry.Tests.Samples.Pg.User` — PG
- `Quarry.Tests.Samples.My.User` — MySQL
- `Quarry.Tests.Samples.Ss.User` — SQL Server

When constructing entity literals per dialect (e.g. `Set(new User { … })`), use the fully qualified name: `Lite` uses `User`, `Pg` uses `Pg.User`, etc. The schema class (`UserSchema`) is shared.

## Parameterized variants

Use NUnit `[TestCase]` to fan out a single test method across mask/flag permutations. Each case gets its own harness so DB state is fresh:

```csharp
[TestCase(true,  true)]
[TestCase(true,  false)]
[TestCase(false, true)]
[TestCase(false, false)]
public async Task Some_ConditionalChain(bool flagA, bool flagB)
{
    await using var t = await QueryTestHarness.CreateAsync();
    var (Lite, Pg, My, Ss) = t;
    // …
}
```

The conditional `Set`/`Where` interceptors fire only when the C# branch is taken, OR'ing their bit into the carrier's `Mask` field. Each mask value selects a different pre-rendered SQL variant from `_sql[Mask]`. A passing single-case test only proves one variant of `_sql[]` — fan out over the bool inputs and assert each variant's SQL + post-execute row state. Codegen-only tests in `Generation/` can assert variant counts via helpers like `AssertMaskVariantCount`; integration tests in `SqlOutput/` should additionally execute each variant and check observable effects.

## Codegen-only unit tests

`Generation/CarrierGenerationTests.cs` is the canonical pattern for testing generator output without a DB:

```csharp
var source = SharedSchema + queryCode;          // synthetic source
var compilation = CreateCompilation(source);    // CSharpCompilation with Quarry refs
var result = RunGenerator(compilation);         // run QuarryGenerator
var tree = result.GeneratedTrees
    .FirstOrDefault(t => t.FilePath.EndsWith(".g.cs") && t.FilePath.Contains(".Interceptors."));
var code = tree!.GetText().ToString();
Assert.That(code, Does.Contain("file sealed class Chain_"));
```

`RunGeneratorWithDiagnostics` returns both the run result and Roslyn diagnostics for tests that assert QRY error/warning emission. `ConditionalCarrierTests.cs` adds helpers like `AssertPrebuiltDispatchWithMask` and `AssertMaskVariantCount` for the conditional-mask pipeline.

The Quarry runtime + System.Runtime + netstandard references are wired in `CreateCompilation`; pass extra sources via params for tests that need multiple files.

## Docker availability

All container helpers wrap their startup in an `IsDockerUnavailable(ex)` heuristic. When Docker isn't running:

```csharp
Assert.Ignore("Docker is not available on this machine — …");
```

Container-backed tests are skipped, not failed. The reason is cached per-process so later attempts don't pay the probe timeout.

When Docker IS available, container startup is **deferred to first use** and amortized over the whole run — one PG + one MySQL + one SQL Server container per process, kept alive until the test runner exits.

## SQL Manifest tests

`ManifestOutput/quarry-manifest.{dialect}.md` are checked-in goldens. Enabling `<QuarrySqlManifestPath>` on `Quarry.Tests.csproj` regenerates them on build; the generator's `WriteIfChanged` guard suppresses no-op diffs. Treat unexpected manifest churn the same way you'd treat unexpected SQL: regression first, then update the goldens if the new SQL is intentionally correct.

**CI enforces this.** `.github/workflows/ci.yml` runs `git diff --exit-code -- src/Quarry.Tests/ManifestOutput` after the test step, so a build that regenerates a golden fails the workflow unless the regenerated file is committed. Adding or changing a chain in `Quarry.Tests` regenerates the goldens — build locally and commit them with the change. Note that appending a call *after* the terminal (e.g. `SortedByAsync`) does not, since it adds no chain.

## Runtime-behaviour suites

Three suites assert execution behaviour rather than SQL text, and each has a specific trap worth knowing before extending it.

| Suite | What it guards |
|---|---|
| `Integration/ConcurrencyTests.cs` | Parallel harnesses running mixed SELECT/UPDATE/Patch, a barrier-synchronised first touch of one shared carrier chain, and parallel read-only contexts across all four dialects. Regression insurance for shared runtime state. |
| `SqlOutput/CrossDialectStreamingTests.cs` | `ToAsyncEnumerable` early-`break` disposal. The assertion with teeth is the **follow-up query on the same harness connection** — a leaked reader poisons it (MySqlConnector forbids a second command with an open reader; SqlClient needs MARS). |
| `Integration/CancellationTests.cs` | Pre-cancelled tokens into every fetch terminal, and mid-stream cancellation of `ToAsyncEnumerable`. |

- **Concurrent writes must stay on SQLite.** `QueryTestHarness.SqlAsync`/`CreateSchema`/`SeedData` are SQLite-only; PG/MySQL/SQL Server share one pre-seeded baseline plus a per-harness transaction rolled back on dispose. Concurrent writes on the container dialects contend on row locks in the shared baseline and produce timeouts, not findings. Exercise them read-only. Create harnesses **sequentially** and parallelise only the Quarry operations — racing container first-call initialisation tests the fixtures, not the library.
- **Mid-stream cancellation is only observable when the provider awaits I/O.** With three seeded rows PostgreSQL delivers the whole result set in one response, so `while (await reader.ReadAsync(ct))` never awaits again and never sees the token. The strict `OperationCanceledException` assertion is therefore SQLite-only; the all-dialect test asserts connection usability instead.
- **The harness-rollback test does not detect a leaked reader** — the providers tolerate a rollback with a reader outstanding. Do not treat it as disposal coverage.

## Pinning a known bug

Convention (introduced with #328/#329): an **active** test named `KnownBug_Issue{N}_...` that asserts the *current buggy* behaviour, with a comment saying "when this test fails, the bug is fixed — remove the workaround and this pin". It signals by failing at exactly the moment the bug is fixed, so a workaround never outlives its cause. Where the buggy behaviour cannot be asserted stably, fall back to `[Ignore("pinned: #{N}")]` on a test asserting the *correct* behaviour.

For interceptor-binding defects, assert on the **emitted interceptor text**, not on a compiler diagnostic: a receiver-arity mismatch that is a hard `CS9144` error in the full test project raises nothing at all in an isolated `CSharpCompilation` (see `Generation/InterceptorBindingGuardTests.cs`).

## Generator-test fixtures

Schemas/contexts that exist solely to feed generator unit tests live in `Samples/`. `MockDbConnection.cs` provides a non-executing `DbConnection` for `SchemaPg/SchemaMy/SchemaSs*Db` contexts used in SQL-only assertions (schema-qualified tests). The carrier still goes through the full pipeline; `MockDbConnection.LastCommand` exposes what would have been executed.

## Running

```sh
dotnet build src/Quarry.Tests/Quarry.Tests.csproj      # ~50s clean, ~5s incremental
dotnet test  src/Quarry.Tests
dotnet test  src/Quarry.Tests --filter "FullyQualifiedName~CrossDialect"
dotnet test  src/Quarry.Migration.Tests
```

Per-test isolation (transactional Pg/My/Ss + in-memory SQLite) means tests are safe to parallelize. Filter by `FullyQualifiedName~` to narrow to a single suite while iterating.

## Common gotchas

- **The four `Set(...)` overloads on the chain look identical but build distinct dialect chains.** Don't accidentally use `Lite.User` typed entities against `Pg.Users()` — the partial-context types diverge.
- **String literals inline; captured variables parameterize.** `Set(u => u.Name = "x")` emits `SET "Name" = 'x'`. `var x = "x"; Set(u => u.Name = x)` emits `SET "Name" = @p0`. Assert accordingly.
- **`PRAGMA foreign_keys` is OFF on SQLite** by default in the harness. Tests that delete a parent row leaving orphans pass on SQLite and on Pg/My/Ss (no FKs replicated). If you need FK enforcement, opt in per-test on SQLite *and* add explicit FKs to the container DDL.
- **Mask integration tests need to verify each mask value separately.** A passing single-case test only proves one variant of `_sql[]`. Use `[TestCase]` over the bool inputs and assert each variant's SQL + post-execute row state.
- **`q.All()` after a conditional `q = q.Where(...)`** typechecks only because both `IUpdateBuilder<T>` and `IExecutableUpdateBuilder<T>` expose the same conditional-friendly surface in the codegen tests, where the source is a string. In integration tests the C# must compile — put conditional `Set` calls before `.Where()` (or after, on the executable builder) and route to `.All()`/`.Where()` via the un-conditional path.
- **A chain inside a doubly-nested lambda does not compile.** Writing a parallel worker as `harnesses.Select((h, i) => Task.Run(async () => { var name = $"Worker{i}"; … .Set(u => u.UserName = name) … }))` makes the generator emit interceptors that reference `name` directly, but that local lives in a display class the interceptor cannot see — `CS0103: The name 'name' does not exist in the current context` in the generated `*.Interceptors.*.g.cs`. Write each worker body as a named `private static async Task<T> Run…WorkerAsync(...)` method so the chain's captures are ordinary method locals. Tracked as issue #333.
- **A partial chain passed as a method argument is not intercepted, and fails at runtime rather than at build time.** Handing `Lite.Users().OrderBy(...).Select(...)` to a helper that applies the terminal throws `NotSupportedException: Entity accessor methods must be intercepted by the Quarry source generator` — with no build-time diagnostic. The chain must terminate at the call site; pass the terminal's *result* (`IAsyncEnumerable<T>`, `Task<T>`) to helpers instead.
- **A chain consumed by both `ToDiagnostics()` and a terminal needs `.Prepare()`** — otherwise QRY033 "consumed by multiple execution paths" fails the build.
- **Co-locating `ToDiagnostics()` with a conditional clause collapses variants.** Putting `var sql = q.ToDiagnostics().Sql;` inside the same `if (...)` block as a conditional `.Set()` makes the chain analyzer see the terminal at the same nesting depth as the clause — `relativeDepth <= 0` — and the clause is reclassified as unconditional. The mask table degenerates to a single variant. Always call `Prepare()` / `ToDiagnostics()` at the chain's outer scope.
