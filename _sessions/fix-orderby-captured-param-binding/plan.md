# Implementation Plan: fix-orderby-captured-param-binding

## Concepts

### The bug (recap)
Four clause-emitter functions (`JoinBodyEmitter.EmitJoinedOrderBy`, `ClauseBodyEmitter.EmitOrderBy`, `ClauseBodyEmitter.EmitGroupBy`, `JoinBodyEmitter.EmitJoin`) hardcode the lambda parameter name to `_` and have a generic-key carrier path that skips the per-clause extraction plan. When the lambda contains captured locals, the carrier's `Px` field is declared and `__ExtractVar_<var>_<i>` is emitted on the carrier, but no interceptor body ever calls the extractor or writes to `Px`. The compiler flags this as `CS0649`. At runtime, the SQL parameter binds `default(T)` instead of the captured value.

### The fix shape
Mirror the already-correct pattern from `EmitWhere` / `EmitJoinedWhere`:
1. Detect captures: `clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath)`.
2. Name the lambda parameter `func` (or `_` when no captures) so `func.Target` is reachable.
3. Emit `[UnconditionalSuppressMessage("Trimming", "IL2075", …)]` only when captures present.
4. Funnel the generic-key carrier path through `CarrierEmitter.EmitCarrierClauseBody` so it shares the keyType-known path's extraction + assignment + masked-return code.
5. For `EmitJoin`'s not-first-in-chain branch, also bind params via `EmitCarrierClauseBody` when `siteParams.Count > 0`.

### Generator self-check (QRY037)
Track every `__c.P{i} = ...` assignment emitted into a per-carrier `HashSet<int>`. After the file is emitted, compare against `CarrierPlan.Fields` filtered to P-fields. Any gap → emit `QRY037` (Error) at the chain-root site location. Implementation lives in `FileEmitter` and the `CarrierEmitter` binding helpers it delegates to. No mask-gating: any-branch assignment satisfies the rule.

### Why this self-check is the right shape
The rule is structural: if the generator declares a `P{i}` field on a carrier, *some* interceptor on that carrier must write to it before the terminal binds parameters. A field that is declared but never written is provably wrong (the parameter binding will use `default(T)`). That makes it safe to fail-fast on. This is much cheaper to detect at generation time than at runtime, and CS0649 is too coarse a signal — it surfaces the symptom but not the chain or the source location.

## Phases

### Phase 1 — Symmetric emitter fix (already applied in worktree)
Repair the four emitters so captured-variable extraction emits correctly:

- `JoinBodyEmitter.EmitJoinedOrderBy`: detect captures, switch `_` → `func`, emit suppression attribute, route generic-key path through `EmitCarrierClauseBody` with the right `castTarget`.
- `ClauseBodyEmitter.EmitOrderBy`: same.
- `ClauseBodyEmitter.EmitGroupBy`: same.
- `JoinBodyEmitter.EmitJoin`: same, plus add `EmitCarrierClauseBody` call on the not-first-in-chain branch when `siteParams.Count > 0` (currently just `Unsafe.As<...>(builder)` with no extraction).

**Files touched:**
- `src/Quarry.Generator/CodeGen/ClauseBodyEmitter.cs`
- `src/Quarry.Generator/CodeGen/JoinBodyEmitter.cs`

**Tests:** None added in this phase. Existing 3,084 `Quarry.Tests` + 146 `Quarry.Analyzers.Tests` must remain green. CS0649 warnings on `*Db.Interceptors.*CrossDialectDistinctOrderByTests.g.cs` should disappear after build. No diff in any `Generation/*` test or `CrossDialect*` test output.

**Commit message:** `fix(generator): emit captured-variable extraction for OrderBy/ThenBy/GroupBy/Join interceptors`

---

### Phase 2 — Tighten the runtime regression test
The existing `Distinct_OrderBy_NonProjectedExprWithCapturedParam_ThreadsParamIndex` test in `src/Quarry.Tests/SqlOutput/CrossDialectDistinctOrderByTests.cs` uses `decimal bias = 0.00m`, which silently passes even with the pre-fix bug because `default(decimal) == 0`. Tighten it:

