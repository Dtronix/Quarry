# Plan: 307-fix-conditional-mask-model

Fixes issue #307 — two verified critical defects in the conditional clause bitmask model,
plus defense-in-depth and docs. Design approved 2026-07-13 (see workflow.md Decisions).

## Key concepts

**Conditional bitmask model.** Clause sites nested in `if`/`else` deeper than the execution
terminal get a bit index (`ChainAnalyzer.cs:613-653`). The carrier's `Mask` field is OR-ed
by clause interceptors at runtime; the terminal dispatches `_sql[__c.Mask]` from a
pre-rendered variant table. `EnumerateMaskCombinations` decides which masks get variants.

**Positional bit protocol (fragile, replaced in step 1).** `ConditionalTerm` today is
`(BitIndex, Role)` with no site linkage; `AssembledPlan.GetClauseEntries` re-derives
site→bit positionally (next term to each site with `NestingContext != null`), while
ChainAnalyzer's loop also skips `relativeDepth <= 0` and null-role sites. The walks agree
only by accident; any change to bit assignment (steps 3-5) would silently misalign them.
There is a latent misassignment today when a chain is partially inside an `if` (a
baseline-depth site with NestingContext eats the first ConditionalTerm).

**Cascade (structural branch group).** One `if` / `else if` / … / `else` statement chain
(or a ternary, which is a 2-arm cascade with a final else). Arms are indexed 0..N-1.
At runtime, exactly one arm executes (or none, if there is no final else). All clause
sites in one arm set their bits together; sites in different arms are mutually exclusive.

