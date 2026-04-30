## Summary
- Fixes a class-of-bug where four clause-emitter functions (`JoinBodyEmitter.EmitJoinedOrderBy`, `ClauseBodyEmitter.EmitOrderBy`, `ClauseBodyEmitter.EmitGroupBy`, `JoinBodyEmitter.EmitJoin`) silently dropped captured-variable extraction. Symptom that surfaced this: `CS0649 Field 'Chain_N.Px' is never assigned to, and will always have its default value 0` on `*Db.Interceptors.*CrossDialectDistinctOrderByTests.g.cs`. The compiler caught it for `decimal bias` because decimals are value types; **CS0649 would not have caught the same bug for non-nullable reference-type captures** (`string`, custom classes), since the carrier emits those fields with `= null!` to suppress CS8618 — silently shipping `null` parameter binding.
- Adds **QRY037** (Error severity), a generator self-check that fails the build if any carrier `P{i}` field is declared without a corresponding `__c.P{i} = ...` assignment. Closes the CS0649 coverage hole permanently.

## Reason for Change

The failing CS0649 was the visible tip of a structurally-symmetric defect across four emitters:

1. The lambda parameter was hardcoded to `_`, so the carrier extraction-plan body emitted `func.Target` against an undeclared name.
2. The generic-key carrier path (taken when `KeyTypeName` couldn't be resolved) emitted only `cast → mask bit → return`, completely skipping the per-clause extraction plan.

Result: the carrier declared `__ExtractVar_<var>_<i>` accessor methods and a `Px` field, the SQL referenced `@px`, but no interceptor body called the extractor or wrote to `Px`. With `bias = 0.00m` in the test data, `default(decimal) == 0` happened to match. With any non-zero captured value the SQL would silently bind 0 — class-of-bug hidden by data coincidence.

## Impact

- **Generator (`Quarry.Generator`):** four emitter fixes + new QRY037 self-check + recorder threading through ~14 emit methods.
- **Runtime (`Quarry`):** zero. No public API changes, no SQL output changes.
- **Test suite:** **+17 new tests** (3,084 + 146 = 3,230 baseline → 3,101 + 146 = **3,247 total**). All green.
- **Downstream consumers:** behavioral fix only. Pre-fix: silently incorrect queries when capturing a non-nullable reference-type variable in OrderBy/ThenBy/GroupBy/Join condition. Post-fix: correct binding. Existing CS0649 warnings on `Chain_N.P*` fields (where the bug *did* surface) disappear.

## Plan items implemented as specified

- **Phase 1** — Symmetric emitter fix in `ClauseBodyEmitter.EmitOrderBy`, `ClauseBodyEmitter.EmitGroupBy`, `JoinBodyEmitter.EmitJoinedOrderBy`, `JoinBodyEmitter.EmitJoin`: detect captures via `clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath)`, switch lambda name `_` → `func`, emit `[UnconditionalSuppressMessage("Trimming", "IL2075", …)]` conditionally, route the generic-key carrier path through `CarrierEmitter.EmitCarrierClauseBody`. For `EmitJoin` not-first-in-chain branch, also bind params via `EmitCarrierClauseBody` when `siteParams.Count > 0`.
- **Phase 2** — `Distinct_OrderBy_NonProjectedExprWithCapturedParam_ThreadsParamIndex` tightened: `bias = 100.00m` (non-zero) + new `AssertOrderByCapturedParamBound` helper inspects `diag.AllParameters[i].Value` across all four dialects.
- **Phase 3** — `CarrierGeneration_OrderByOnJoinedBuilder_WithCapturedVariable_EmitsExtractionAndAssignment` asserts the OrderBy interceptor body contains `__ExtractVar_bias_*` and `__c.P{n} = bias!`.
- **Phase 4** — Three generation tests covering the latent paths: single-table OrderBy with `int offset`, GroupBy with `int bucketOffset`, Join condition with `decimal minTotal`.
- **Phase 5** — QRY037 descriptor in `DiagnosticDescriptors.cs` (Error severity), registered in `QuarryGenerator.GetDescriptorById`, `CarrierAssignmentRecorder` threaded through every emit method that writes to P-fields, post-emission gap detection in `FileEmitter.EmitCarrierAssignmentDiagnostics`, plus 13 unit tests covering the recorder, the gap-detection helper, and the diagnostic-emission integration.

## Deviations from plan implemented

- **Phase 5.2 (DESIGN-phase decision revisited during IMPLEMENT):** When threading the recorder through emit methods, two additional emit sites that the plan didn't enumerate also write to P-fields and needed plumbing: `SetOperationBodyEmitter.EmitSetOperation` (Union/Intersect/Except direct-form `__c.P{i} = __op.P{j}`) and `TransitionBodyEmitter.EmitCteDefinition` (CTE With() direct-form `__c.P{i} = __inner.P{j}`). Both threaded; QRY037 false-fired on `CrossDialectSetOperationTests` until plumbed. The plan's "every Px assignment site routes through the recorder" intent is now structurally complete.
- **Phase 5.5 (test strategy):** The plan's "synthetic FileEmitter input" approach was pragmatically replaced with a pure-helper extraction. `FileEmitter.ProduceCarrierAssignmentDiagnostics(carriers, recorder, resolveLocation)` is the testable contract; 6 unit tests cover it directly without constructing a full `AssembledPlan`. Plus `FileEmitter.ComputeUnassignedPIndices(plan, recorder)` (5 unit tests). The "no false positives" side gets free coverage from 3,088 real chains in `Quarry.Tests` (any QRY037 fire on a real chain breaks the build).

## Gaps in original plan implemented

The DESIGN-phase decision said "Track during emission". Implementation reality: the assignment sites are localized to a few helpers in `CarrierEmitter`, but the call sites span every body-emitter. Plumbing surface = ~14 method signatures. The DESIGN reasoning ("track during emission, no string scanning") still holds — the plumbing cost was justified once we discovered CS0649 doesn't catch non-nullable reference-type captures.

## Migration Steps

None required for downstream consumers. Pre-fix CS0649 warnings on `Chain_N.P*` fields disappear after this change; queries that previously bound `default(T)` for captured values (the silent-correctness class) start binding correct values.

## Performance Considerations

- Build-time: minor cost from the per-carrier P-index recording (`HashSet.Add` per assignment site) and the post-emission gap-detection scan (one pass per carrier). Negligible vs. the rest of the generator.
- Runtime: zero. The fix changes interceptor bodies but the generated SQL and carrier shapes are unchanged for already-correct chains.

## Security Considerations

None. Internal source generator changes only. The QRY037 message format is a fixed string with `{0}` (carrier class name) and `{1}` (P-field index) — no user-controllable content.

## Breaking Changes

- **Consumer-facing:** None. No public API changes.
- **Internal:** `internal static` emit methods in `CarrierEmitter`/`ClauseBodyEmitter`/`JoinBodyEmitter`/`SetOperationBodyEmitter`/`TransitionBodyEmitter` gain an optional trailing `CarrierAssignmentRecorder? recorder = null` parameter. Source-compatible for all in-tree callers; no consumer outside `Quarry.Generator` calls these.
- **Diagnostic addition:** **QRY037 is a NEW Error-severity diagnostic.** A generator regression that produces an unassigned carrier P-field will now fail the build instead of silently shipping `default(T)` parameter binding. By design.
