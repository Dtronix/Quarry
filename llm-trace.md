# Quarry .Trace() Chain Tracing System

Guide for LLM agents working on the Quarry source generator. Covers how to use `.Trace()` to diagnose query chain compilation issues, how to read the output, and how to extend the trace system with additional data.

## 1. Enabling Trace Output

### Consumer project setup

Add `QUARRY_TRACE` to the consumer project's `.csproj`:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);QUARRY_TRACE</DefineConstants>
</PropertyGroup>
```

Then add `.Trace()` to any builder chain before the execution terminal:

```csharp
var result = await db.Users()
    .Where(u => u.IsActive)
    .Trace()                    // <-- compile-time signal
    .Select(u => (u.UserId, u.UserName))
    .ExecuteFetchAllAsync();
```

Trace comments appear as `// [Trace]` lines inside the `#region` for that chain in the generated `.g.cs` interceptors file.

### Without QUARRY_TRACE

If `.Trace()` is present but `QUARRY_TRACE` is not defined, the generator emits **QRY034** warning:

```
QRY034: .Trace() found on chain at File.cs:42 but QUARRY_TRACE is not defined.
        Add <DefineConstants>QUARRY_TRACE</DefineConstants> to enable trace output.
```

No trace comments are emitted. Non-traced chains are completely unaffected regardless of `QUARRY_TRACE`.

### In generator tests

Pass `QUARRY_TRACE` via `CSharpParseOptions`:

```csharp
var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "QUARRY_TRACE" });
var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
```

See `UsageSiteDiscoveryTests.Trace_WithQuarryTraceSymbol_EmitsTraceComments` for a working example.

## 2. Architecture

### Pipeline stages and where trace data is generated

```
Stage 2 (Discovery)  ─┐
Stage 3 (Binding)     ├─ Per-site. No trace logging here (IsTraced unknown).
Stage 4 (Translation) ─┘

Stage 5 (ChainAnalyzer)  ← IsTraced detected. Retroactively reconstructs
                            per-site trace from TranslatedCallSite data.
                            Logs chain-level analysis (joins, terms, params).

Stage 5b (SqlAssembler)   ← Gated behind chain.IsTraced. Logs rendered SQL
                            per mask variant.

Stage 5c (CarrierAnalyzer) ← Gated behind chain.TraceLines != null. Logs
                             carrier eligibility and field counts.

Stage 6 (Emission)        ← QuarryGenerator collects TraceCapture data,
                            attaches to PrebuiltChainInfo.TraceLines.
                            FileEmitter writes // [Trace] comments.
```

### Key files

| File | Role |
|------|------|
| `IR/TraceCapture.cs` | `[ThreadStatic]` side-channel dictionary. `Log(uniqueId, message)` accumulates lines. `Get(uniqueId)` retrieves. `Clear()` resets per pass. |
| `Parsing/ChainAnalyzer.cs` | `LogSiteTrace()` reconstructs per-site discovery/binding/translation trace. `LogChainTrace()` logs chain-level plan details. Both gated behind `isTraced`. |
| `IR/SqlAssembler.cs` | Logs rendered SQL per mask, gated behind `chain.IsTraced`. |
| `CodeGen/CarrierAnalyzer.cs` | Logs carrier eligibility, gated behind `chain.TraceLines != null`. |
| `QuarryGenerator.cs` | `HasQuarryTrace(Compilation)` reads preprocessor symbol. Collects `TraceCapture.Get()` data and attaches to `PrebuiltChainInfo.TraceLines`. Reports QRY034. |
| `CodeGen/FileEmitter.cs` | Emits `chain.TraceLines` as `// [Trace]` comments inside the chain's `#region`. Skips `InterceptorKind.Trace` sites in `EmitInterceptorMethod`. |
| `Models/PrebuiltChainInfo.cs` | `TraceLines` property carries trace data per-chain. Excluded from `IEquatable`. |
| `Models/InterceptorKind.cs` | `Trace` enum value. Discovered via extension method receiver type resolution. |
| `Quarry/Query/TraceExtensions.cs` | 14 per-interface no-op extension methods. Runtime pass-through. |
| `Parsing/UsageSiteDiscovery.cs` | Extension method discovery: resolves `ReceiverType` for `.Trace()` calls. |