1. Change `bias` to a non-zero distinguisher value (e.g. `100.00m`). Pick a value that, when added to `Total`, doesn't change the relative *order* of rows but still proves the parameter was bound (e.g. assert the SQL still binds `bias = 100.00m` via diagnostics; or assert sorted ordering across a delta that requires correct binding).
2. Add an assertion using `lt.ToDiagnostics().AllParameters` to verify the bound parameter value is the captured `bias`, not `0`. This is the cleanest cross-dialect proof — it inspects the carrier's bound parameter directly without depending on per-dialect ordering quirks.

The assertion shape:
```csharp
var pgDiag = pg.ToDiagnostics();
Assert.That(pgDiag.AllParameters.Last().Value, Is.EqualTo(bias),
    "@p1 (bias) must bind the captured value, not default(decimal). Regression: pre-fix bug silently bound 0.");
```
Repeat the assertion for all 4 dialects so the guarantee is per-dialect.

**Files touched:**
- `src/Quarry.Tests/SqlOutput/CrossDialectDistinctOrderByTests.cs`

**Tests:** Modified test stays green after Phase 1 fix; would FAIL on pre-Phase-1 code (proving regression coverage).

**Commit message:** `test(orderby): assert OrderBy captures bind correctly across dialects`

---

### Phase 3 — Generation-level assertion for the original OrderBy capture trigger
Add a new test in `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` that compiles a small program containing an OrderBy chain with a captured local, runs the generator, and asserts the emitted interceptor source contains:
- `__ExtractVar_<var>_<i>(__target)` — the extraction call
- `__c.P{n} = <var>!;` — the field assignment

Pattern (use existing test infrastructure: `CreateCompilation`, `RunGenerator`, scan `result.GeneratedTrees` for the `Interceptors.*.g.cs` file):

```csharp
[Test]
public void CarrierGeneration_OrderByOnJoinedBuilder_WithCapturedVariable_EmitsExtractionAndAssignment()
{
    var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public class Queries
{
    private readonly TestDbContext _db;
    public Queries(TestDbContext db) { _db = db; }
    public string Test()
    {
        decimal bias = 5.0m;
        return _db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                  .Where((u, o) => o.Total > 0)
                  .OrderBy((u, o) => o.Total + bias)
                  .Distinct()
                  .Select((u, o) => u.UserName)
                  .ToDiagnostics().Sql;
    }
}";

    var code = RunGeneratorAndGetInterceptorsSource(source);
    Assert.That(code, Does.Contain("__ExtractVar_bias_"),
        "OrderBy interceptor must emit the bias extraction call");
    Assert.That(code, Does.Match(@"__c\.P\d+\s*=\s*bias!"),
        "OrderBy interceptor must assign bias to a carrier P-field");
}
```

**Files touched:**
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` (one new test added).

**Helper extraction:** if a `RunGeneratorAndGetInterceptorsSource(source)` helper doesn't exist, inline the `CreateCompilation` + `RunGenerator` + tree-search pattern that the existing `CarrierGeneration_*` tests use.

**Commit message:** `test(generation): assert OrderBy emits captured-var extraction + P-field assignment`

---

### Phase 4 — Generation-level assertions for the three latent paths
Add three more `CarrierGenerationTests` in the same file, one per latent path:

1. **Single-table OrderBy with capture** — `_db.Users().Where(...).OrderBy(u => u.UserId + offset).Select(u => u.UserName)` with `int offset` captured. Asserts `__ExtractVar_offset_*` and `P{n} = offset!` appear in the interceptor body. This guards `ClauseBodyEmitter.EmitOrderBy`.

2. **GroupBy with capture** — `_db.Orders().GroupBy(o => o.UserId + bucketOffset)…` with `int bucketOffset` captured. (If `GroupBy(o => o.UserId + bucketOffset)` is rejected by the analyzer for shape reasons, fall back to a more permissive shape — verify by reading `ChainAnalyzer` what shapes are accepted; do NOT assume.) Asserts the emitted GroupBy interceptor extracts and assigns the captured value. Guards `ClauseBodyEmitter.EmitGroupBy`.

3. **Join condition with capture** — `_db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id && o.Total > minTotal)…` with `decimal minTotal` captured. Asserts the Join interceptor body contains the extraction + assignment. Guards `JoinBodyEmitter.EmitJoin`.

For each, also assert `code.Does.Contain("PrebuiltDispatch")` to confirm the chain hit the carrier path (not RuntimeBuild → QRY032).

**Files touched:**
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` (three new tests).

