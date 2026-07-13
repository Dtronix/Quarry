## Summary
- Closes #308

Six runtime hot-path fixes from the 2026-07-07 multi-agent deep review — the remaining avoidable costs and latent defects on the emitted execution path (`CarrierEmitter` / `EntityCodeGenerator` / runtime internals). Each is small; they are bundled as the issue requested.

| # | Item | Fix |
|---|------|-----|
| 1 | **IN-list SQL cache collision** (high, correctness) | The multi-collection IN-list cache was validated by an XOR-of-scaled-lengths hash, which is not injective — length pairs `(16,900)` and `(85,41)` both hash to `-249261860`, so a false hit reused `ColParts` built for the wrong lengths and drove the bind loop out of range (`IndexOutOfRangeException`). Now validated by an exact per-collection `ColParts[i].Length` compare (hash kept as a cheap pre-filter). |
| 2 | **Per-row `NavigationList` alloc** (high, allocation) | `NavigationList<T>.Unloaded()` allocated `new()` per row per `Many<T>` navigation. Backed by a cached, deeply-immutable singleton; public API and generator emit unchanged. |
| 3 | **Unconditional `OpId.Next()`** | The single-row Insert terminal and the 5 `QuarryContext` raw-SQL paths called `Interlocked.Increment` even with logging disabled. Gated on `logger != null` (matching the query preamble / batch-insert terminal). |
| 4 | **Missing `ConfigureAwait(false)`** | Added to every await in `src/Quarry` (67 sites) and enforced going forward via a runtime-project-scoped `.editorconfig` enabling **CA2007 = error**. |
| 5 | **Per-row RawSql mapper alloc** | RawSql readers emitted `new {Mapper}().FromDb(...)` per row per mapped column. Now reference cached mapper fields (struct-local for the struct reader; file-scope for the lambda readers), reusing the existing `GetMappingFieldName` precedent. |
| 6 | **Six bundled nits** | (a) batch insert reuses an already-materialized list instead of `ToList`; (b) `ParameterNames` cache widened 256 → 2100; (c) dead `func.Target` read dropped when all clause captures are static; (d) shared empty enumerator for an unloaded `NavigationList`; (e) `First` materializes before logging completion; (f) documented the `PreparedQuery<T>` reinterpret-cast invariant. |

## Performance Considerations
The entire change set is performance-oriented and behavior-preserving except item 1 (a correctness fix): it removes per-row `NavigationList` and RawSql-mapper allocations on the entity-fetch/RawSql paths, eliminates `Interlocked` contention on inserts with logging disabled, widens the parameter-name cache to cover batch inserts (up to SQL Server's 2100-parameter ceiling), and skips a batch-insert collection copy when the caller already passed a list.

## Security Considerations
None. No change alters SQL text assembly or parameter binding in an unsafe direction; the IN-cache fix makes bind-length validation *stricter* by removing a stale-SQL reuse path.

## Breaking Changes
- **Consumer-facing:** `NavigationList<T>.Unloaded()` now returns a process-wide shared singleton rather than a fresh instance per call — an observable reference-identity change, safe because the type is `sealed` and the unloaded state is deeply immutable (no public mutators). Any consumer asserting per-call distinctness would need to adjust.
- **Internal:** `ConfigureAwait(false)` added throughout `src/Quarry`; the runtime project now fails its build on a bare await (CA2007 = error), scoped to `src/Quarry` only (tests/generator/benchmarks/samples unaffected). `PreparedQuery<T>` change is documentation-only.

## Review notes
A structured review found no High findings and no correctness or plan-compliance concerns; the two highest-risk edits (the IN-cache length compare and the `ConfigureAwait` `await using` disposal-ordering splits) were verified correct against source, including masked/conditional collections and reverse-declaration disposal order. One Medium (a missing failure-path test for item 6e) and one Low (`.editorconfig root = true` inheritance) were addressed; the remaining Lows were behavior-neutral coverage gaps, plan-authorized cosmetics, or by-design changes.

Every fix ships with a test that fails without it (teeth-verified for items 1, 3, 5, 6c, 6e; CA2007 = error is the standing guard for item 4).
