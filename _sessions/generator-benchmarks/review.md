# Review: generator-benchmarks

## Classifications
| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---------|--------------------|-----|-----|-------|--------------|
| 1 | Plan Compliance | .cs.txt → .cs pivot is documented in Decisions | Low | D | D |  |
| 2 | Plan Compliance | Sqlite 10.0.3 → 10.0.5 bump is justified by new project ref | Low | D | D |  |
| 3 | Plan Compliance | BuildReferences matches plan + adds System.Threading.Tasks | Low | D | D |  |
| 4 | Plan Compliance | SchemaOnly corpus is comment-only per plan | Low | D | D |  |
| 5 | Plan Compliance | ColdCompile uses Throughput/Medium per plan | Low | D | D |  |
| 6 | Correctness | RunGenerator discards diagnostics; tests catch it instead | Low | D | D |  |
| 7 | Correctness | RunWith allocates 7-element SyntaxTree[] per iter (~64 B noise) | Low | A | A | Pre-built per-corpus full SyntaxTree[] arrays in [GlobalSetup]; RunWith now does only BuildCompilation+RunGenerator. |
| 8 | Correctness | BuildCompilation accepts IEnumerable; RunWith passes array | Low | D | D |  |
| 9 | Correctness | LoadCorpus path-with-.cs would fail with helpful error | Low | D | D |  |
| 10 | Correctness | ReferencesCache uses Lazy thread-safe correctly | Low | D | D |  |
| 11 | Correctness | GeneratedTrees.Length returned as DCE guard | Low | D | D |  |
| 12 | Correctness | Setup uses List + ToArray idiomatically | Low | D | D |  |
| 13 | Correctness | BuildCompilation inherits LangVersion from Parse only | Low | D | D |  |
| 14 | Test Quality | Smoke test asserts User.g.cs presence (plan goal met) | Low | D | D |  |
| 15 | Test Quality | Throughput tests assert >0 interceptors, not a baseline count | Low | D | D |  |
| 16 | Test Quality | MigrateAsync detection via string-contains is brittle | Medium | A | A | Switched to filename pattern: any generated file ending in .MigrateAsync.g.cs. |
| 17 | Test Quality | SchemaOnly interceptor check uses filename-Contains | Low | D | D |  |
| 18 | Test Quality | [TestCase] parameterization is clean (no duplication) | Low | D | D |  |
| 19 | Test Quality | HarnessProxy uses new to re-expose protected statics | Low | D | D |  |
| 20 | Test Quality | LoadCorpus called twice in one test (stylistic only) | Low | D | D |  |
| 21 | Test Quality | No direct [GlobalSetup] smoke; HarnessProxy covers same surface | Low | D | D |  |
| 22 | Codebase Consistency | FixtureFiles array duplicated across 3 benchmark classes | Medium | A | A | Hoisted to GeneratorBenchmarkBase as protected static IReadOnlyList<string>; added ParseFixturePlus helper. |
| 23 | Codebase Consistency | Generator/ folder + namespace consistent with Benchmarks/ | Low | D | D |  |
| 24 | Codebase Consistency | [MemoryDiagnoser] per-class vs config-level: either fine | Low | D | D |  |
| 25 | Codebase Consistency | All new methods Quarry_*-prefixed (gh-pages filter) | Low | D | D |  |
| 26 | Codebase Consistency | Harness test follows existing Tests/Generation conventions | Low | D | D |  |
| 27 | Codebase Consistency | Corpus types duplicate Schema names; add header comment | Low | A | A | Prepended a uniform `// CORPUS — embedded resource ...` marker line to every Corpora/v1/**/*.cs file. |
| 28 | Codebase Consistency | Corpus namespace BenchHarness avoids collision | Low | D | D |  |
| 29 | Integration | New Tests → Benchmarks project ref, no cycle, clean | Low | D | D |  |
| 30 | Integration | Tests transitive surface widens (BDN/EF/Dapper/SqlKata pulled) | Medium | A | A | Extracted GeneratorBenchmarkBase + Corpora into a new minimal-deps project Quarry.Benchmarks.GeneratorHarness (Microsoft.CodeAnalysis.CSharp + Quarry + Quarry.Generator only). Quarry.Tests now references the harness project, not Quarry.Benchmarks. Verified Tests.deps.json no longer carries BDN/Dapper/EFCore/SqlKata. |
| 31 | Integration | Microsoft.CodeAnalysis.CSharp direct ref version-aligned | Low | D | D |  |
| 32 | Integration | Compile Remove + EmbeddedResource glob has no overlap | Low | D | D |  |
| 33 | Integration | Build-time generator and harness do not double-generate | Low | D | D |  |
| 34 | Integration | InterceptorsNamespaces irrelevant for harness-driven runs | Low | D | D |  |
| 35 | Integration | Branch should rebase before PR (REMEDIATE step 5 handles it) | Low | D | D |  |