### Data flow

All trace data is keyed by the chain's **execution site UniqueId** (e.g., `ExecuteFetchAllAsync`'s UniqueId). `LogSiteTrace` and `LogChainTrace` write to this key. `QuarryGenerator` reads it via `TraceCapture.Get(execUid)` and attaches the list to `PrebuiltChainInfo.TraceLines`.

## 3. Reading Trace Output

Trace output is structured as `// [Trace] {Category} ({SiteName}):` headers followed by `//   key=value` detail lines.

### Categories (in output order)

**Per-site categories** (repeated for each clause site + execution site):

| Category | What it shows |
|----------|---------------|
| `Discovery` | How the site was found: `kind`, `builderKind`, `isAnalyzable`, `chainId`, `builderType`, `entityType`, `resultType`, parsed expression (rendered SQL), clause kind, conditional info, projection info, disqualifier flags |
| `Binding` | Entity resolution: `table`, `schema`, `dialect`, `context`, resolved column list, joined entities, insert/update info |
| `Translation` | Clause translation result: `clauseKind`, `isSuccess`, rendered SQL expression, per-parameter details (type, value expression, path, flags like captured/collection/enum), join metadata, set assignments, errors |

**Chain-level categories** (once per chain):

| Category | What it shows |
|----------|---------------|
| `ChainAnalysis` | `tier`, `queryKind`, primary table, `isDistinct`, joins (kind, table, rendered ON condition), WHERE terms (rendered condition, bit index), ORDER BY (expression, direction), GROUP BY / HAVING expressions, SET terms, projection (kind, columns with types and SQL), pagination, parameters (all flags), conditional terms, possible masks |
| `Assembly` | Rendered SQL string per mask variant, parameter count per variant |
| `Carrier` | Eligibility decision, base class, field/static field counts, ineligibility reason |

### Example trace (abbreviated)

For `db.Users().Where(u => u.IsActive).Trace().Select(u => (u.UserId, u.UserName)).ExecuteFetchAllAsync()`:

```csharp
    #region Chain: ExecuteFetchAllAsync at line 23

    // [Trace] Discovery (Where at line 19):
    //   kind=Where, builderKind=Query, isAnalyzable=True
    //   chainId=abc123, uniqueId=8988b5d8
    //   builderType=IQueryBuilder, entityType=TestApp.User
    //   parsedExpr="IsActive" = 1
    //   clauseKind=Where, isDescending=False
    //
    // [Trace] Binding (Where):
    //   entity=TestApp.User, table=users, schema=(null), dialect=SQLite
    //   context=TestDbContext
    //   resolvedColumns=[UserId, UserName, IsActive]
    //
    // [Trace] Translation (Where):
    //   clauseKind=Where, isSuccess=True
    //   resolvedExpr="IsActive" = 1
    //   params=none
    //
    // [Trace] Discovery (Select at line 21):
    //   kind=Select, builderKind=Query, isAnalyzable=True
    //   ...
    //
    // [Trace] ChainAnalysis:
    //   tier=PrebuiltDispatch, queryKind=Select
    //   primaryTable=users, schema=(null)
    //   where: "IsActive" = 1
    //   projection: kind=Tuple, resultType=(int, string), identity=False
    //     col: UserId -> UserId [int]
    //     col: UserName -> UserName [string]
    //   parameters=none
    //   possibleMasks=[0]
    //
    // [Trace] Assembly (mask=0):
    //   sql=SELECT "UserId", "UserName" FROM "users" WHERE "IsActive" = 1
    //   paramCount=0
    //
    // [Trace] Carrier:
    //   eligible=true
    //   baseClass=CarrierBase<User, (int, string)>
    //   fields=0, staticFields=1
```

