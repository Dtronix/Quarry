# Review: 308-runtime-hotpath-fixes

## Classifications
Class: (A) valid, fix now | (B) gap, fix now | (C) separate issue | (D) not valid / accept.

| ID | Class | Rec | Sev | Section | Finding | Action Taken |
|----|-------|-----|-----|---------|---------|--------------|
| F3 | B | B | M | Test Quality | Item 6e First reorder ships with no test of the failure-path (no spurious FetchCompleted) | Added `FetchFirst_MaterializationThrows_NoSpuriousCompletionLog` (malformed-datetime materialization throw); teeth-verified — pre-fix logs spurious `[1] Fetched 1 rows` |
| F8 | B | B | L | Integration | `src/Quarry/.editorconfig` `root = true` blocks future repo-root editorconfig inheritance | Removed `root = true` (replaced with an explanatory comment); CA2007 `[*.cs]` rule still applies, build stays 0-error |
| F1 | D | D | L | Correctness | First masks materialization error with "no elements" — INVALID: `ReportReaderFailure` always throws `QuarryQueryException` wrapping the real ex; comment is accurate | dismissed |
| F2 | D | D | L | Correctness | RawSql file-scope `_mapper_*` field unused for struct-reader sites — one-time static (not per-row); clean fix needs disproportionate plumbing | dismissed |
| F4 | D | D | L | Test Quality | BatchInsert test can't distinguish fast-path from fallback — behavior-preserving, no clean observable | dismissed |
| F5 | D | D | L | Test Quality | 5 QuarryContext OpId-gating sites untested — runtime-neutral, opId not observable | dismissed |
| F6 | D | D | L | Consistency | `__target`-guard block triplicated in CarrierEmitter — plan pre-authorized as optional; matches verbose emit style | dismissed |
| F7 | D | D | L | Consistency | Disposal-handle naming divergence — new handles use consistent `__<var>Disp`; `_cmd` is pre-existing/out of scope | dismissed |
| F9 | D | D | L | Integration | `NavigationList.Unloaded()` reference-identity change — intended design of item 2; safe (sealed + immutable) | dismissed |

Scope reviewed: `git diff origin/master..HEAD -- . ':(exclude)_sessions'` (branch is directly ahead of `origin/master`; merge-base == `origin/master`, so two-dot == three-dot). 21 files, +775/-96. Cross-referenced against `plan.md` (11 steps) and `workflow.md` Decisions/Working Notes.

The two highest-risk edits were scrutinized directly against source and found **correct**:
- **IN-cache length compare** (`CarrierEmitter.cs:1261-1263`): exact per-collection `ColParts[i].Length == __col{gi}Len`. Verified `ColParts[i]` is never null (`TerminalEmitHelpers.EmitCollectionPartsPopulation` always emits `__colParts = new string[__colLen]`, even for masked-off conditional collections where `Len==0` ⇒ `new string[0]`). Cache is keyed by `__c.Mask` (`:1247`), so within a slot the active-collection set is fixed and a masked-off collection's length is consistently 0 — no false hit, and worst case is a spurious miss (rebuild), never stale reuse. `ColParts[i]` (order = `collections[i]`) correctly pairs with the `GlobalIndex`-named length var. Sound.
- **ConfigureAwait disposal splits** (`QueryExecutor.cs`, `QuarryContext.cs`): the `await using var x = await ...` → `var x = await ...; await using var __xDisp = x.ConfigureAwait(false)` rewrite preserves reverse-declaration disposal order in every method (reader `__dbReaderDisp` still declared after `_cmd`, so reader disposes first, command second). Exception-before-assignment semantics unchanged. No leak, no reordering.

## Plan Compliance

No concerns. All 11 steps implemented as specified; the three scope decisions (full `src/Quarry` ConfigureAwait sweep, CA2007=error scoped to the runtime project, all six item-6 nits) are honored. OpId gating uses the exact `__logger != null ? OpId.Next() : 0` / `LogsmithOutput.Logger != null ? ...` forms from the plan across all 6 sites (1 insert terminal + 5 QuarryContext raw-SQL). The `quarry-manifest.sqlite.md` snapshot delta (two new query entries, counts 706→710 / 513→515) is expected regeneration driven by the two new execution tests, not scope creep. The plan's optional "factor a tiny helper" for item 6c was not taken (see F6), but the plan marked it optional.

## Correctness

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F1 | `ExecuteCarrierFirstWithCommandAsync` (`QueryExecutor.cs:76-86`): when `reader(dbReader)` throws, the catch calls `ReportReaderFailure` (log-only) then falls through to `throw new InvalidOperationException("Sequence contains no elements.")`, masking the real materialization exception with a misleading message. This is **pre-existing** (the reorder only moved `FinalizeQuery` after `reader(...)`), but the new comment "a reader failure is reported as a reader failure" overstates the fix — the failure is logged, not surfaced. The reorder does correctly eliminate the spurious `FetchCompleted`. | L | Callers see a wrong exception type/message on a materialization fault; unchanged by this PR but adjacent to it and worth a follow-up. |
| F2 | `InterceptorCodeGenerator.CollectMappingInstances` (`:150-162`) now walks `site.RawSqlTypeInfo.Properties` for every RawSql site regardless of reader shape, while `RawSqlBodyEmitter.EmitRowReaderStruct` (`:43-46`) also emits a struct-local field. For a struct-reader site (the compile-time-resolved shape) both are emitted, so the file-scope `_mapper_*` field is unused — a never-read `private static readonly ... = new()` (a one-time mapper allocation and a potential CS0414 in the `.g.cs`, almost certainly suppressed by generated-code detection). Ironic given item 5 targets reducing mapper allocations. | L | Minor emit bloat / one dead static allocation per distinct mapper per struct-reader site; benign but avoidable by gating the RawSql branch to non-struct readers. |