Branch base: `e5493f3` (merge-base) — master is currently 2 commits ahead at `a0711ff`. The 1257-insertion / 1-deletion `git diff merge-base..HEAD` is the right window for review; the larger `master..HEAD --stat` includes those two unrelated master commits inverted as deletions and is misleading.

## Plan Compliance
| Finding | Severity | Why It Matters |
|---|---|---|
| `.cs.txt` → `.cs` pivot is documented in workflow Decisions (Czech-locale collision) and matches what's in the csproj. Compliance, not drift. | Low | Prompt called this out as expected. |
| Sqlite bump 10.0.3 → 10.0.5 in `src/Quarry.Tests/Quarry.Tests.csproj:43` is justified by the new `<ProjectReference Include="..\Quarry.Benchmarks\Quarry.Benchmarks.csproj" />` (line 59) — Quarry.Benchmarks already pinned 10.0.5 on master, and a same-graph version mismatch would warn (NU1605). Recorded in Phase 2 commit message. Not scope creep. | Low | One-line alignment with explicit rationale; no other consumer pinned to 10.0.3 behavior (confirmed via grep). |
| Plan §"Reference set" lists six runtime DLLs; `BuildReferencesImpl` in `GeneratorBenchmarkBase.cs:62-70` adds them all, including the extra `System.Threading.Tasks.dll` not present in `CarrierGenerationTests.CreateCompilation`. Matches plan, slightly richer than the existing test pattern. | Low | Plan-conformant; safer for any await-using snippets in corpora. |
| Plan said `Quarry_Pipeline_SchemaOnly` corpus would be empty / "fixture alone". `Corpora/v1/PipelineSplit/SchemaOnly.cs` is a comment-only file (3 lines) — matches. | Low | Compliance check. |
| Plan §"Phase 4" specified ColdCompile uses Medium; `GeneratorColdCompileBenchmarks.cs:27` loads `Throughput/Medium`. After Phase 5 expanded Medium to 50 queries, ColdCompile now spans the full Medium — consistent with the plan's "headline number on Medium." | Low | Not noted in workflow Decisions, but matches plan intent. |

## Correctness
| Finding | Severity | Why It Matters |
|---|---|---|
| `RunGenerator` discards the updated compilation and diagnostics (`out _, out _` at `GeneratorBenchmarkBase.cs:48`). For the benchmark hot path this is fine, but it means a corpus that produces generator-emitted compile errors (or analyzer-style diagnostics from the generator) silently passes. The harness tests (`GeneratorBenchmarkHarnessTests.cs:99-105`) cover post-gen errors via a fresh `compilation.AddSyntaxTrees`, so the test gate is intact — the prod harness omission is intentional but worth flagging. | Low | If a future corpus regresses to error-producing output the bench would still report timings on broken input. Tests catch it; production harness doesn't. |
| `RunWith` in both `GeneratorThroughputBenchmarks.cs:45-54` and `GeneratorPipelineSplitBenchmarks.cs:45-54` allocates a fresh 7-element `SyntaxTree[]` per iteration. ~64 B / iter — drowned by the compilation+driver allocations (>>1 KB). Not a measurement-pollution risk. Could be hoisted to one pre-built array per benchmark method (e.g. `_smallFull`, `_mediumFull`, `_largeFull` of length 7) to keep the per-iter measurement strictly to compile+drive — minor cleanup, not a correctness issue. | Low | The `[MemoryDiagnoser]` numbers will read ~64 B higher than necessary, but the signal-to-noise ratio is fine. |
| `BuildCompilation` accepts `IEnumerable<SyntaxTree>` (`GeneratorBenchmarkBase.cs:36`); `RunWith` passes a `SyntaxTree[]`. Roslyn re-enumerates fine. No issue. | Low | Sanity. |
| `LoadCorpus` constructs the resource name as `prefix + relativePath.Replace('/', '.') + ".cs"` (`GeneratorBenchmarkBase.cs:21`). If a future caller passed a path that already ended in `.cs` (because they copied the filename), the lookup would fail with `".cs.cs"` and throw with a helpful "Available: …" list. Behavior is defensible. | Low | Documented-in-error rather than silent. |
| `ReferencesCache` uses `Lazy<>` with `isThreadSafe: true`. BenchmarkDotNet does not parallelize a single benchmark across threads but does host benchmarks in subprocesses; `Lazy` is the right choice in either case. No race risk. | Low | Confirmed. |
| `GeneratedTrees.Length` is returned from each `[Benchmark]` method as the BDN dead-code-elimination guard. Good. | Low | Standard pattern. |
| `Setup` in `GeneratorColdCompileBenchmarks.cs:24` builds a `List<SyntaxTree>(FixtureFiles.Length + 1)` then `.ToArray()`. Idiomatic; not in the measured path. | Low | Sanity. |
| `BuildCompilation` does not pin `LangVersion` for the compilation (only the `CSharpParseOptions` does, in `Parse`). The compilation inherits the parser's `LanguageVersion.Latest` per tree. Matches `CarrierGenerationTests`. | Low | Consistent with existing pattern. |

