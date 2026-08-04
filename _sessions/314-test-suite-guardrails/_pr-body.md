## Summary

- Closes #314

Addresses all seven findings from the 2026-07-07 multi-agent deep review (tests perspective): hollow
incremental-caching tests, no perf regression gate, zero concurrency testing, row-order flakes,
known generator bugs routed around instead of pinned, unenforced manifest goldens, and untested
streaming/cancellation. The two bundled low items (test parallelization, display-class canary) were
deferred by decision — parallelization is orthogonal and risky, the canary belongs to #310.

Suite goes from 3424 to **3501** tests, all green (plus Migration 201, Analyzers 146).

## Reason for Change

The suite's weak spots were not missing tests so much as tests that could not fail. `IncrementalCachingTests`
exercised anonymous-type projections, which are disqualified to `RuntimeBuild` — so every interceptor it
inspected was a hollow shell, and its "unchanged run" case reused the same `CSharpCompilation` instance, so
model `.Equals` was never invoked at all. 526 positional row assertions on PostgreSQL/MySQL/SQL Server ran
against queries with no `ORDER BY`. The manifest goldens had no CI enforcement. Benchmarks published with no
threshold logic, and silently dropped failed runs from the dashboard.

## Impact

- **Three real defects found and fixed or filed while building the guardrails**, which is the substantive
  result:
  - a **generator crash** (`DisplayClassEnricher` dereferencing superseded syntax trees on a warm rebuild →
    CS8785 → *every interceptor silently vanishes*), found by the new fresh-tree unchanged-run test and fixed
    inline with user approval;
  - **#334** — `InsertBatch` interceptors call `internal` `BatchInsertSqlBuilder`, so the feature does not
    compile for any consumer outside Quarry's `InternalsVisibleTo` list. Found during review remediation, by
    the assertion that guard fixtures must compile cleanly. Every in-repo project that uses `InsertBatch`
    happens to hold a grant, which is why nothing had exercised it as an ordinary consumer would;
  - **#333** — chains inside doubly-nested lambdas emit interceptors that fail to compile (CS0103).