## Security

No concerns. No change alters SQL text assembly or parameter binding in an unsafe direction. The IN-cache fix makes bind-length validation *stricter* (removes a stale-SQL reuse path). `ParameterNames` still emits `@p{n}` / `${n+1}` positional names only. No new input crosses a trust boundary.

## Test Quality

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F3 | Item 6e (First materialize-before-log reorder) ships with **no test**. The behavior it fixes — no `FetchCompleted` logged when materialization throws, and timing that includes materialization — is unverified; existing First/FirstOrDefault tests only exercise the success path, which is observably identical pre/post reorder. A logger-capture assertion (`First_MaterializationThrows_NoSpuriousCompletionLog`) would give it real teeth. Plan permitted skipping *if no harness exists*, but absence was not confirmed. | M | The only item whose corrected behavior has zero coverage; a future regression that reintroduces log-before-materialize would pass all tests. |
| F4 | `BatchInsert_ListAndLazyEnumerable_BothInsertCorrectly` (item 6a) asserts only row counts/names. It cannot distinguish the new `as IReadOnlyList<T>` fast-path from the `ToList` fallback — both produce identical rows — so it does not prove the no-copy branch is taken. Acceptable (optimization is behavior-preserving) but the new branch is untested. | L | The optimization could silently break (always fall back to copy) without any test failing. |
| F5 | Item 3 gating is verified only for the single-row Insert terminal (`OpIdGatingGenerationTests`). The 5 `QuarryContext` raw-SQL gating sites have no assertion. Runtime-behavior-neutral (opId only observed when a logger is present), so low risk. | L | A regression un-gating one of the 5 raw-SQL sites would reintroduce the Interlocked contention undetected. |

Positive notes (teeth confirmed): item 1 test reproduces the documented `(16,900)`/`(85,41)` collision on a shared per-carrier cache (throws pre-fix); `ParameterNamesTests.AtP_CachedEntriesAreInterned...` at index 2099 has genuine teeth for the 256→2100 widening (pre-fix that index hit the concat fallback ⇒ distinct instances ⇒ `SameAs` fails); `Unloaded_GetEnumerator_ReturnsSharedInstance` and `Unloaded_ReturnsSharedSingleton` correctly assert reference identity; `StaticCaptureExtractionTests` asserts absence of `var __target =`. The CA2007=error build is a real guard for item 4 (any bare await fails the runtime-project build).

## Codebase Consistency

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F6 | The `__target`-guard block (`if (extractionPlan.Extractors.Any(e => !e.IsStaticField))` + identical 2-line comment) is duplicated verbatim at 3 sites in `CarrierEmitter.cs` (`:286`, `:504`, `:529` region). Plan flagged factoring a helper as optional; the copy-paste is consistent with the surrounding emit style but is textbook triplication. | L | Future edits must be applied in three places; drift risk. |
| F7 | Disposal-handle naming diverges: `QueryExecutor` retains the existing `_cmd` for the command handle while `QuarryContext` introduces `__commandDisp`, and reader handles use `__dbReaderDisp`. Two conventions for the same "unused disposal handle" concept within one sweep. | L | Cosmetic; slightly muddies the otherwise-mechanical ConfigureAwait pattern. |

The item-5 field-name reuse (`GetMappingFieldName`) correctly follows the existing chain-mapper / Patch-binder precedent, and the struct-local-vs-file-scope same-name coexistence is intentional and matches the documented pattern.

## Integration / Breaking Changes

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F8 | `src/Quarry/.editorconfig` sets `root = true`. Because it lives in the project directory, any repo-root `.editorconfig` added later will **not** be inherited by files under `src/Quarry`. No parent config exists today (workflow confirms), so no current impact, but a future repo-wide style/analyzer baseline would silently skip the runtime project. Consider dropping `root = true` (or documenting the intent) unless isolation is deliberate. | L | Latent surprise for a future maintainer adding shared editorconfig rules. |
| F9 | `NavigationList<T>.Unloaded()` now returns a process-wide shared singleton instead of a fresh instance per call — an observable reference-identity contract change on a `public` API. Safe given the type is `sealed` and the unloaded state is deeply immutable (`_items` null readonly, no public mutators), but any external consumer or test asserting per-call distinctness would break. | L | Public-surface behavior change; benign under the documented immutability invariant. |

CA2007=error blast radius is correctly confined to `src/Quarry` (tests/generator/benchmarks/samples unaffected). No dependency or public-signature changes elsewhere; `PreparedQuery<T>` change is documentation-only.
