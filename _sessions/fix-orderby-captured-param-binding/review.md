# Review: fix-orderby-captured-param-binding

## Classifications
| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---|---|---|---|---|---|
| 1 | Plan Compliance | `hasResolvableCapturedParams` is dead in `EmitCarrierClauseBody` body | Info | D | | |
| 2 | Plan Compliance | QRY037 message wording divergence from plan | Info | D | | |
| 3 | Plan Compliance | `CarrierAssignmentRecorder` type vs raw dict on FileEmitter | Info | D | | |
| 4 | Plan Compliance | Inline `carrierToChain` build vs planned `ResolveRootSiteForCarrier` helper | Info | D | | |
| 5 | Plan Compliance | Pure-helper unit test vs synthetic FileEmitter end-to-end fire test | Info | D | | |
| 6 | Plan Compliance | Phase 5.6 workflow.md divergence note recorded | Info | D | | |
| 7 | Plan Compliance | `llm.md` updated as inline paragraph instead of table row | Info | D | | |
| 8 | Plan Compliance | SetOp/CTE recorder plumbing scope-crept beyond plan | Info | D | | |
| 9 | Plan Compliance | All 10 P-field assignment sites covered by recorder | Info | D | | |
| 10 | Correctness | `EmitJoin` not-first-in-chain implicit invariant (Clause.Parameters vs siteParams) | Low | B | B | applied |
| 11 | Correctness | `int.Parse` in `ComputeUnassignedPIndices` would `OverflowException` on absurd P-name | Low | B | B | applied |
| 12 | Correctness | `_chains.Count` iteration with `< _carrierPlans.Count` defensive bounds check | Info | D | | |
| 13 | Correctness | `recorder?.Record` null-safe at all 10 sites | Info | D | | |
| 14 | Correctness | `GetAssigned` allocates new HashSet on cache miss | Info | D | | |
| 15 | Correctness | `CarrierAssignmentRecorder` not thread-safe (single-threaded by design) | Info | D | | |
| 16 | Correctness | Navigation-join lambda hardcoded `_` (latent — caught by compile error if hit) | Low | D | | |
| 17 | Correctness | `EmitCarrierClauseBody` short-circuit guard chain verified safe | Info | D | | |
| 18 | Correctness | `carrierToChain` lookup miss silently drops diagnostic vs plan's Location.None fallback | Low | B | B | applied |
| 19 | Correctness | Eligible-carrier gating correct | Info | D | | |
| 20 | Test Quality | Phase 2 `AllParameters[1].Value` runtime regression coverage strong | Info | D | | |
| 21 | Test Quality | Phase 3+4 generation regex assertions verified | Info | D | | |
| 22 | Test Quality | Phase 4 GroupBy `o.UserId + bucketOffset` shape verified | Info | D | | |
| 23 | Test Quality | Phase 5 `ComputeUnassignedPIndices` covers happy + failure modes + edge cases | Info | D | | |
| 24 | Test Quality | `RealChains_DoNotTriggerQRY037_NoFalsePositives` partially redundant but useful for debugging | Info | D | | |
| 25 | Test Quality | No test exercises `EmitCarrierAssignmentDiagnostics` path (carrierToChain build, _emitDiagnostics.Add, Location resolution) | Low | C | A | applied |
| 26 | Test Quality | Mask-gated "any-branch assignment counts" trivially satisfied by mask-agnostic recorder | Info | D | | |
| 27 | Test Quality | `BuildCarrierPlan` helper clean | Info | D | | |
| 28 | Test Quality | `Is.Empty` assertions are meaningful (not trivially-true) | Info | D | | |
| 29 | Codebase Consistency | Fix mirrors EmitWhere/EmitJoinedWhere/EmitHaving/EmitModificationWhere pattern | Info | D | | |
| 30 | Codebase Consistency | Suppression-attribute gating differs from existing emitters (`methodFields.Count > 0` vs `hasResolvableCapturedParams`) | Low | C | A | applied |
| 31 | Codebase Consistency | `CarrierAssignmentRecorder` naming novel but acceptable | Info | D | | |
| 32 | Codebase Consistency | Optional trailing-parameter threading matches codebase pattern | Info | D | | |
| 33 | Codebase Consistency | QRY037 descriptor declaration matches QRY030–036 template | Info | D | | |
| 34 | Codebase Consistency | QuarryGenerator descriptor-list ordering matches semantic grouping | Info | D | | |
| 35 | Codebase Consistency | `_emitDiagnostics.Add` invocation matches existing QRY041 usage | Info | D | | |
| 36 | Codebase Consistency | No utility duplicated | Info | D | | |
| 37 | Integration | All emit-method signatures internal-only; new param is optional trailing | Info | D | | |
| 38 | Integration | `CarrierAssignmentRecorder` internal sealed; not externally visible | Info | D | | |
| 39 | Integration | QRY037 is a new Error-severity diagnostic — by design fail-fast on regressions | Info | D | | |
| 40 | Integration | InternalsVisibleTo for tests already in place | Info | D | | |
| 41 | Integration | No new dependencies / package upgrades | Info | D | | |
| 42 | Integration | `llm.md` documentation captures QRY037 in chain-diagnostics paragraph | Info | D | | |
| 43 | Integration | No SQL output diff (interceptor bodies only) | Info | D | | |
| 44 | Integration | Migration: pre-fix CS0649 warnings on Chain_N.P-fields disappear; queries become correct | Info | D | | |

