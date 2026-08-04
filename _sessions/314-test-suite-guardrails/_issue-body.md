## Description

`InsertBatch(...)` interceptors call `Quarry.Internal.BatchInsertSqlBuilder.Build(...)`, but that type
is `internal` to the `Quarry` assembly. Consumer code that uses `InsertBatch` therefore fails to
compile:

```
error CS0122: 'BatchInsertSqlBuilder' is inaccessible due to its protection level
```

in the generated `*.Interceptors.*.g.cs`.

This is invisible inside this repository because **every** project that exercises `InsertBatch` is on
Quarry's `InternalsVisibleTo` list — `Quarry.Tests`, `Quarry.Benchmarks`, `Quarry.Sample.WebApp`. An
external consumer has no such grant, so the feature does not compile for them at all.

Found by the interceptor-binding guard matrix added in #314: the two `InsertBatch` shapes compile in a
synthetic `CSharpCompilation` that is *not* a friend assembly, which is the only place in the repo
that models an ordinary consumer.

## Location

- Emission: `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs:518` and `:559` — both emit
  `Quarry.Internal.BatchInsertSqlBuilder.Build(...)` unconditionally.
- The type: `src/Quarry/Internal/BatchInsertSqlBuilder.cs:10` — `internal static class BatchInsertSqlBuilder`.
- Friend grants that mask it: `src/Quarry/Quarry.csproj:19-25`.

## Diagnostics

```
TestDbContext.Interceptors.Source1.g.cs(99,35): error CS0122:
    'BatchInsertSqlBuilder' is inaccessible due to its protection level
```

Reproduced by `InterceptorBindingGuardTests` for the shapes `BatchInsert_NonQuery` and
`BatchInsert_ScalarAsync`, which compile:

```csharp
var rows = new[] { new User { UserName = "a", IsActive = true } };
await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ExecuteNonQueryAsync();
await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ExecuteScalarAsync<int>();
```

in an assembly named `InterceptorBindingGuardAssembly` — i.e. not a friend of `Quarry`.

## What Has Been Tried

- Confirmed the reference is unconditional: both emission sites hard-code the fully qualified
  `Quarry.Internal.BatchInsertSqlBuilder`, with no public shim and no alternate path.
- Confirmed the in-repo blind spot: `grep` for `InsertBatch` outside `Quarry.Tests` finds
  `Quarry.Benchmarks` and `Quarry.Sample.WebApp`, both of which hold `InternalsVisibleTo` grants. So
  no existing build would ever have caught this.
- Not yet checked: whether any other generated call target is `internal`. The same audit should be run
  across `TerminalBodyEmitter`, `JoinBodyEmitter` and `CarrierEmitter` — this one was found by
  accident, and a second instance would fail the same way.

## Gathered Information

- The runtime surface a generated interceptor may reference is effectively part of Quarry's public
  API contract, even though it is emitted rather than hand-written. Nothing currently enforces that.
- `BatchInsertSqlBuilder.Build` expands a row template per entity (see `SqlAssembler.cs:890`), so it
  is genuinely runtime-needed, not a codegen-time helper that could be inlined away.

## Suggested Approach

1. **Make the emitted surface public.** Either promote `BatchInsertSqlBuilder` to `public` (it already
   lives under a `Quarry.Internal` namespace, which signals "don't call this" without breaking
   compilation), or add a thin `public` forwarder that the emitter targets instead.
2. **Then prevent recurrence.** Audit every type a generated interceptor can name and assert
   accessibility. The natural home is the guard matrix in
   `src/Quarry.Tests/Generation/InterceptorBindingGuardTests.cs`, which already compiles each shape in
   a non-friend assembly — it just needs the `InsertBatch` shapes returned to the clean-binding set
   once this is fixed.

The two shapes are currently pinned as `KnownBug_Issue{this}_BatchInsert_ReferencesInternalType`
in that fixture. **When that pin fails, this bug is fixed** — remove the pin and move the shapes back
into `GenericTerminalShapes`.
