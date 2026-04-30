# Plan: generator-benchmarks

## Overview

Add benchmarks that measure the cost of running `QuarryGenerator` against representative corpora, hosted inside the existing `Quarry.Benchmarks` project so output flows through the existing BenchmarkConfig → JSON → Quarry.Benchmarks.Reporter → gh-pages publish pipeline unchanged.

The plan is split into seven independently committable phases. Phases 1–2 set up the project plumbing and a reusable harness. Phase 3 builds the shared corpus fixture. Phases 4–6 add one benchmark class each, in order of complexity. Phase 7 verifies the end-to-end run produces clean BenchmarkDotNet output and the published filter (`Quarry_*`) picks them up.

## Key concepts

**Driver pattern.** Mirrors `GeneratorTests.CreateCompilation` and `IncrementalCachingTests`. The generator is invoked by constructing a `CSharpCompilation` from parsed `SyntaxTree`s plus a fixed reference set, wrapping a fresh `QuarryGenerator` in a `CSharpGeneratorDriver`, and calling `RunGeneratorsAndUpdateCompilation`. To keep the measurement focused on the generator rather than Roslyn parsing, parsing of all corpus syntax trees and the reference list happens once in `[GlobalSetup]`. Each `[Benchmark]` iteration only re-creates the immutable `CSharpCompilation` and a fresh `CSharpGeneratorDriver` and runs the driver. This is the same boundary used by every Roslyn-source-generator benchmark in the wild.

**Frozen, versioned corpus.** Corpora live under `src/Quarry.Benchmarks/Corpora/v1/` as `.cs.txt` files. The `.cs.txt` extension prevents the bench project from compiling them; they are added as `<EmbeddedResource>` and pulled out at `[GlobalSetup]` via `Assembly.GetManifestResourceStream`. The `v1` folder name is intentional — when the corpus is intentionally extended, a `v2` folder is added and the benchmarks switch to it in a separate, deliberate change.

**Hybrid corpus shape.** A small handwritten fixture (`Corpora/v1/Fixture/`) provides a single `[QuarryContext(Dialect = SqlDialect.PostgreSQL)]` partial context plus five schemas (User, Order, OrderItem, Product, Address). Type names match `Quarry.Tests.Samples` so query snippets ported from cross-dialect tests reference the same `User`/`Order`/etc. types. Throughput and PipelineSplit corpora consist of one or more files containing query-call-site bodies and (for PipelineSplit) `[Migration]` classes that reference the fixture types.

**Method naming.** All `[Benchmark]` methods are `Quarry_*`-prefixed — the gh-pages publish filter at `.github/workflows/benchmark.yml:181` selects only methods matching `startswith("Quarry_")` for the tracked time-series.

## Reference set

Each `CSharpCompilation` needs the same references the existing generator tests use (see `GeneratorTests.cs:23-37` and `IncrementalCachingTests.cs:73-86`):
- `typeof(Quarry.Schema).Assembly.Location`
- `typeof(object).Assembly.Location`
- `typeof(System.Data.IDbConnection).Assembly.Location`
- From `RuntimeEnvironment.GetRuntimeDirectory()`: `System.Runtime.dll`, `System.Collections.dll`, `System.Linq.dll`, `System.Linq.Expressions.dll`, `netstandard.dll`, `System.Threading.Tasks.dll`

Built once in `[GlobalSetup]`, reused across iterations.

## Phases

### Phase 1 — Project plumbing

Add to `src/Quarry.Benchmarks/Quarry.Benchmarks.csproj`:
- `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />` (matches the version pinned in `Quarry.Generator.csproj` and `Quarry.Tests.csproj`).
- `<EmbeddedResource Include="Corpora\v1\**\*.cs.txt" />` (placeholder — files added in Phase 3).

The existing `<ProjectReference Include="..\Quarry.Generator\Quarry.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="true" />` already exposes `QuarryGenerator` as a runtime-callable type, so no further reference work is required. The existing `MSB3277` suppression already covers the Roslyn-vs-BenchmarkDotNet version mismatch.

**Tests added:** none (this phase is build-config only — verified by `dotnet build src/Quarry.Benchmarks` succeeding).

**Dependencies:** none.

### Phase 2 — `GeneratorBenchmarkBase` harness

