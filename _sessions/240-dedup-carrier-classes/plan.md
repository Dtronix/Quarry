# Plan: Deduplicate Structurally Identical Carrier Classes

## Key Concepts

**Carrier structural key:** A composite value that captures everything contributing to the generated carrier class body text — fields, mask config, extractor methods, SQL variants, reader delegate, collection cache presence, and resolved interfaces. Two carriers with the same key produce identical class text (modulo class name).

**Dedup site:** The carrier emission loop in `FileEmitter.Emit()` (lines 134-218). This is where carrier class names are assigned and classes are emitted. Dedup operates here by grouping carriers by structural key and emitting each unique class once.

**Lookup invariant:** Each chain retains its own `CarrierPlan` instance (with the shared class name). The existing `carrierLookup`, `carrierClauseLookup`, and `operandCarrierNames` dictionaries continue to map site UniqueIds to per-chain `(CarrierPlan, AssembledPlan)` pairs. No changes needed to downstream interceptor body emission — each chain's clause sites still resolve to the correct extraction plans via their own CarrierPlan.

**Extractor safety:** Extraction method names use positional `clauseIndex` (not site UniqueId), so structurally identical chains produce identical `[UnsafeAccessor]` method names. A shared carrier class has all needed methods.

## Phase 1: Add CarrierStructuralKey to FileEmitter

Add a private nested readonly struct `CarrierStructuralKey` inside `FileEmitter` that implements `IEquatable<CarrierStructuralKey>`.

The key compares:
- `CarrierPlan.Fields` — sequence equality on (Name, TypeName, Role, IsReferenceType)
- `CarrierPlan.MaskType` — string equality
- `CarrierPlan.MaskBitCount` — int equality
- Flattened extractors from `CarrierPlan.ExtractionPlans` — sequence equality on (MethodName, VariableName, VariableType, DisplayClassName, CaptureKind, IsStaticField). Ignore `ClauseUniqueId` and `DelegateParamName` since they don't affect the class body.
- `AssembledPlan.SqlVariants` — dictionary equality on (int key → AssembledSqlVariant)
- `AssembledPlan.ReaderDelegateCode` — string equality (null if not self-contained, checked via `CarrierEmitter.IsReaderSelfContained`)
- `AssembledPlan.ChainParameters` collection presence — bool: `chain.ChainParameters.Any(p => p.IsCollection)`
- Resolved interfaces — sequence equality on string[]

Add a static factory method `Create(CarrierPlan, AssembledPlan, string[])` that builds the key. The method calls `CarrierEmitter.IsReaderSelfContained` to decide whether `ReaderDelegateCode` is included.

`GetHashCode` combines: Fields.Count, MaskBitCount, MaskType, SqlVariants count, first SQL string hash (if any), interfaces count.

`Equals` does full deep comparison using `EqualityHelpers.SequenceEqual` and `EqualityHelpers.DictionaryEqual`.

**Tests:** Unit tests are deferred to Phase 3 (integration test covers this phase too). No existing tests affected.

**Commit:** "Add CarrierStructuralKey for carrier deduplication"

## Phase 2: Wire dedup into FileEmitter carrier emission loop

Modify the carrier emission loop in `FileEmitter.Emit()` (lines 142-218). After the eligibility checks and interface resolution (which must still happen for every chain), add a dedup gate:

```csharp
var carrierDedup = new Dictionary<CarrierStructuralKey, string>(); // key → className
```

For each eligible carrier, after resolving interfaces:
1. Compute `var key = CarrierStructuralKey.Create(carrierPlan, chain, resolvedInterfaces);`
2. If `carrierDedup.TryGetValue(key, out var existingName)`:
   - Assign `carrierPlan.ClassName = existingName;` (reuse name)
   - Skip `CarrierEmitter.EmitCarrierClass(...)` call
   - Still register all lookups (carrierLookup, carrierClauseLookup, operandCarrierNames, carrierFirstClauseIds) — these are per-chain
3. Else:
   - Assign `carrierPlan.ClassName = $"Chain_{carrierIndex}";` and increment carrierIndex (as before)
   - `carrierDedup[key] = carrierPlan.ClassName;`
   - Emit the carrier class (as before)

The `carrierIndex` counter now only increments for unique carriers, so the numbering stays sequential with no gaps.

**Important detail:** `ImplementedInterfaces` and `BaseClassName` must still be set on every carrier's `CarrierPlan` (even deduped ones) because downstream code may read them. The current code already does this before the emission call, so this is preserved.

**Tests:** Run full test suite. All 3,190 tests should pass unchanged — no existing test produces structurally identical carriers in the same file.

**Commit:** "Deduplicate structurally identical carrier classes in FileEmitter"

## Phase 3: Add deduplication tests

Add tests to `CarrierGenerationTests.cs` that exercise the dedup path:

1. **`DuplicateCarriers_SameWherePattern_SharedClass`** — Two chains with identical `Where(u => u.IsActive)` patterns on the same entity. Verify: only one `file sealed class Chain_0` in output, both interceptors reference `Chain_0`.

2. **`DuplicateCarriers_DifferentPatterns_SeparateClasses`** — Two chains with different where clauses (different SQL). Verify: two carrier classes `Chain_0` and `Chain_1`.

3. **`DuplicateCarriers_SameFieldsDifferentSql_SeparateClasses`** — Two chains with same parameter types but different SQL (e.g., different column in Where). Verify: not merged (SQL differs).

**Commit:** "Add tests for carrier class deduplication"

## Dependencies

Phase 2 depends on Phase 1 (uses the key type).
Phase 3 depends on Phase 2 (tests the wired-up behavior).