- Two pre-existing routed-around bugs are now filed and pinned rather than worked around silently
  (**#328** conditional `Having` not mask-gated, **#329** entity-terminal receiver arity).
- **#332** records nine `CrossDialectJoinTests` that encode row order through a column they never project;
  left unchanged by decision, with a `<remarks>` block explaining why a plausible-looking composite sort key
  silently reorders them.

## Plan items implemented as specified

| Step | Finding | What landed |
|---|---|---|
| 1 | F6 | Manifest golden drift check in CI |
| 2 | F1a | `WithTrackingName` on seven load-bearing pipeline nodes, names as typed constants |
| 4 | F1c | Negative equality + hash consistency for the four Stage-5 pipeline models |
| 5 | F5a | Tracking issues #328 / #329 filed for the routed-around bugs |
| 8 | F3 | Concurrency suite: parallel mixed read/write, contended carrier first-touch, parallel all-dialect reads |
| 9 | F7a | Streaming early-break disposal, bite-verified by removing `await using` from the reader |
| 10 | F7b | Runtime cancellation across every fetch terminal |
| 11–13 | F4 | Row-order sweep: 118 sites now sorted, six queries given a query-side `ORDER BY` |
| 14 | F2 | Alert-only benchmark regression gate |
| 15 | — | `llm-testing.md` and `src/Quarry.Generator/llm.md` updated |

## Deviations from plan implemented

- **Step 3 (F1b)** — the rewritten caching tests found a real generator crash. Fixed inline with user
  approval (`DisplayClassEnricher` recovers the equivalent node from the current compilation by
  `FilePath` + span). Two assertions became **#310 pins** rather than correct-behaviour assertions,
  because the defects they hit are real and tracked.
- **Step 6 (F5b)** — #328's misattribution turned out to be **stale** (likely fixed by #307/#322), but a
  real defect remains in the same shape: conditional `Having` renders unconditionally. #328 was retitled
  to that. #329 was reclassified from CS9177 (warning) to **CS9144 (error)**, making the `.Select(...)`
  workarounds load-bearing rather than cosmetic.
- **Step 7 (F5c)** — the blanket `<NoWarn>CS9177</NoWarn>` was found **vestigial** (a full build with the
  suppression overridden reported zero CS9177) and removed, rather than kept as planned. The "exact
  expected set of CS9177" is empty, so the guard asserts zero CS9144/CS9177 across a shape matrix plus
  proof an interceptor was actually emitted. A compilable #329 pin was recovered after step 6 concluded
  there was none.
- **Steps 11–13 (F4)** — 118 sites converted, not ~526. The raw count is inflated by multi-field
  assertions and by pg/my/ss sides that only assert `Has.Count`. Sixteen of the twenty-one files in the
  final group needed nothing, and for informative reasons: `Composition`/`WindowFunction` were already
  order-independent, `ConditionalMask` carries an unconditional `OrderBy` in every chain, and
  `OrderBy`/`DistinctOrderBy` pin a top-level `ORDER BY` that a client-side sort would mask.
- **Step 14 (F2)** — issue lookup and creation use `curl` + REST rather than `gh`, which is not guaranteed
  on the self-hosted benchmark runner.

## Gaps in original plan implemented

Found by the review pass and fixed here:

- The manifest gate could not distinguish "goldens verified current" from "generator stopped emitting
  them" — `git diff --exit-code` on a directory nothing wrote is empty. The goldens are now deleted before
  the build, so the check also proves regeneration; untracked goldens are caught too, and `.gitattributes`
  keeps the comparison platform-independent.
- The perf gate could defeat itself three ways: an API blip reddened the run *and* discarded the publish;
  title-only dedupe plus an advancing baseline meant one open issue silenced the channel permanently; and
  a legitimate benchmark rename red-locked the pipeline with no override.
- `AssertNoStageRecomputedDifferently` passed vacuously when no stage was tracked — in the flagship
  assertion for the review's only High finding.
- The #329 pin grepped every generated tree rather than the terminal's own signature, so it could have
  stayed green after the bug was fixed.
- Step 3's paired "a no-op edit leaves everything cached" assertion had been dropped; invalidation is the
  easy direction, and a false invalidation hides in the other one.
- Step 4's equality coverage never varied several of the fields it claimed to cover.
- Three copies of the generator-test reference list had already diverged on a reference that measurably
  changes generator classification; they are now one shared helper.

## Performance Considerations

`DisplayClassEnricher` built a linear scan of `compilation.SyntaxTrees` per stale site, on a path that
fires for every site on a warm rebuild. Now a `FilePath → SyntaxTree` dictionary built once.

The concurrency suite costs ~35s (24 harnesses across 3 tests); `Workers` is a single const if CI time
needs trimming.

## Security Considerations

`issues: write` is required to file a regression issue, but the benchmark job also runs arbitrary project
and NuGet code on a persistent self-hosted runner. Issue filing therefore lives in a **separate
`ubuntu-latest` job** that consumes only the perf-gate artifact; the benchmark job stays `contents: read`.

Values crossing the shell boundary in the new workflow logic go through `jq --arg`/`--argjson`/`--rawfile`,
which parse rather than interpolate; the only interpolated identifier is `${GITHUB_REPOSITORY}`. Benchmark
names and the commit subject flow verbatim into an issue body, so an `@mention` in a commit subject would
notify — presentational only.

## Breaking Changes

- **Consumer-facing:** none. All test-project and CI changes, plus two generator fixes that only make
  existing behaviour more robust.
- **Internal:**
  - `Quarry.Tests.csproj` drops the blanket `<NoWarn>CS9177</NoWarn>` and gains
    `<WarningsAsErrors>CS9177;CS9144</WarningsAsErrors>` — an interceptor arity or signature mismatch now
    breaks that build instead of scrolling past.
  - `TrackingNames` is `internal`, not `public`; it was briefly added to the shipped `Quarry.Generator`
    package's surface for a test-only concern, and reaches tests through the existing `InternalsVisibleTo`.
  - `.gitattributes` is new — contributors on Windows with `core.autocrlf=false` may see the manifest
    goldens normalize on their next checkout.