Add `src/Quarry.Benchmarks/Generator/GeneratorBenchmarkBase.cs`. This class provides the shared compile/drive primitives so the three benchmark classes share zero logic but stay terse.

Public surface:
- `protected static string LoadCorpus(string resourceName)` — reads an embedded `.cs.txt` resource by short name (e.g. `"Fixture.UserSchema"`); throws if not found.
- `protected static SyntaxTree Parse(string source, string path)` — `CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: path)`.
- `protected static IReadOnlyList<MetadataReference> BuildReferences()` — builds the reference list described above (single static cache).
- `protected static CSharpCompilation BuildCompilation(IEnumerable<SyntaxTree> trees, IReadOnlyList<MetadataReference> refs)` — returns a `DynamicallyLinkedLibrary` compilation with `NullableContextOptions.Enable`.
- `protected static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)` — wraps a fresh `QuarryGenerator` in a fresh driver and returns the run result. **This is the method `[Benchmark]` calls.**

The harness deliberately does *not* hold global state — each benchmark class owns its parsed `SyntaxTree[]` in its own `[GlobalSetup]`. This keeps benchmark classes independent (BenchmarkDotNet may instantiate them in any order).

**Tests added:** `src/Quarry.Tests/Generation/GeneratorBenchmarkHarnessTests.cs` (small smoke test): parses a trivial inline source, runs `RunGenerator`, asserts that at least one `GeneratedTree` is produced. Guards against a future regression where the benchmark harness silently runs the generator with mismatched references and produces zero output (which would make every benchmark a no-op while still reporting timings).

**Dependencies:** Phase 1.

### Phase 3 — Shared fixture corpus

Author `Corpora/v1/Fixture/` files:
- `UserSchema.cs.txt`, `OrderSchema.cs.txt`, `OrderItemSchema.cs.txt`, `ProductSchema.cs.txt`, `AddressSchema.cs.txt` — each declares a `Schema`-derived class with a representative spread of `Key`, `Col`, nullable, length-bounded, and FK-ish columns. Names and types match the equivalents in `Quarry.Tests/Samples/` so ported query snippets compile.
- `BenchDbContext.cs.txt` — single `[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]` partial class declaring `IEntityAccessor<User> Users()`, etc.

These files are added to the csproj `EmbeddedResource` glob from Phase 1.

**Tests added:** Extend `GeneratorBenchmarkHarnessTests` with `Fixture_Compiles_AndGeneratesEntityClasses` — loads all six fixture files, runs the generator, asserts that `User`, `Order`, `OrderItem`, `Product`, `Address` `.g.cs` trees are produced and an interceptor file is emitted for the context. This is the regression guard: if a future fixture edit breaks the generator, this test fails before any benchmark numbers go weird.

**Dependencies:** Phase 2.

### Phase 4 — `GeneratorColdCompileBenchmarks`

Add `src/Quarry.Benchmarks/Generator/GeneratorColdCompileBenchmarks.cs`. Single `[Benchmark]` method `Quarry_GeneratorColdCompile`. `[GlobalSetup]` parses fixture + a Medium-size query block (loaded from `Corpora/v1/Throughput/Medium.cs.txt`, which is built in Phase 5 — see Dependencies below). The benchmark method calls `BuildCompilation` and `RunGenerator`. `[MemoryDiagnoser]` attribute on the class.

Note on phase ordering: the Medium corpus file is needed for ColdCompile but is authored in Phase 5. To keep phases atomic, **the Medium file is the first artifact authored in Phase 5 and the ColdCompile class is added in Phase 4 against a placeholder Medium file containing a single trivial query**. Phase 5 then expands Medium and authors Small/Large. This keeps each phase independently committable with green tests.

**Tests added:** none (the benchmark harness test from Phase 2/3 already covers the codepath — adding a "test that runs a benchmark" provides no signal beyond what the harness test gives).

**Dependencies:** Phase 3.

### Phase 5 — `GeneratorThroughputBenchmarks` + Throughput corpora

Author `Corpora/v1/Throughput/Small.cs.txt` (~10 query call sites), expand `Medium.cs.txt` to ~50, author `Large.cs.txt` (~200). Snippets are ported from `CrossDialectAggregateTests`, `CrossDialectJoinTests`, `CrossDialectCteTests`, `CrossDialectWindowFunctionTests`, `CrossDialectSelectTests`, `CrossDialectWhereTests`, `CrossDialectSubqueryTests`, with the dialect-tuple wrapper stripped (single PG context). Each query is wrapped in a method body inside a single `BenchmarkUsageSites` static class with method names `Q1`…`Q200` so identifiers stay unique.