**Verification gate:** all three tests must pass on Phase-1 code AND fail on pre-Phase-1 code (mentally walk through what the unfixed emitter would emit for each shape, confirm assertions would fail).

**Commit message:** `test(generation): cover GroupBy/single-OrderBy/Join captured-variable emission`

---

### Phase 5 — QRY037 generator self-check
Add a generation-time self-check that fails the build if any carrier P-field is declared but never assigned in any emitted interceptor body. Five substeps:

#### 5.1 — Add `DiagnosticDescriptor` for QRY037
In `src/Quarry.Generator/DiagnosticDescriptors.cs`, alongside the existing `QRY030–QRY036` chain diagnostics, add:

```csharp
/// <summary>
/// QRY037: Carrier parameter field has no assignment.
/// Severity: Error
/// </summary>
public static readonly DiagnosticDescriptor CarrierParameterFieldUnassigned = new(
    id: "QRY037",
    title: "Carrier parameter field unassigned",
    messageFormat: "Carrier '{0}' declares parameter field 'P{1}' but no clause interceptor assigns it. The corresponding SQL parameter would silently bind default(T). This is a generator defect — please file an issue.",
    category: Category,
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true,
    description: "Every Px field on a generated carrier class must be assigned by at least one clause interceptor body. " +
                 "If this diagnostic fires, an emitter has skipped the per-clause extraction plan for a captured-variable parameter " +
                 "(see fix-orderby-captured-param-binding). The query would silently bind the parameter's default value at runtime.");
```

Update `llm.md` diagnostic table to include the new entry.

#### 5.2 — Add per-carrier assignment tracking
In `src/Quarry.Generator/CodeGen/FileEmitter.cs`, add a private field and an `AssignmentRecorder` instance:

```csharp
private readonly Dictionary<string, HashSet<int>> _assignedPIndicesByCarrier = new();
```

Add an internal `AssignmentRecorder` API in `CarrierEmitter` (or as a new lightweight type) that the binding helpers call when emitting `__c.P{i} = ...`. The recorder owns mutation of `_assignedPIndicesByCarrier[carrierClass]`.

Thread it through every emit-binding site:
- `EmitCarrierClauseBody` (carrier param assignment loop, lines ~280–326)
- `EmitCarrierParamBindings` (private helper, line ~1061)
- `EmitCollectionContainsExtraction` (collection IN-clause expansion)
- Any `__c.P{i} = ...` direct emission site (e.g. `__c.P{globalIdx} = (cast)value!;` for SET clauses)

Verify by searching `__c.P` in `CarrierEmitter.cs` and ensuring every assignment site routes through the recorder.

#### 5.3 — Post-emission gap detection
After `FileEmitter.Emit()` finishes processing all chains/carriers, iterate `_carrierPlans` and for each carrier:

```csharp
foreach (var carrier in _carrierPlans ?? Enumerable.Empty<CarrierPlan>())
{
    var declaredP = carrier.Fields
        .Select(f => f.Name)
        .Where(name => name.StartsWith("P") && int.TryParse(name.Substring(1), out _))
        .Select(name => int.Parse(name.Substring(1)))
        .ToHashSet();

    _assignedPIndicesByCarrier.TryGetValue(carrier.ClassName, out var assigned);
    assigned ??= new HashSet<int>();

    foreach (var idx in declaredP.Except(assigned).OrderBy(i => i))
    {
        // Find the chain whose ChainRoot/ExecutionSite owns this carrier
        var rootSite = ResolveRootSiteForCarrier(carrier, _chains);
        _emitDiagnostics.Add(new Models.DiagnosticInfo(
            "QRY037",
            new Models.DiagnosticLocation(rootSite.FilePath, rootSite.Line, rootSite.Column),
            new object[] { carrier.ClassName, idx }));
    }
}
```