### Diagnosing common issues

| Symptom | Where to look in trace |
|---------|----------------------|
| Wrong SQL output | `Assembly` — check rendered SQL per mask. Then `ChainAnalysis` WHERE/ORDER/JOIN terms for incorrect expressions. |
| Parameter binding mismatch | `Translation` — check per-parameter type, value, path, isCollection/isCaptured flags. Then `ChainAnalysis` parameters section for global index mapping. |
| Chain not optimized (RuntimeBuild) | `ChainAnalysis` — check `tier` and `notAnalyzableReason`. Then `Discovery` — check `isAnalyzable` and disqualifier flags per site. |
| Column resolution failure | `Binding` — check `resolvedColumns` list. Missing columns mean the entity schema doesn't expose them. |
| Join issues | `Discovery` — check `joinedEntityType`, `joinedEntityTypes`, `isNavigationJoin`. `ChainAnalysis` — check join entries with rendered ON conditions. |
| Conditional dispatch wrong | `Discovery` — check `conditional` info (depth, condition text, branch). `ChainAnalysis` — check `conditionalTerms` and `possibleMasks`. |
| Carrier ineligible | `Carrier` — check `reason`. Common: "chain parameters not resolved", "chain has unmatched methods", terminal-specific reasons. |
| Expression parse failure | `Discovery` — check `parsedExpr`. If missing, the lambda couldn't be parsed to SqlExpr. `Translation` — check `error` field. |

## 4. Extending the Trace System

### Adding trace data to an existing stage

All trace logging is in `ChainAnalyzer.LogSiteTrace()` (per-site) and `ChainAnalyzer.LogChainTrace()` (per-chain). These reconstruct trace data from the `TranslatedCallSite` and `QueryPlan` objects.

To add a new field to an existing category:

1. Find the category in `LogSiteTrace` or `LogChainTrace`
2. Add a `log(chainUid, ...)` call reading from the appropriate pipeline object
3. The data is available on `site.Bound.Raw` (discovery), `site.Bound` (binding), `site.Clause` (translation), or `plan` (chain analysis)

Example — adding a custom type mapping trace to Translation:

```csharp
// In LogSiteTrace, inside the Translation section:
if (site.Clause?.CustomTypeMappingClass != null)
    log(chainUid, $"  customTypeMapping={site.Clause.CustomTypeMappingClass}");
```

### Adding trace data to a new pipeline stage

If you add a new stage that processes chains after `ChainAnalyzer`:

1. Gate the logging behind `IsTraced` (on `AnalyzedChain`) or `TraceLines != null` (on `PrebuiltChainInfo`)
2. Log to `TraceCapture.Log(execUniqueId, message)` using the execution site's UniqueId as the key
3. Use the `[Trace] CategoryName:` / `  key=value` format for consistency
4. Ensure the logging happens **before** `QuarryGenerator` calls `TraceCapture.Get()` to collect the data (i.e., before the bridge conversion loop)

### Adding a new SqlExpr node type

When rendering expressions, `FormatExpr()` in `ChainAnalyzer` delegates to `SqlExprRenderer.Render()`. If you add a new `SqlExpr` subclass:

1. Add a case to `SqlExprRenderer.RenderExpr()` — this automatically makes it visible in trace output
2. No changes needed in the trace system itself

### Key constraints

- **Never log in Stages 2-4** (discovery, binder, translator). These run per-site before `IsTraced` is known. All per-site trace is retroactively reconstructed in `ChainAnalyzer.LogSiteTrace()` from the `TranslatedCallSite` data.
- **TraceCapture is ThreadStatic** — safe for concurrent generator runs but data doesn't cross threads.
- **TraceLines on PrebuiltChainInfo is excluded from IEquatable** — trace data doesn't affect incremental caching.
- **Use `FormatExpr()` for SqlExpr rendering** — wraps `SqlExprRenderer.Render()` with try/catch fallback to type name. Uses PostgreSQL dialect and generic param format for readability.