## Plan Compliance
| Finding | Severity | Why It Matters |
|---|---|---|
| Phase 1 (symmetric emitter fix) faithfully implements all four emitter changes in `ClauseBodyEmitter.cs` (EmitOrderBy lines 95–193, EmitGroupBy lines 245–355) and `JoinBodyEmitter.cs` (EmitJoin lines 25–162, EmitJoinedOrderBy lines 254–374). The keyType-known branch was changed to pass `hasResolvableCapturedParams` instead of `false` for the `EmitCarrierClauseBody` flag — beneficial cleanup, but note that `hasResolvableCapturedParams` is in fact **unused** inside `EmitCarrierClauseBody` (CarrierEmitter.cs line 260 only declares it as a parameter; nothing in the body reads it). The param is dead and was already dead on master. | Info | Documents that the plan's "pass `hasResolvableCapturedParams`" instruction is cosmetic: the actual extraction is gated by `extractionPlan != null && Extractors.Count > 0`, not the flag. |
| Phase 5.1 message format diverges very slightly from plan: plan said `"Carrier '{0}' declares parameter field 'P{1}' but no clause interceptor assigns it. The corresponding SQL parameter would silently bind default(T). This is a generator defect — please file an issue."`, actual is `"Generator defect: carrier '{0}' declares parameter field 'P{1}' but no clause interceptor body assigns it. The corresponding SQL parameter would silently bind default(T). Please file an issue."`. Order/wording reorganized but semantics preserved (`DiagnosticDescriptors.cs` lines 535–539). | Info | Wording-only divergence; not a functional issue. |
| Phase 5.2 plan said add `Dictionary<string, HashSet<int>> _assignedPIndicesByCarrier` directly on `FileEmitter`. Actual implementation is a separate `CarrierAssignmentRecorder` type wrapping the dictionary (workflow's IMPLEMENT-phase decision noted this). Workflow records this divergence. Decision: encapsulate state in a per-FileEmitter recorder with `Record`/`GetAssigned` API, threaded as optional `CarrierAssignmentRecorder? recorder = null` parameter through every emit method that writes `__c.P{i} = ...`. Confirms DESIGN-phase "Track during emission" — NOT post-emit scanning. | Info | Reasonable refactor, matches workflow Decision 6 and is cleaner than threading a raw dictionary. |
| Phase 5.3 plan called for `ResolveRootSiteForCarrier` helper. Actual implementation builds a `Dictionary<string, AssembledPlan> carrierToChain` inline in `EmitCarrierAssignmentDiagnostics` (FileEmitter.cs lines 528–571) and uses the FIRST chain owning a class name. Same outcome via different shape. | Info | Functionally equivalent. |
| Phase 5.5 plan said "construct a synthetic FileEmitter input where a CarrierPlan declares a P-field that no site assigns" via internal test hook. Actual approach exposed `FileEmitter.ComputeUnassignedPIndices` as `internal static` and unit-tested the pure function directly (CarrierGenerationTests.cs lines 4313–4380). End-to-end fire test omitted. Plan's "Pick the cleanest approach during implementation" guidance accommodates this. | Info | Pragmatic deviation; the gap-detection logic is fully covered, but the diagnostic-emission path (`EmitCarrierAssignmentDiagnostics`, the `carrierToChain` lookup, the `_emitDiagnostics.Add` call) is not directly exercised by any test. A bug in that wiring would slip through. |
| Phase 5.6 plan said "Update `_sessions/.../workflow.md` decisions to record the implementation approach if it diverges." workflow.md has the IMPLEMENT-phase divergence note (about CarrierAssignmentRecorder threading, lines 66–69). | Info | Done. |
| `llm.md` update: plan called for a "QRY037 row" in the diagnostic table. Implementation appended to the QRY030–036 list narrative inline (single sentence with implementation details about `ComputeUnassignedPIndices`/`CarrierAssignmentRecorder`). Header changed `QRY030–036` → `QRY030–037`. | Info | Documentation captured; format matches surrounding chain-diagnostics paragraph. |
| Scope creep: `SetOperationBodyEmitter.EmitSetOperation` and `TransitionBodyEmitter.EmitCteDefinition` also gained the `recorder` parameter and recording sites (SetOperationBodyEmitter.cs line 97, TransitionBodyEmitter.cs line 218). Not mentioned in plan but required for QRY037 correctness — these emit `__c.P{idx} = __op.P{i}` / `__c.P{targetIdx} = __inner.P{p}` direct assignments. | Info | Necessary for QRY037 to not false-fire on set operations / CTE chains; a real implementation gap if omitted. Plan understated scope. |
| Plan's mid-IMPLEMENT decision noted "any Px assignment site routes through the recorder". Verified: 8 recording sites in CarrierEmitter.cs (lines 315, 320, 326, 1058, 1064, 1081, 1085, 1091), 1 in SetOperationBodyEmitter.cs line 97, 1 in TransitionBodyEmitter.cs line 218. No `__c.P{...}` assignment found that bypasses recorder. | Info | Coverage looks complete. |

## Correctness
| Finding | Severity | Why It Matters |
|---|---|---|
| `JoinBodyEmitter.EmitJoin` not-first-in-chain branch with `siteParams.Count > 0` (lines 89–98 chained, 146–154 single-arity) routes through `EmitCarrierClauseBody` with `clauseBit: null, isFirstInChain: false`. `EmitCarrierClauseBody` derives params from `site.Clause.Parameters`, NOT from the `siteParams` list passed at the call site. As long as `site.Clause.Parameters.Count == siteParams.Count`, this is fine; both are derived from the same source. Implicit invariant. | Low | If a future divergence between `site.Clause.Parameters` and `ResolveSiteParams` (TerminalEmitHelpers) is introduced, the new branch will silently miscount. Worth a comment or assert. |
| `ComputeUnassignedPIndices` (FileEmitter.cs lines 56–73) parses `P{digits}` field names with `int.Parse`. For pathologically long names (e.g. `P99999999999999999999`) this throws `OverflowException`. In practice generator-emitted P-field indices are small, but the parser is not defensive. | Low | Unlikely to occur, but `int.TryParse` would be marginally safer. |
| `EmitCarrierAssignmentDiagnostics` (FileEmitter.cs lines 526–572) iterates `_chains.Count` while indexing `_carrierPlans` with `i < _carrierPlans.Count`. `PipelineOrchestrator.GroupTranslatedIntoFiles` adds chains and carriers in lockstep (lines 411–419), so the bounds check is defensive only. | Info | Harmless but unnecessary; just noting. |
| Default `recorder = null` parameter is safely handled — every recording call is `recorder?.Record(...)`. Verified at 10 sites. No NullReferenceException risk. | Info | Clean null-handling. |
| `CarrierAssignmentRecorder.GetAssigned` returns a *new* empty `HashSet<int>` on cache miss instead of caching it (CarrierAssignmentRecorder.cs lines 36–37). Each miss allocates. This is also called inside `ComputeUnassignedPIndices` once per carrier check — minor allocation cost. | Info | Negligible at generator-time; not a correctness bug. |
| `CarrierAssignmentRecorder` is not thread-safe (plain `Dictionary`/`HashSet`). It's owned by the per-FileEmitter instance, and FileEmitter is constructed and used per `Emit()` call. Source generators run single-threaded per file. No threading concern. | Info | OK as-is; static body emitters take recorder by parameter, not via shared static state. |
| `EmitJoin` branch `hasResolvableCapturedParams = !isCrossJoin && !site.IsNavigationJoin && clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true` (JoinBodyEmitter.cs line 48). Navigation joins are excluded — but the navigation-join lambda signature (line 127) hardcodes `_`. If a future navigation-join lambda accepts captures, the lambda parameter would still be `_` while the extraction plan emits `func.Target`. Latent (probably impossible today since `Func<T, NavigationList<U>>` shouldn't capture). | Low | Documents a latent inconsistency. QRY037 would catch it; not silent. |
| Boundary: `EmitCarrierClauseBody` short-circuits when `clauseParams == null \|\| clauseParams.Count == 0`. For the `EmitJoin` not-first-in-chain branch with captures, `siteParams.Count > 0` is checked at the call site, but the callee re-derives params from `site.Clause.Parameters`. If `site.Clause` is null (`clauseInfo` null), `EmitCarrierClauseBody` would emit no bindings and just return — silent no-op. The not-first-in-chain branch is gated by the outer `if (prebuiltChain != null && clauseInfo != null && clauseInfo.IsSuccess)` (JoinBodyEmitter.cs line 65 / 107), so `clauseInfo` is non-null when this branch runs. Safe. | Info | Verified safe; documenting the chain of guards. |
| `carrierToChain` map (FileEmitter.cs lines 538–545) is populated FIRST chain wins for a given class name (via `!ContainsKey`). For deduplicated carriers (multiple chains share a class), the diagnostic location points to the first owning chain. Plan says "if lookup fails, use Location.None equivalent" — implementation does `if (!TryGetValue) continue;` (line 561), silently dropping the diagnostic. | Low | If `carrierToChain` lookup fails for an eligible-carrier chain, QRY037 would NOT fire for that carrier. The dedup logic ensures lookup succeeds when carrier is eligible & has a class name — but a defensive `Location.None` fallback would be more aligned with the plan. |
| `EmitCarrierAssignmentDiagnostics` skips carriers where `!carrier.IsEligible \|\| string.IsNullOrEmpty(carrier.ClassName)`. Correct — ineligible carriers don't get classes emitted, so no P-fields exist. | Info | Correct gating. |
| `EmitCarrierClass` (CarrierEmitter.cs line 388) emits `= null!` initializer ONLY for fields where `field.IsReferenceType && !field.TypeName.EndsWith("?")`. The decision rationale (workflow line 67) about CS0649 coverage is supported by the code. | Info | Confirmed. |

## Security
No concerns.

## Test Quality
| Finding | Severity | Why It Matters |
|---|---|---|
| Phase 2 tightening: `bias = 100.00m` (CrossDialectDistinctOrderByTests.cs line 344) and the new `AssertOrderByCapturedParamBound` helper (lines 401–409) inspect `diag.AllParameters[1].Value`. This is meaningful — directly observes the bound carrier P-field. Pre-fix, this would observe `0m` instead of `100m`. Per-dialect coverage (4 calls). Correct strategy. | Info | Strong runtime regression coverage. |
| Phase 3 + 4: Each generation test does three assertions — `Does.Contain("PrebuiltDispatch")`, `Does.Contain("__ExtractVar_<var>_")`, `Does.Match(@"__c\.P\d+\s*=\s*<var>!")`. The regex catches the assignment; the `__ExtractVar_*` substring catches the extraction call. Both would FAIL on pre-Phase-1 emitter output. Verified. | Info | Coverage as planned. |
| Phase 4 `CarrierGeneration_GroupBy_WithCapturedVariable_EmitsExtractionAndAssignment` uses `o.UserId + bucketOffset` as the GroupBy key (line 4264). The ChainAnalyzer-permissibility wasn't verified per the plan's caveat ("If GroupBy(o => o.UserId + bucketOffset) is rejected by the analyzer, fall back to a more permissive shape — verify by reading ChainAnalyzer"). Test passes per workflow (3,241 green), so the shape was accepted. Correct. | Info | OK. |
| Phase 5 unit tests on `ComputeUnassignedPIndices` cover: all-assigned (empty result), single-unassigned, many-unassigned, no P-fields (only Mask/Limit, plus a deliberate "Ptr" non-numeric suffix as edge case). Covers the DESIGN-phase edge cases ("non-numeric P-suffix; no P-fields at all"). Recorder happy-path + idempotency tests too. | Info | Strong coverage of the gap-detection function. |
| `RealChains_DoNotTriggerQRY037_NoFalsePositives` (lines 4488–4517) is partially redundant with the broader test suite (3,088 tests — any false-fire breaks all builds). However, it provides a minimal, focused fixture useful for debugging QRY037 false-positives in isolation. | Info | Net positive; small. |
| No test exercises the `EmitCarrierAssignmentDiagnostics` path itself (the `carrierToChain` map building, the `_emitDiagnostics.Add` call, the `Location` resolution). A bug in that 40-line method would slip past Phase 5 tests. The Phase 5.5 plan's alternative (synthetic FileEmitter input) was deferred. | Low | Test coverage gap for the diagnostic-emission glue, though the consequence of a bug there is "QRY037 doesn't fire when it should" rather than silent miscompilation. |
| Mask-gated branches "any-branch assignment counts" decision (workflow line 64). No test verifies this — i.e., a P-field assigned only on one mask branch should not fire QRY037. The recorder is mask-agnostic by construction, so the rule is satisfied trivially. | Info | The design naturally satisfies this; no test needed. |
| Test `BuildCarrierPlan` helper (line 4520) accepts `params CarrierField[]` — clean factory. Reasonable API. | Info | Clean. |
| `Is.Empty` assertions on collections are meaningful (NUnit checks `Count == 0`); not the "trivially-true on empty set" anti-pattern. | Info | OK. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---|---|---|
| The four emitter fixes mirror the existing pattern in `EmitWhere` / `EmitJoinedWhere` / `EmitHaving` / `EmitModificationWhere`: detect captures via `clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath)`, use `funcParamName = hasResolvableCapturedParams ? "func" : "_"`, conditionally emit `[UnconditionalSuppressMessage]`. The fix is precisely a clone of the already-correct pattern. Verified by Grep — same 5-line scaffold appears 6 times in ClauseBodyEmitter.cs and 3 times in JoinBodyEmitter.cs (one per clause kind). | Info | Strong consistency with existing patterns. |
| Subtle drift: existing emitters (e.g. `EmitJoinedWhere` on master) gate the suppression attribute on `methodFields.Count > 0` rather than `hasResolvableCapturedParams`. The new emitters gate on `hasResolvableCapturedParams`. Both work, but the existing emitters use a slightly different signal. Inconsistent — not problematic since neither emits a wrong attribute, but worth noting if future refactoring unifies the gating. | Low | Two equivalent gating predicates exist; mild inconsistency. |
| `CarrierAssignmentRecorder` follows a "Recorder" naming pattern. No other "*Recorder" types exist in the codebase (Grep confirms). Naming is descriptive but novel. Alternatives (`AssignmentTracker`, `PFieldRegistry`) would also have worked; this isn't a problem. | Info | Acceptable new naming; no existing convention to follow. |
| Threading pattern: `CarrierAssignmentRecorder? recorder = null` is added at the end of every emit method's parameter list. This matches the codebase's general pattern of optional trailing parameters (e.g. `operandCarrierNames`, `methodFields`). Consistent. | Info | Plumbing pattern matches existing code. |
| `DiagnosticDescriptors.cs` QRY037 declaration (lines 535–545) follows the same template as QRY030–QRY036 directly above it: same 6-line scaffold (id, title, messageFormat, category, defaultSeverity, isEnabledByDefault, description). Matches. | Info | Idiomatic. |
| `QuarryGenerator.cs` line 768 adds `DiagnosticDescriptors.CarrierParameterFieldUnassigned` to the descriptor list. Inserted before the `RawSqlUnresolvableColumn` block — consistent with the existing list ordering (chain diagnostics QRY030–QRY037 grouped together). | Info | Order matches semantic grouping. |
| `_emitDiagnostics.Add(new Models.DiagnosticInfo(...))` invocation pattern in `EmitCarrierAssignmentDiagnostics` (line 562) matches the existing usage at FileEmitter.cs line 298 (QRY041) — same constructor shape, same params-string-array message args. Consistent. | Info | Idiomatic. |
| No utility duplicated. `ComputeUnassignedPIndices` is novel; no existing helper iterates carrier P-fields with this filter pattern. | Info | New code, justified. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---|---|---|
| All changed emit method signatures (~14 methods across `ClauseBodyEmitter`, `JoinBodyEmitter`, `CarrierEmitter`, `SetOperationBodyEmitter`, `TransitionBodyEmitter`) are `internal` and gain a trailing optional `CarrierAssignmentRecorder? recorder = null` parameter. Source-compatible for all internal callers. No external consumer can call these methods. | Info | No public-API break. |
| `CarrierAssignmentRecorder` is `internal sealed class` in `Quarry.Generators.CodeGen` namespace. New internal type. Not visible to external assemblies. | Info | Internal-only. |
| `QRY037` is a NEW Error-severity diagnostic. If the generator pipeline has a defect that causes a real-world chain to hit QRY037, downstream consumer builds will FAIL. This is a breaking change for builds — but it's the intended behavior (replaces silent default(T) binding with hard build failure). Workflow says 3,241 tests green, no QRY037 fires on the test suite. | Info | Severity escalation is by design. Document in release notes. |
| The `[InternalsVisibleTo]` access required by `CarrierGenerationTests.cs` calling `Quarry.Generators.CodeGen.FileEmitter.ComputeUnassignedPIndices`, `CarrierAssignmentRecorder`, `CarrierPlan`, etc. presumed pre-existing (tests already use `Quarry.Generators.*` namespaces). Verified: existing test file references `Quarry.Generators.IR`, `Quarry.Generators.Models`, etc. | Info | No new InternalsVisibleTo needed. |
| No new dependencies. No package upgrades. | Info | Clean. |
| `llm.md` documentation updated to include QRY037 in the chain-diagnostics paragraph. Consumer-facing prose, not code. | Info | Doc consistency. |
| No SQL output diff expected (workflow Phase 1 gate: "no diff in any Generation/* test or CrossDialect* test output"). The fix changes interceptor body emission, not SQL — no .g.sql baseline files affected. | Info | Confirmed by green test suite. |
| Migration: existing consumers compiled against pre-fix Quarry would have had `CS0649` warnings on Chain_N.P{i} fields for value-type captures. Post-fix: warnings disappear, parameter binding becomes correct. No source changes required by consumers. Behavioral fix only — silently incorrect queries become silently correct. | Info | Net positive for downstream. |