Where `ResolveRootSiteForCarrier` looks up the carrier's owning `AssembledPlan` and returns the chain-root site for source-location purposes. If lookup fails (defensive), use `Location.None` equivalent.

#### 5.4 — Register descriptor in `QuarryGenerator.GetDescriptorById`
In `src/Quarry.Generator/QuarryGenerator.cs`, find the `GetDescriptorById` switch (or dictionary), add an entry mapping `"QRY037"` to `DiagnosticDescriptors.CarrierParameterFieldUnassigned`. Now `_emitDiagnostics` entries with that ID surface as a real `Diagnostic` to the consumer compilation.

#### 5.5 — Self-check test
Add a test in `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` that proves the diagnostic fires when expected. Approach: construct a synthetic `FileEmitter` input where a `CarrierPlan` declares a P-field that no site assigns. The cleanest implementation is an **internal test hook** — expose a way to call `FileEmitter` with a hand-built `CarrierPlan` that has more P-fields than the input sites would assign. Then assert `EmitDiagnostics` contains a QRY037 entry.

Alternative: write the test as a black-box check — assert that on Phase-1 code, no chain in `Quarry.Tests` triggers QRY037 (sanity check) — and add a separate fixture test that exercises a deliberately-misshapen carrier. The black-box "no-fire" check is essentially what the rest of the test suite already verifies.

Pick the cleanest approach during implementation; if the synthetic-CarrierPlan test is awkward, document the limitation in the test file and rely on the symmetric-fix argument — the diagnostic exists to catch regressions, and any future regression of the bug shape will be caught by the existing tests in Phase 4 *plus* the QRY037 self-check.

#### 5.6 — Documentation
Update `llm.md` Diagnostics section with the QRY037 row (Error, "Carrier parameter field unassigned"). Update `_sessions/fix-orderby-captured-param-binding/workflow.md` decisions to record the implementation approach if it diverges.

**Files touched:**
- `src/Quarry.Generator/DiagnosticDescriptors.cs`
- `src/Quarry.Generator/QuarryGenerator.cs`
- `src/Quarry.Generator/CodeGen/FileEmitter.cs`
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`
- `src/Quarry.Tests/Generation/CarrierGenerationTests.cs`
- `llm.md`

**Tests:** Phase-5 self-check test plus the no-fire black-box sanity check (already implicit in green Phase-1 tests).

**Commit message:** `feat(generator): add QRY037 self-check for unassigned carrier parameter fields`

---

## Dependencies between phases
- Phase 1 must come first. Phases 2–5 all assume the symmetric fix is in place.
- Phases 2 / 3 / 4 are independent of each other once Phase 1 is in.
- Phase 5 must come after Phases 1–4 so that the diagnostic doesn't false-fire on partially-fixed state.

## Test gates per phase
| Phase | New tests | Existing tests | Must hold |
|-------|-----------|----------------|-----------|
| 1 | none | all | 3,230/3,230 green; CS0649 cleared; no SqlOutput diff |
| 2 | 0 (modified) | all | 3,230/3,230 green; modified test asserts captured value bound |
| 3 | 1 | all | 3,231/3,231 green; new test asserts emission text |
| 4 | 3 | all | 3,234/3,234 green; new tests cover latent paths |
| 5 | 1 | all | 3,235/3,235 green; QRY037 doesn't fire on the test suite; QRY037 fires on synthetic broken input |

## Phase boundaries are commit boundaries
Each phase is independently committable. After each commit, run the full test suite. If any phase introduces unexpected failure, treat it as the only failure to debug — earlier phases are guaranteed green by their own gates.