## Security
No concerns.

(`LoadCorpus` reads from `Assembly.GetManifestResourceStream` — names are filtered against the assembly's own embedded resource table, so directory-traversal style strings like `"../../etc"` cannot escape the assembly. References are loaded from `RuntimeEnvironment.GetRuntimeDirectory()` which is the same source `CarrierGenerationTests` already uses. New `Microsoft.CodeAnalysis.CSharp 5.0.0` PackageReference matches the version pinned in `Quarry.Generator.csproj:26` and `Quarry.Tests.csproj:34` — no new dependency surface.)

## Test Quality
| Finding | Severity | Why It Matters |
|---|---|---|
| `RunGenerator_WithHarnessReferences_ProducesEntityClass` (`GeneratorBenchmarkHarnessTests.cs:28-43`) asserts `GeneratedTrees.Length > 0` AND a `User.g.cs`-suffixed tree exists. Stronger than "≥1 tree" — passes the plan's "guard against silent zero-output" requirement. Good. | Low | Plan goal met. |
| `Throughput_Corpora_CompileCleanly_AndProduceInterceptors` (`:80-117`) asserts `interceptorFiles > 0` (line 115). The prompt asks: does it assert a *meaningful* number? It does not — Small/Medium/Large all just check `> 0`. The test does back-validate query count via regex match against `Q\d+` (lines 108-111) so a corpus regression that drops half the Q-methods would fail the count assertion, but not via interceptor count. Reasonable defense in depth, but a regression that splits one interceptor file into many (or vice versa) goes unnoticed. | Low | If interceptor file emission strategy changes, you'd want a count-per-corpus baseline. Out-of-scope for v1. |
| `PipelineSplit_Corpora_FireExpectedPipelines` (`:122-166`) detects MigrateAsync via `t.GetText().ToString().Contains("MigrateAsync", ...)` (line 162). Brittle: any non-migration generated file mentioning the literal "MigrateAsync" (e.g. an XML doc reference, an interceptor commenting on migration ergonomics) would false-positive. Today's emitter likely doesn't, but coupling to a method-name string is fragile. Asserting on filename pattern (e.g. file ending in `Migrations.g.cs`) would be more durable. | Medium | If a future generator adds a reference to MigrateAsync in interceptor XML doc comments, PlusQueries (which expects `false`) would start failing. |
| The `expectInterceptors` flag for `SchemaOnly` is `false` (line 119); the assertion checks `result.GeneratedTrees.Any(t => t.FilePath.Contains("Interceptors", StringComparison.Ordinal))`. The plan said "no interceptor file with non-trivial content" — current test catches the existence-of-file case but would miss an interceptor file that's emitted but empty/skeletal. | Low | The two distinctions are equivalent in current emitter behavior; if the emitter starts always emitting an interceptor stub even with zero queries, this test would break and force a rethink (which is good). |
| `[TestCase]` parameterization is used cleanly for both Throughput and PipelineSplit families — no duplication. | Low | Compliant with prompt's "parameterized via TestCase cleanly" criterion. |
| `HarnessProxy` (`:172-187`) uses `new` to re-expose protected static surface. Cleaner than `[InternalsVisibleTo]` for this case since the harness is a `protected static`-only API. The `using static` import (`using Quarry.Benchmarks.Generator;` at line 1) plus `new` keyword is a standard test-affordance pattern. | Low | Idiomatic. |
| Tests load `LoadCorpus(corpus)` twice in `Throughput_Corpora_CompileCleanly_AndProduceInterceptors` (once via `Parse` at line 91, again at line 107 for the regex). Minor — embedded-resource read is cheap and the test is not perf-sensitive. | Low | Stylistic. |
| No test asserts that `[GlobalSetup]` actually parses without throwing — the test class doesn't drive the benchmark types directly. The harness tests reach the same surface via `HarnessProxy`, so coverage is equivalent. A direct `new GeneratorThroughputBenchmarks().Setup()` smoke test would catch a typo in the static `FixtureFiles` array shared across the three benchmark classes (it's literally duplicated in `GeneratorColdCompileBenchmarks.cs:9-17`, `GeneratorThroughputBenchmarks.cs:9-17`, `GeneratorPipelineSplitBenchmarks.cs:9-17`). | Low | The duplication invites drift — see Codebase Consistency. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---|---|---|
| The `FixtureFiles` array is literally duplicated across all three new benchmark classes (`GeneratorColdCompileBenchmarks.cs:9-17`, `GeneratorThroughputBenchmarks.cs:9-17`, `GeneratorPipelineSplitBenchmarks.cs:9-17`). The plan said "harness deliberately does not hold global state — each benchmark class owns its parsed `SyntaxTree[]` in its own `[GlobalSetup]`" which justifies *parsing* per-class, but not the *file-list* duplication. A `protected static IReadOnlyList<string> FixtureFiles` on `GeneratorBenchmarkBase` (or a `ParseFixture(this)` helper) would dedupe with no semantic change. | Medium | If the fixture grows a sixth schema, three places must be updated atomically; one-place-edit invariants are weaker. |
| Existing benchmarks live in `Quarry.Benchmarks/Benchmarks/` (e.g. `AggregateAvgBenchmarks.cs`) and inherit from `BenchmarkBase` (DB-backed). New generator benchmarks live in a sibling `Quarry.Benchmarks/Generator/` folder and inherit from a new `GeneratorBenchmarkBase`. The folder split is sensible (different concerns; different base class needs); namespace `Quarry.Benchmarks.Generator` is consistent with `Quarry.Benchmarks.Benchmarks` etc. | Low | Convention-consistent. |
| `[MemoryDiagnoser]` attribute placed on the class — matches BDN convention. Existing DB benchmarks rely on a `BenchmarkConfig.cs`-driven `[MemoryDiagnoser]` rather than the per-class attribute. Slight inconsistency (per-class attribute is redundant if the global config already enables it; benign if not). Worth a glance at `BenchmarkConfig.cs` to confirm. | Low | Either way, MemoryDiagnoser fires; no behavioral diff. |
| All new benchmark methods are `Quarry_*`-prefixed: `Quarry_GeneratorColdCompile`, `Quarry_Throughput_{Small,Medium,Large}`, `Quarry_Pipeline_{SchemaOnly,PlusQueries,PlusMigrations}`. Matches the gh-pages publish filter. | Low | Plan / Decisions compliance. |
| New harness test file matches existing `Quarry.Tests/Generation/*Tests.cs` conventions: `[TestFixture]`, NUnit `Assert.That`, file-private helper class. Aligned. | Low | Consistent. |
| The new corpus types (`BenchHarness.UserSchema`, etc.) DUPLICATE class names from the existing `Quarry.Benchmarks.Schemas.UserSchema` etc. They live in different namespaces and the corpus files are excluded from compilation (`<Compile Remove>`), so no conflict at build. But a future maintainer skimming `src/Quarry.Benchmarks/` will see two `UserSchema.cs` candidates with different schemas and could confuse them. A README or a one-line `// CORPUS — not compiled, embedded only` header in each corpus file would mitigate. | Low | Cognitive friction, not a defect. |
| Corpus files use `namespace BenchHarness` which is short and unique within the assembly's *test-resource* surface; existing benchmarks use `Quarry.Benchmarks.Context`. No namespace collision (corpus files don't compile). | Low | Sanity. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---|---|---|
| New `<ProjectReference Include="..\Quarry.Benchmarks\Quarry.Benchmarks.csproj" />` in `Quarry.Tests.csproj:59`. Cycle check: Quarry.Benchmarks → Quarry, Quarry.Generator. Quarry.Tests → Quarry, Quarry.Generator, Quarry.Benchmarks. No cycle. Build-order: Tests now requires Benchmarks compiled first; Benchmarks is `OutputType=Exe` so the test build will trigger an exe build. CI build time impact: small (Benchmarks is a single small project). | Low | New edge in dependency graph; one-way; clean. |
| `Quarry.Tests` now indirectly drags every PackageReference of `Quarry.Benchmarks` into its dependency closure (`BenchmarkDotNet`, `Dapper`, `Microsoft.EntityFrameworkCore.Sqlite`, `SqlKata`, `Microsoft.Data.Sqlite`). `Microsoft.Data.Sqlite` was already a direct ref of Tests; the others were not. No version conflicts (BDN's transitive Roslyn is already covered by `MSB3277` suppression in Benchmarks.csproj). However: this means `dotnet test src/Quarry.Tests` will now pull and restore BDN + EF + Dapper + SqlKata. Restore time will tick up; runtime impact zero (the BDN subprocess is not spawned by NUnit). | Medium | Transitive surface area on the test project widened noticeably. Acceptable if the team accepts the trade-off; flagging for awareness. |
| New `Microsoft.CodeAnalysis.CSharp 5.0.0` direct PackageReference in `Quarry.Benchmarks.csproj:27`. Same version is pinned in `Quarry.Generator.csproj:26` (with `PrivateAssets="all"`) and in `Quarry.Tests.csproj:34`. Quarry.Generator's `PrivateAssets="all"` is exactly why the direct ref is needed (decisions log confirms). No duplicate-resolution risk: identical versions resolve to one assembly. | Low | Clean. |
| `<Compile Remove="Corpora\v1\**\*.cs" />` + `<EmbeddedResource Include="Corpora\v1\**\*.cs" />` in `Quarry.Benchmarks.csproj:42-43`. Glob check: no other `<Compile>` or `<EmbeddedResource>` glob in the csproj overlaps `Corpora\v1\**`. The default SDK Compile glob `**/*.cs` does include `Corpora/**/*.cs` — the explicit `Compile Remove` is what suppresses it. Good. The `<EmbeddedResource>` does NOT have a corresponding `<Remove>` from the implicit EmbeddedResource glob (which by default does NOT pull `*.cs`), so no double-include. | Low | Verified. |
| Quarry.Benchmarks has the analyzer-style `<ProjectReference Include="..\Quarry.Generator\..." OutputItemType="Analyzer" />`. The benchmark project's own `[QuarryContext]` `BenchDb` (in `Quarry.Benchmarks.Context`) will continue to drive the build-time generator pass. Embedded corpus files are NOT compiled, so the build-time generator does NOT also chew through them — they only run via the in-process harness invocation. No double-generation. | Low | Sanity confirmed. |
| `InterceptorsNamespaces` in `Quarry.Benchmarks.csproj:11` covers `Quarry.Benchmarks.Context` and `Quarry.Generated`. The corpus context lives in `BenchHarness` namespace, but only matters for the *harness-invoked* generator, not the build-time one. The harness builds its own compilation without setting any analyzer config, so InterceptorsNamespaces is irrelevant at harness-runtime. No mismatch impact. | Low | Sanity confirmed. |
| Sqlite 10.0.3 → 10.0.5 in Tests: search for any test depending on 10.0.3-specific behavior — none found. The integration test rewrites (`MySqlBackslashEscapesIntegrationTests.cs` removal etc.) shown in `master..HEAD --stat` are NOT branch changes — they belong to master commits the branch is behind. Confirmed via `git diff $(git merge-base master HEAD)..HEAD --stat` which shows only 21 changed files, all directly tied to the benchmarks work. | Low | The misleadingly-large initial diff is a base-vs-tip artifact. Branch should rebase onto current master before merging to avoid surfacing those commits in the PR diff. |

## Issues Created
- (none yet)