Add `src/Quarry.Benchmarks/Generator/GeneratorThroughputBenchmarks.cs` with three `[Benchmark]` methods:
- `Quarry_Throughput_Small`
- `Quarry_Throughput_Medium`
- `Quarry_Throughput_Large`

Each runs the full fixture + the corresponding corpus file. `[GlobalSetup]` parses all three corpora once. The body of each `[Benchmark]` builds a fresh compilation from the matching pre-parsed trees and runs the driver.

**Tests added:** Extend the harness test with `Throughput_Corpora_Compile` — loads each of Small/Medium/Large together with the fixture, runs the generator, asserts no compilation errors and at least one interceptor `.g.cs` per corpus. Catches a malformed snippet without needing to run a benchmark.

**Dependencies:** Phase 4 (because Phase 4 authored a placeholder `Medium.cs.txt` that this phase expands).

### Phase 6 — `GeneratorPipelineSplitBenchmarks` + split corpora

Author `Corpora/v1/PipelineSplit/`:
- `SchemaOnly.cs.txt` — empty file (just a `// intentionally empty` comment). The fixture alone is the schema-only input.
- `PlusQueries.cs.txt` — same content as `Corpora/v1/Throughput/Medium.cs.txt` (~50 query call sites). Note: keep it as a separate file so a future change to Medium doesn't silently shift the split numbers.
- `PlusMigrations.cs.txt` — `PlusQueries.cs.txt` content + ~10 `[Migration]` and one `[MigrationSnapshot]` declarations against the fixture types. Uses the same patterns as `Quarry.Migration.Tests` fixtures.

Add `src/Quarry.Benchmarks/Generator/GeneratorPipelineSplitBenchmarks.cs` with three `[Benchmark]` methods:
- `Quarry_Pipeline_SchemaOnly`
- `Quarry_Pipeline_PlusQueries`
- `Quarry_Pipeline_PlusMigrations`

Cost differences attribute to: SchemaOnly = Pipeline 1 baseline; PlusQueries − SchemaOnly = Pipeline 2 (Interceptors); PlusMigrations − PlusQueries = Pipeline 3 (Migrations).

**Tests added:** Extend harness test with `PipelineSplit_Corpora_Compile` — for each of SchemaOnly/PlusQueries/PlusMigrations, asserts that the generator emits the expected output kind: SchemaOnly produces entity `.g.cs` files but no interceptor file with non-trivial content; PlusQueries adds interceptor file(s); PlusMigrations adds a migration `.g.cs` file. Catches a corpus regression that would silently zero-out a pipeline.

**Dependencies:** Phase 5.

### Phase 7 — Verify end-to-end benchmark output

Run `dotnet run --project src/Quarry.Benchmarks -c Release -- --filter '*Generator*' --artifacts BenchmarkDotNet.Artifacts` locally to verify:
1. All seven `Quarry_*` benchmark methods run to completion without errors.
2. `BenchmarkDotNet.Artifacts/results/*-report-full.json` contains entries for each.
3. Filtering `select(.Method | startswith("Quarry_"))` (the publish filter) returns the new entries — i.e. they will land in the gh-pages dataset.
4. The `MemoryDiagnoser` produces non-zero `BytesAllocatedPerOperation` for each (sanity check that allocation tracking works for generator runs).

This phase produces no new code; it is a verification gate before transitioning to REVIEW. Findings get amended into earlier phases if anything is broken.

**Tests added:** none.

**Dependencies:** Phases 1–6.

## Phase summary table

| Phase | Deliverable | Test additions | Depends on |
|------|------------|---------------|-----------|
| 1 | csproj edits | none | — |
| 2 | GeneratorBenchmarkBase | smoke harness test in Quarry.Tests | 1 |
| 3 | Fixture corpus (6 files) | fixture-compiles test | 2 |
| 4 | ColdCompile bench (placeholder Medium) | none | 3 |
| 5 | Throughput bench + 3 corpora | throughput-compiles test | 4 |
| 6 | PipelineSplit bench + 3 corpora | pipeline-split-compiles test | 5 |
| 7 | End-to-end run verification | none | 1–6 |

`phases-total: 7`
