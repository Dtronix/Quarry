# Plan: PatchUpdateBenchmarks

A single-phase implementation. The change is purely additive — one new benchmark file, no production code touched, no existing benchmarks modified. The Patch API it exercises is already shipped (PR #301, commit 8797127).

## Concepts

**Variable-column UPDATE.** The SET column list is decided at runtime by caller flags rather than at the call site. This is the use case that motivated the Patch API: the existing `Set(u => u.X = ...)` and `Set(new User { X = ... })` overloads both fix the column set at compile time and so can't express it without resorting to multiple `if`-guarded chains. Patch (lambda form) lets a single chain handle any subset.

**Flag-driven branches as the variability signal.** All 4 flags are set to `true` in `GlobalSetup`. Each benchmark iteration reads them and takes the same branch every time, but they're field reads (not consts) so the JIT cannot constant-fold them. This is the established pattern in `ConditionalBranchBenchmarks` — what we're measuring is the cost of "code shaped like a runtime-conditional patch," not the cost of actually varying the inputs.

**One-column vs. all-columns cardinalities.** Two named benchmarks per library. OneColumn is the lower bound (the framework overhead of Patch's mask-driven SET assembly when only one bit fires); AllColumns is where the comparison gets interesting (Quarry's amortized SQL template vs. StringBuilder/dict growth in the other libraries). Readers compare same-library OneColumn vs. AllColumns rows for scaling.

**Per-library idiom choices.** Each library uses the pattern its users would write in production, not a normalized "everyone uses StringBuilder" path:

- *Raw / Dapper / SqlKata, OneColumn:* hardcoded single-column UPDATE behind an early-return flag check. A developer with a fixed-1-column scenario wouldn't reach for StringBuilder.
- *Raw / Dapper, AllColumns:* `StringBuilder` + conditional `Append` + conditional parameter add, with a `first` flag to manage the comma separator. The textbook hand-rolled dynamic UPDATE.
- *SqlKata, AllColumns:* conditional `Dictionary<string, object>` build, then `AsUpdate(dict)`. The native SqlKata pattern.
- *EF Core, both:* `FirstAsync(u => u.UserId == _targetId)` → conditional property assignments → `SaveChangesAsync`. EF has no clean variable-update idiom — `ExecuteUpdate`'s `SetProperty` chain is fixed at compile time without hand-built `Expression<Func<SetPropertyCalls<T>, ...>>` trees, which nobody writes. The load-mutate-save pattern is the actual EF idiom. Trade-off: 2 round-trips per call (one SELECT, one UPDATE) — honest to real EF code but means EF rows will look worse than the others.
- *Quarry, both:* `Set((ref User.Patch p) => { if (flag) p.X = ...; ... })`. Same lambda shape in both cardinalities; only the number of `if` branches changes.

**Source-generator-bug workaround.** All fields read inside the Quarry Patch lambda are `private static`. This matches the comment in `UpdateBenchmarks.cs:19-21` — `UnsafeAccessor` emits `StaticField` for class-level fields used in lambdas, and instance fields hit a closure-capture bug. Instance fields are fine for non-Quarry benchmarks but using static everywhere keeps the call sites symmetric and avoids confusion.

## Layout

One new file: `src/Quarry.Benchmarks/Benchmarks/PatchUpdateBenchmarks.cs`. Inherits from `BenchmarkBase` (same as every other benchmark file). Class layout:

```
Fields:
  _iterationEfContext  : EfBenchContext  (recreated per iteration)
  _targetId            : static int      (=1, set in GlobalSetup)
  _setName/_setEmail/_setActive/_setLastLogin : static bool (all =true, set in GlobalSetup)
  NewName/NewEmail     : const string

Hooks:
  GlobalSetup          : base + assign fields
  IterationSetup       : new EfBenchContext
  IterationCleanup     : dispose EF context + reset row 1 to seed values
                         (UPDATE users SET UserName='User001', Email='user001@example.com',
                          IsActive=1, LastLogin=NULL WHERE UserId=1)

Benchmarks (10):
  Raw_OneColumn        [Baseline=true]
  Dapper_OneColumn
  EfCore_OneColumn
  Quarry_OneColumn
  SqlKata_OneColumn
  Raw_AllColumns
  Dapper_AllColumns
  EfCore_AllColumns
  Quarry_AllColumns
  SqlKata_AllColumns
```

The cleanup query needs the full reset because AllColumns benchmarks mutate Email, IsActive, and LastLogin in addition to UserName. Seed values come from `DatabaseSetup.cs:46-56` (i=1 → name "User001", email "user001@example.com", active=1, LastLogin=NULL).

## Algorithm sketches

**Raw_AllColumns** — illustrates the StringBuilder + comma-separator dance every library version of `AllColumns` mirrors:

```csharp
var sb = new StringBuilder("UPDATE users SET ");
await using var cmd = Connection.CreateCommand();
bool first = true;
if (_setName) { sb.Append("UserName = @name"); cmd.Parameters.AddWithValue("@name", NewName); first = false; }
if (_setEmail) { if (!first) sb.Append(", "); sb.Append("Email = @email"); cmd.Parameters.AddWithValue("@email", NewEmail); first = false; }
if (_setActive) { if (!first) sb.Append(", "); sb.Append("IsActive = @active"); cmd.Parameters.AddWithValue("@active", 1); first = false; }
if (_setLastLogin) { if (!first) sb.Append(", "); sb.Append("LastLogin = @last"); cmd.Parameters.AddWithValue("@last", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")); }
sb.Append(" WHERE UserId = @id");
cmd.Parameters.AddWithValue("@id", _targetId);
cmd.CommandText = sb.ToString();
return await cmd.ExecuteNonQueryAsync();
```

**Quarry_AllColumns** — the headline; same lambda shape regardless of how many branches fire:

```csharp
return await QuarryDb.Users()
    .Update()
    .Set((ref User.Patch p) =>
    {
        if (_setName)      p.UserName  = NewName;
        if (_setEmail)     p.Email     = NewEmail;
        if (_setActive)    p.IsActive  = true;
        if (_setLastLogin) p.LastLogin = DateTime.UtcNow;
    })
    .Where(u => u.UserId == _targetId)
    .ExecuteNonQueryAsync();
```

**EfCore_AllColumns** — idiomatic EF, 2 round-trips:

```csharp
// Comment: EF has no clean runtime-variable SetProperty story without
// hand-building an Expression tree. Load-mutate-save is the real EF idiom.
var user = await _iterationEfContext.Users.FirstAsync(u => u.UserId == _targetId);
if (_setName)      user.UserName  = NewName;
if (_setEmail)     user.Email     = NewEmail;
if (_setActive)    user.IsActive  = true;
if (_setLastLogin) user.LastLogin = DateTime.UtcNow;
return await _iterationEfContext.SaveChangesAsync();
```

**SqlKata_AllColumns** — native dictionary pattern:

```csharp
var values = new Dictionary<string, object>();
if (_setName)      values["UserName"]  = NewName;
if (_setEmail)     values["Email"]     = NewEmail;
if (_setActive)    values["IsActive"]  = 1;
if (_setLastLogin) values["LastLogin"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
var query = new Query("users").Where("UserId", _targetId).AsUpdate(values);
var compiled = SqlKataCompiler.Compile(query);
await using var cmd = Connection.CreateCommand();
cmd.CommandText = compiled.Sql;
foreach (var binding in compiled.Bindings)
    cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
return await cmd.ExecuteNonQueryAsync();
```

`_OneColumn` variants for Raw/Dapper/SqlKata skip the StringBuilder/dict — they early-return if `!_setName`, then issue a hardcoded single-column UPDATE. Quarry and EF run the same shape in both cardinalities, just with fewer `if`s.

## Phase 1 — Add PatchUpdateBenchmarks.cs (single phase)

1. Write `src/Quarry.Benchmarks/Benchmarks/PatchUpdateBenchmarks.cs` with the class described above. Match naming, namespace (`Quarry.Benchmarks.Benchmarks`), and using-pattern of `UpdateBenchmarks.cs`/`ConditionalBranchBenchmarks.cs`.
2. Build the benchmarks project. Must succeed with 0 errors. The Quarry source generator will emit interceptors for the new chain call sites.
3. Inspect the generated file `BenchDb.Interceptors.*PatchUpdateBenchmarks.g.cs` to confirm interceptors were emitted (proves the Patch lambda chain was recognized — failing this means the lambda shape wasn't matched by the analyzer, which would throw at runtime per `UpdateBuilderPatchExtensions.cs`).
4. Smoke-run a short benchmark configuration (e.g. `dotnet run -c Release -- --filter "*PatchUpdate*" --job dry`) to verify all 10 benchmarks execute without exceptions. A dry job runs each method a handful of times — cheap and proves the wiring works without the full statistical run.
5. Run the Patch-relevant test subset (`CrossDialectUpdateTests`, `PatchInfoTests`) to confirm we haven't accidentally invalidated any generator output. Adding a new call site can shift interceptor numbering — these tests must stay green.
6. Stage code + `_sessions/` together, commit.

## Tests

No new unit tests. Benchmark files don't have unit-test coverage in this repo (verified — `src/Quarry.Tests` has no `BenchmarkBase` references). Verification is:

- **Compile-time:** `dotnet build` must succeed. The Quarry source generator catching the Patch lambda chain is itself a correctness check — if it doesn't, the chain falls through to the throwing `UpdateBuilderPatchExtensions` extension method.
- **Generated-source inspection:** check that an interceptor file for `PatchUpdateBenchmarks` is emitted alongside the existing per-class interceptor files. Confirms the chain was recognized.
- **Dry run:** `--job dry` (a couple of invocations per benchmark, no statistical sampling) catches any runtime crashes — wrong column types, EF tracking conflicts, SqlKata dialect mismatches.
- **Regression:** existing Patch tests must still pass — adding a new call site changes interceptor IDs but the existing tests must still match their generated output.

## Dependencies

None. The Patch API is shipped (`commit 8797127`). All referenced libraries (`Dapper`, `Microsoft.EntityFrameworkCore`, `SqlKata`, `Microsoft.Data.Sqlite`) are already in `Quarry.Benchmarks.csproj` per the existing benchmark files.