**Pagination virtual parameter slots.** Runtime-valued Limit/Offset are NOT in
`ChainParameters`; the carrier binds them at slot `ChainParameters.Count`(+1)
(`AppendPagination` comment, #303). MySQL positional `?` binding relies on
generation-time bind-order extraction per variant (`PipelineOrchestrator.
RewriteMySqlBindMarkers`, `AssembledPlan.BuildParamConditionalMap`).

## Algorithm: per-arm mask enumeration (step 5)

```
inputs: conditional sites, each with NestingContext
        {GroupKey, ArmIndex, ArmCount, HasFinalElse, NestingDepth(cascades)}
group sites by GroupKey → cascades; ungrouped sites (plain if, no else) → independent bits
for each cascade:
    armBits[arm] = OR of bits of sites in that arm
    options = { armBits[a] for each represented arm }
    if (!HasFinalElse) or (represented arms < syntactic ArmCount):
        options += { 0 }                       // "no arm taken" is reachable
masks = cross-product of: independent bits (each on/off) × one option per cascade
```

Backward compatibility: single `if` (no else) → cascade of 1 arm without final else →
options {0, bit} — identical to today's independent bit. Single-clause `if`/`else` →
2 arms + final else, both represented → options {b0, b1} — identical to today's
exclusive pair. Variant counts for all currently-supported shapes are unchanged.

Depth semantics change: nesting depth counts **cascades**, not `IfStatementSyntax`
ancestors — an `else if` arm is depth 1, not 2. `MaxIfNestingDepth = 2` then means
"a cascade nested inside a cascade arm" at most.

## Algorithm: reachable ⊆ enumerated validator (step 6)

Brute force over all `2^totalBits` masks (≤256): a mask is *structurally reachable* iff
for every cascade the mask's intersection with the cascade's bits is either empty
(allowed only if cascade can take no represented arm) or exactly one arm's full bit set;
independent bits unconstrained. Assert every reachable mask ∈ PossibleMasks; on violation
demote the chain via `MakeRuntimeBuildChain` (→ QRY032) instead of emitting broken code.
Deliberately implemented as a separate walk from EnumerateMaskCombinations so one bug
can't hide in both.

## Steps

### Step 1: ConditionalTerm carries SiteUniqueId; match bits by identity
- [x] `ConditionalTerm` gains `SiteUniqueId` (string; from `site.Bound.Raw.UniqueId`);
  update ctor, `Equals`, `GetHashCode` (QueryPlan equality feeds incremental caching).
- [x] ChainAnalyzer bit loop passes the site's UniqueId.
- [x] `AssembledPlan.GetClauseEntries` matches site→term by UniqueId (dictionary lookup)
  instead of the positional `condIdx` walk.
- [x] Audit other ConditionalTerms consumers (`TerminalEmitHelpers`, `ManifestEmitter`,
  `CarrierAnalyzer`, orchestrator) for positional assumptions; adapt if any.
- Tests: full suite green (pure refactor for all currently-supported shapes). Add
  `Generation/` regression test for the latent bug: chain wholly inside an `if` with one
  deeper conditional Where — the *correct* site must be flagged conditional (assert
  interceptor bodies: baseline-depth clause does NOT set Mask, deeper clause does).
- No dependencies.

### Step 2: Runtime dispatch guard (defense in depth, runtime layer)
- [ ] `CarrierEmitter.EmitCarrierSqlDispatch` multi-variant paths (plain `_sql[__c.Mask]`
  at :1173 and the collection `_sqlCache[mask]` path) emit a bounds + null guard:
  `if ((uint)__c.Mask >= _sql.Length || _sql[__c.Mask] is null) throw new
  InvalidOperationException("Quarry: conditional clause combination (mask N) was not
  enumerated at compile time — this is a Quarry generator bug; please file an issue…")`.
  Keep the guard on a helper (e.g. `Quarry.Internal.ThrowHelper`) if generated-size matters;
  follow existing generated-code idioms.
- [ ] Audit all other generated `_sql[` indexing sites (TerminalEmitHelpers diagnostics
  path iterates enumerated keys only — no guard needed there).
- Tests: `Generation/` — multi-variant chain's generated dispatch contains the guard;
  single-variant chains contain none. End-to-end: the else-if repro shape (still broken
  until step 5) now throws the actionable exception instead of a provider null-CommandText
  error — write as a temporary pin (updated in step 5 when the shape becomes valid) OR
  cover via `MockDbConnection`-style unit if simpler; prefer the pin with a comment.
- No dependencies (independent of steps 1/3-5).

### Step 3: WithTimeout stops consuming a conditional bit
- [ ] ChainAnalyzer bit loop: skip `InterceptorKind.WithTimeout` (explicit kind check —
  do NOT remove `ClauseRole.WithTimeout` from `MapInterceptorKindToClauseRole`; roles are
  used for clause-entry classification elsewhere).
- [ ] Confirm `GetClauseEntries` (now ID-matched, step 1) yields IsConditional=false for
  the WithTimeout site; carrier Timeout field emission unchanged.
- Tests: `Generation/` — chain with conditional WithTimeout + one conditional Where emits
  2 variants (not 4), WithTimeout interceptor body sets Timeout but no Mask.
  `SqlOutput/` cross-dialect: conditional WithTimeout taken/not-taken executes correctly
  both ways (timeout applied vs `DefaultTimeout`).
- Depends on step 1 (ID matching prevents positional misalignment).

### Step 4: Honor conditional Limit/Offset/Distinct (defect 1)
- [ ] Plan model: `PaginationPlan` gains `LimitBitIndex`/`OffsetBitIndex` (int?);
  `QueryPlan` gains `DistinctBitIndex` (int?) alongside `IsDistinct`. Equality updated.
- [ ] ChainAnalyzer: when the Limit/Offset/Distinct site is conditional, record its bit
  in the plan (site→bit now resolvable by UniqueId). `hasLimit`/`isDistinct` still set.
- [ ] SqlAssembler rendering, gated per mask (follow the WHERE `GetActiveTerms` pattern):
  - `AppendPagination`: skip LIMIT (resp. OFFSET) when its bit is set and absent from
    the mask; paramIndex advances only for rendered parts (per-variant ParameterCount
    then reflects gating automatically — MySQL bind-order extraction is per-variant).
  - DISTINCT keyword sites (`RenderSelectSql:264`, `:448`, and the wrap path): render
    only when unconditional or bit ∈ mask; `NeedsDistinctOrderByWrap(plan, mask, config)`
    considers the bit.
  - Batch fallback: `canBatch = false` when pagination or distinct is conditional
    (shared prefix/suffix decomposition can't express per-mask pagination).
  - SQL Server `ORDER BY (SELECT NULL)` injection only in variants where pagination
    is active.
- [ ] Emitters: `TransitionBodyEmitter.EmitPagination`/`EmitDistinct` set
  `__c.Mask |= (1 << bit)` when the site is conditional (thread the bit through
  `FileEmitter`/`InterceptorRouter` the same way clause emitters get `clauseBit`).
- [ ] Terminal binding: bind Limit/Offset carrier fields only when the bit is active
  (mask-gated, like conditional collection materialization at `CarrierEmitter.cs:1191`).
  Update `MaskAwareTerminalBindingTests` (the ":362 — pagination bound unconditionally"
  design intent is superseded).
- [ ] MySQL: extend `BuildParamConditionalMap`/bind-order handling to the pagination
  virtual slots so positional `?` binding skips inactive pagination params per variant.
- [ ] Consistency: `ToDiagnostics` active-clause reporting and `ManifestEmitter` variant
  labels correct for the new bits (verify `TerminalEmitHelpers.cs:381-389`, `:490`).
- Tests:
  - `Generation/`: conditional `Limit(25)` → 2 variants, mask-0 SQL has NO `LIMIT`,
    mask-1 has it; Limit interceptor sets Mask; same for Offset, Distinct;
    conditional Distinct + OrderBy-on-non-projected-column renders wrap only in the
    distinct-active variant.
  - `SqlOutput/` cross-dialect `[TestCase(true/false)]`: conditional literal Limit —
    false branch returns FULL row set (the issue's silent-truncation repro);
    runtime-valued Limit — false branch does NOT emit `LIMIT 0`; conditional Offset;
    conditional Distinct; conditional Limit combined with conditional Where (2 bits,
    4 variants, all executed); `ToDiagnostics` ActiveMask/SQL consistency in each case.
  - MySQL is exercised by the cross-dialect harness (positional binding with a
    conditional Limit + parameterized Where both ways).
- Depends on step 1 (bit identity); independent of steps 2/3.

### Step 5: Structural cascade grouping (defect 2)
- [ ] `NestingContext` gains `GroupKey` (string — cascade head `IfStatementSyntax`
  span/position; ternary uses its own span), `ArmIndex` (int), `ArmCount` (int),
  `HasFinalElse` (bool). Equality updated. `NestingDepth` semantics: cascades, not ifs.
- [ ] `DetectNestingContext` (UsageSiteDiscovery): walk from the innermost containing
  arm to the cascade head (`if` whose `Parent` is an `ElseClauseSyntax` belongs to the
  parent's cascade); compute arm index by walking the cascade's arms; count arms +
  final else. Ternary = 2-arm cascade with final else. Depth = number of distinct
  cascades crossed walking up to the method body.
- [ ] ChainAnalyzer: group conditional sites by `GroupKey` (drop condition-text keying
  and the `BranchKind`-based exclusive/independent split); implement per-arm
  enumeration exactly as in the algorithm section. `BranchKind` may become redundant —
  remove it only if nothing else consumes it, else leave populated.
- [ ] Verify `MaxIfNestingDepth = 2` guard still demotes cascade-in-cascade beyond
  depth 2, and NO LONGER demotes flat else-if chains of any arm count (subject to the
  8-bit total limit → QRY032 as today).
- Tests:
  - `Generation/`: repro shape 1 (3-arm else-if, one Where per arm) → masks exactly
    {1,2,4}, no null gaps ≤ maxMask reachable, all three predicates present in their
    variants; repro shape 2 (two Wheres in one if-branch of if/else) → masks {3,4},
    variant 3 contains BOTH predicates; if/else-if with NO final else → masks include 0;
    arm with no chain sites (else branch that doesn't touch the query) → 0 included;
    ternary unchanged; existing single-if and if/else variant counts unchanged.
  - `SqlOutput/` cross-dialect: 3-arm else-if executed down each arm (row-state
    asserted per arm); two clauses in one branch executed both ways; else-if without
    final else executed down the no-arm path; `ToDiagnostics` consistency for each.
  - Update the step-2 pin: the else-if shape now executes correctly (no throw).
- Depends on step 1; recommended after step 4 (shared ChainAnalyzer region, fewer
  conflicts), but not semantically dependent on it.

### Step 6: Generation-time reachability validator (defense in depth, gen layer)
- [ ] Implement the brute-force validator (algorithm section) in ChainAnalyzer after
  enumeration; on violation `MakeRuntimeBuildChain("conditional mask enumeration
  incomplete…")` → QRY032.
- [ ] Make the validator's core pure/internal so it is unit-testable with synthetic
  cascade/bit inputs (it should never fire through the public pipeline once step 5 is in).
- Tests: unit tests on the validator core — passing case (per-arm enumeration output),
  failing case (feed a deliberately-pruned mask list → violation detected). Full suite
  green (validator silent on all real chains).
- Depends on step 5.

### Step 7: Documentation
- [ ] Root `llm.md` (usage): conditional-clause section states exactly which chain
  methods participate in conditional masking (Where/OrderBy/ThenBy/GroupBy/Having/
  Select/Set/Join…, now incl. Limit/Offset/Distinct), that WithTimeout is conditional-
  safe without consuming a bit, and that else-if cascades and multi-clause branches
  are supported (8-bit / depth-2 limits unchanged).
- [ ] `src/Quarry.Generator/llm.md` (internals): cascade grouping model, per-arm
  enumeration, SiteUniqueId bit matching, pagination/distinct mask gating, validator,
  runtime guard. (Per repo convention: root = usage only, generator = internals.)
- [ ] README QRY tables if diagnostics text changed (no new QRY id is added by this plan).
- Tests: n/a (docs). Suite green.
- Depends on steps 1-6 (describes the final state).

## Notes
- No new QRY error id: defect 1 is honored (not rejected), defect 2 becomes supported,
  and both defense layers reuse QRY032 / runtime exception.
- ManifestOutput goldens (`quarry-manifest.*.md`) may churn if test-project chains hit
  the changed enumeration — treat churn as regression first, update goldens only when
  the new SQL/variant sets are intentionally correct.
- `ConditionalBranchBenchmarks` (benchmarks project) is the issue's verification vehicle
  for defect 1 — after step 4, its generated `Limit_*` interceptor must set the mask and
  variant tables must differ; check it compiles.
- Each step: implement → verify against source → run full suite → tick checkbox →
  commit (with `_sessions/`).
