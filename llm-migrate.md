# Migrate to Quarry — LLM Skill

Skill for migrating .NET data access code from Dapper, EF Core, SqlKata, and/or raw ADO.NET to Quarry. Produces a migration plan first, then executes conversion per-project.

Prerequisites: read `llm.md` for Quarry architecture and API reference. Target project must be .NET 10+.

## Phase 0: User Configuration

Prompt the user for all five questions before proceeding. Do not skip any.

### Q1: Source Libraries

Which SQL/ORM libraries are currently in use? (multi-select, one or more)

- Dapper
- EF Core
- SqlKata
- Raw ADO.NET (SqlCommand / DbCommand / DbDataReader)
- Auto-discover (scan the codebase — runs Phase 1.1–1.3 first, then confirms findings)

### Q2: System Scope

- Single project / single data access layer
- Multiple projects in one solution (may use different libraries per project)

If multiple: identify each project and its data access library independently. Run Phases 1–7 per-project, ordered by dependency (shared/core projects first).

### Q3: Dependency Injection

- Microsoft.Extensions.DependencyInjection (standard)
- No DI (direct instantiation)
- Custom DI container — ask user to describe: registration API, lifetime management, connection resolution pattern

### Q4: Migration System Integration

- Full — scaffold from existing DB + set up ongoing `quarry migrate` workflow
- Scaffold only — import schema, user handles migrations from docs
- None — skip migration setup

### Q5: ASP.NET Project?

- Yes — reference `Quarry.Sample.WebApp` patterns for DI, middleware, startup integration
- No

---

## Phase 1: Discovery

Goal: build a complete inventory of all database interaction code in the target project(s). Acquire every SQL execution site.

### 1.1 Package Detection

Scan all `.csproj` files for NuGet references:

```
Dapper:       Dapper, Dapper.Contrib, Dapper.SqlBuilder
EF Core:      Microsoft.EntityFrameworkCore
              Microsoft.EntityFrameworkCore.SqlServer
              Microsoft.EntityFrameworkCore.Sqlite
              Npgsql.EntityFrameworkCore.PostgreSQL
              Pomelo.EntityFrameworkCore.MySql
SqlKata:      SqlKata, SqlKata.Execution
ADO.NET:      Microsoft.Data.SqlClient
              Microsoft.Data.Sqlite
              Npgsql
              MySqlConnector
              MySql.Data
              System.Data.SqlClient
```

Record which projects reference which packages. Determine dialect from provider package:

| Provider Package | SqlDialect |
|---|---|
| `Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL` | `PostgreSQL` |
| `Microsoft.Data.SqlClient`, `EF SqlServer`, `System.Data.SqlClient` | `SqlServer` |
| `Microsoft.Data.Sqlite`, `EF Sqlite` | `SQLite` |
| `MySqlConnector`, `Pomelo.EntityFrameworkCore.MySql`, `MySql.Data` | `MySQL` |

### 1.2 Using Statement Scan

Search all `.cs` files for import statements:

```
Dapper:       using Dapper;
EF Core:      using Microsoft.EntityFrameworkCore;
SqlKata:      using SqlKata;
              using SqlKata.Execution;
ADO.NET:      using System.Data;
              using System.Data.Common;
              using Microsoft.Data.SqlClient;
              using Microsoft.Data.Sqlite;
              using Npgsql;
              using MySqlConnector;
```

### 1.3 API Pattern Scan

Grep for known call patterns. Record file path, line number, and containing method for each match.

**Dapper:**
```
\.QueryAsync<
\.Query<
\.QueryFirstAsync<
\.QueryFirstOrDefaultAsync<
\.QuerySingleAsync<
\.QuerySingleOrDefaultAsync<
\.ExecuteAsync\(
\.ExecuteScalarAsync<
\.QueryMultipleAsync\(
```

**EF Core:**
```
\.ToListAsync\(
\.ToArrayAsync\(
\.FirstOrDefaultAsync\(
\.FirstAsync\(
\.SingleOrDefaultAsync\(
\.SingleAsync\(
\.CountAsync\(
\.AnyAsync\(
\.AllAsync\(
\.SaveChangesAsync\(
\.SaveChanges\(
DbSet<
\.Include\(
\.ThenInclude\(
\.FromSqlRaw\(
\.FromSqlInterpolated\(
\.ExecuteSqlRawAsync\(
\.ExecuteSqlInterpolatedAsync\(
```

**SqlKata:**
```
new Query\(
\.From\(
QueryFactory
\.Get<
\.GetAsync<
\.First<
\.FirstAsync<
\.FirstOrDefault<
\.Paginate<
\.PaginateAsync<
```

**Raw ADO.NET:**
```
new SqlCommand\(
new SqliteCommand\(
new NpgsqlCommand\(
new MySqlCommand\(
\.CreateCommand\(
\.ExecuteReaderAsync\(
\.ExecuteReader\(
\.ExecuteNonQueryAsync\(
\.ExecuteNonQuery\(
\.ExecuteScalarAsync\(
\.ExecuteScalar\(
DbCommand
DbDataReader
SqlDataReader
SqliteDataReader
NpgsqlDataReader
```

### 1.4 Implicit Modification Analysis

Standard query/modification calls (1.3) are explicit — the developer wrote the SQL or the method name reveals the operation. EF Core also performs implicit modifications through change tracking, navigation manipulation, and third-party bulk libraries. These must be traced to determine what SQL they actually produce.

#### 1.4.1 EF Core Change Tracking Flow Trace

For every `SaveChangesAsync()` / `SaveChanges()` call site, trace backward through the method (and callers if needed) to determine the effective SQL operations:

**Entity state transitions to detect:**
```
context.Add(entity)           → INSERT (all non-computed columns)
context.AddRange(entities)    → INSERT per entity
context.Update(entity)        → UPDATE (all columns)
context.Remove(entity)        → DELETE
context.Attach(entity)        → no SQL until property mutation
dbSet.Add(entity)             → INSERT
dbSet.Remove(entity)          → DELETE
entity.State = EntityState.Modified  → UPDATE (all columns)
entity.State = EntityState.Added     → INSERT
entity.State = EntityState.Deleted   → DELETE
```

**Implicit update detection (load-mutate-save pattern):**
1. Find where entities are loaded (via `FirstAsync`, `FindAsync`, `ToListAsync`, etc.)
2. Trace property assignments between load and `SaveChangesAsync()`
3. Record which properties are mutated — these become the UPDATE SET columns
4. Record the identity/key used — this becomes the WHERE clause

Example trace:
```
UserRepository.cs:45   var user = await ctx.Users.FirstAsync(u => u.Id == id);
UserRepository.cs:46   user.Name = newName;
UserRepository.cs:47   user.Email = newEmail;
UserRepository.cs:48   await ctx.SaveChangesAsync();
  → Effective: UPDATE users SET name = @p0, email = @p1 WHERE id = @p2
  → Quarry:    db.Users().Update().Set(u => { u.Name = newName; u.Email = newEmail; }).Where(u => u.UserId == id).ExecuteNonQueryAsync()
```

**Multi-entity SaveChanges:** A single `SaveChangesAsync()` may flush multiple tracked changes across different entity types. Trace ALL entity state changes between the last `SaveChangesAsync()` (or context creation) and the current one. Each becomes a separate Quarry operation.

**Conditional mutations:** If property assignments are behind `if` statements or method calls, record the condition — these may need conditional Quarry chains or separate code paths.

#### 1.4.2 Navigation Property Cascade Trace

Navigation property manipulation triggers implicit SQL through EF Core's change tracker. Detect and trace these patterns:

```
// Collection add → INSERT of child + FK set
parent.Orders.Add(new Order { ... })
  → INSERT INTO orders (..., parent_id) VALUES (..., @parentId)
  → Quarry: db.Orders().Insert(new Order { ..., UserId = parentId }).ExecuteNonQueryAsync()

// Collection remove → DELETE of child (or FK null if optional)
parent.Orders.Remove(order)
  → DELETE FROM orders WHERE id = @orderId
    OR UPDATE orders SET parent_id = NULL WHERE id = @orderId
  → Quarry: db.Orders().Delete().Where(o => o.OrderId == orderId).ExecuteNonQueryAsync()
    OR db.Orders().Update().Set(o => o.UserId = null).Where(o => o.OrderId == orderId).ExecuteNonQueryAsync()

// Reference reassignment → UPDATE FK column
order.User = newUser
  → UPDATE orders SET user_id = @newUserId WHERE id = @orderId
  → Quarry: db.Orders().Update().Set(o => o.UserId = newUserId).Where(o => o.OrderId == orderId).ExecuteNonQueryAsync()

// Collection clear → DELETE or FK null for all children
parent.Orders.Clear()
  → DELETE FROM orders WHERE parent_id = @parentId
    OR UPDATE orders SET parent_id = NULL WHERE parent_id = @parentId
  → Quarry: db.Orders().Delete().Where(o => o.UserId == parentId).ExecuteNonQueryAsync()

// Reference set to null → UPDATE FK to NULL
order.User = null
  → UPDATE orders SET user_id = NULL WHERE id = @orderId
  → Quarry: db.Orders().Update().Set(o => o.UserId = null).Where(o => o.OrderId == orderId).ExecuteNonQueryAsync()
```

Check `OnModelCreating` for cascade delete behavior (`DeleteBehavior.Cascade`, `SetNull`, `Restrict`) — this determines whether removes are DELETE or UPDATE NULL. Record the cascade behavior per relationship.

#### 1.4.3 Bulk Operation Library Detection

Scan `.csproj` for third-party bulk operation packages:

```
EFCore.BulkExtensions:      <PackageReference Include="EFCore.BulkExtensions" />
Zack.EFCore.Batch:          <PackageReference Include="Zack.EFCore.Batch" />
EF Extensions (Z.Entity):   <PackageReference Include="Z.EntityFramework.Extensions.EFCore" />
linq2db.EntityFrameworkCore: <PackageReference Include="linq2db.EntityFrameworkCore" />
```

Grep for their API calls:

```
// EFCore.BulkExtensions
\.BulkInsert\(
\.BulkInsertAsync\(
\.BulkUpdate\(
\.BulkUpdateAsync\(
\.BulkDelete\(
\.BulkDeleteAsync\(
\.BulkInsertOrUpdate\(
\.BulkInsertOrUpdateAsync\(

// Zack.EFCore.Batch
\.BatchUpdate\(
\.BatchDelete\(

// Z.EntityFramework.Extensions
\.BulkSaveChanges\(
\.BulkMerge\(
```

Map to Quarry equivalents:

| Bulk Library Call | Quarry Equivalent |
|---|---|
| `BulkInsert(entities)` | `db.Users().InsertBatch(u => (u.Col1, u.Col2)).Values(entities).ExecuteNonQueryAsync()` |
| `BulkUpdate(entities)` | Loop: `db.Users().Update().Set(entity).Where(u => u.Id == entity.Id).ExecuteNonQueryAsync()` per entity, or `db.RawSqlNonQueryAsync()` for true bulk |
| `BulkDelete(entities)` | `db.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync()` |
| `BulkInsertOrUpdate` / `BulkMerge` | `db.RawSqlNonQueryAsync()` with MERGE/UPSERT SQL |
| `BatchUpdate(predicate, setter)` | `db.Users().Update().Set(u => ...).Where(u => ...).ExecuteNonQueryAsync()` |
| `BatchDelete(predicate)` | `db.Users().Delete().Where(u => ...).ExecuteNonQueryAsync()` |

Note: Quarry's `InsertBatch` has a 2100-parameter guard. For very large batch inserts, chunk the collection.

### 1.5 Entity / Model Inventory

Collect all data model classes used with the detected libraries.

**EF Core:** Find all `DbContext` subclasses. Extract `DbSet<T>` properties → entity types. Find entity configurations (`OnModelCreating` Fluent API, `IEntityTypeConfiguration<T>` implementations). Record per entity:
- Table name (`[Table]` attribute or `ToTable()` or convention)
- Column mappings (`[Column]`, `HasColumnName()`, `[MaxLength]`, `HasMaxLength()`)
- Keys (`[Key]`, `HasKey()`, `[DatabaseGenerated]`)
- Relationships (`HasOne`/`HasMany`/`WithMany`/`WithOne`, `[ForeignKey]`, navigation properties)
- Indexes (`HasIndex()`, `[Index]`)
- Value conversions (`HasConversion()`, `ValueConverter<,>`)
- Precision, computed columns, defaults, required/optional

**Dapper:** Find all POCO classes used as type parameters in `Query<T>`, `QueryAsync<T>`. Plain classes with public properties matching column names. No metadata — schema must come from DDL or scaffold.

**SqlKata:** Find result types from `.Get<T>()`, `.First<T>()`, `.Paginate<T>()`.

**Raw ADO.NET:** Find classes populated from `DbDataReader`. Trace reader access patterns (`reader["col"]`, `reader.GetString(n)`, `reader.GetInt32(n)`).

### 1.6 DI Registration Scan

If Q3 indicates DI is used, locate:

- Service registration entry point (`IServiceCollection` extensions, `Startup.ConfigureServices`, `Program.cs` builder)
- Connection string source (`IConfiguration.GetConnectionString()`, `appsettings.json` keys)
- `DbContext` registration (`AddDbContext<T>`, `AddDbContextFactory<T>`, `AddDbContextPool<T>`)
- Connection factory registration (`AddScoped<IDbConnection>`, custom factory classes)
- Repository pattern registrations (`AddScoped<IUserRepository, UserRepository>`)
- Lifetimes (Scoped, Transient, Singleton) for all data access registrations

### 1.7 Discovery Output

Produce a structured inventory per project. Present to user for confirmation before Phase 2.

```
Project: MyApp.Data
  Dialect: PostgreSQL (from Npgsql.EntityFrameworkCore.PostgreSQL)
  Libraries: EF Core (primary), Dapper (5 sites in ReportService)
  DI: Microsoft.Extensions.DependencyInjection
    - ApplicationDbContext: Scoped (AddDbContext)
    - IDbConnection: Scoped (NpgsqlConnection factory)

  Entities (12):
    - User        table: users       PK: UserId (identity)  FK: none           Indexes: IX_Email (unique)
    - Order       table: orders      PK: OrderId (identity)  FK: UserId→users   Indexes: none
    - OrderItem   table: order_items PK: ItemId (identity)   FK: OrderId→orders Indexes: none
    ...

  Query Sites (14):
    EF Core (9):
      UserRepository.cs:23    — Where + ToListAsync
      UserRepository.cs:45    — Include + FirstAsync
      OrderRepository.cs:12   — GroupBy + Select + ToListAsync
      ...
    Dapper (5):
      ReportService.cs:12     — QueryAsync<ReportDto> (complex join SQL)
      ReportService.cs:34     — ExecuteScalarAsync<int>
      ...

  Modification Sites — Explicit (6):
    EF Core SaveChanges (4):
      UserRepository.cs:67    — Add + SaveChangesAsync
      UserRepository.cs:89    — Update + SaveChangesAsync
      ...
    Dapper Execute (2):
      BulkService.cs:15       — ExecuteAsync (batch insert)
      ...

  Modification Sites — Implicit (5):
    Change Tracking (load-mutate-save):
      UserService.cs:34       — Load User → mutate Name, Email → SaveChangesAsync
                                 Effective: UPDATE users SET name, email WHERE id
      OrderService.cs:55      — Load Order → mutate Status → SaveChangesAsync
                                 Effective: UPDATE orders SET status WHERE id
    Navigation Cascades:
      OrderService.cs:78      — parent.Orders.Add(new Order{...}) → SaveChangesAsync
                                 Effective: INSERT INTO orders (..., user_id)
      UserService.cs:92       — parent.Orders.Remove(order) → SaveChangesAsync
                                 Effective: DELETE FROM orders WHERE id (cascade: Cascade)
    Bulk Libraries:
      BulkService.cs:30       — BulkInsert<User>(entities) [EFCore.BulkExtensions]
                                 Effective: batch INSERT users (all columns)
```

---

## Phase 2: Migration Plan

Generate a per-project plan. Present to user for approval before executing any changes.

### 2.1 Conversion Complexity Rating

Rate each query/modification site:

- **Direct** — 1:1 mapping exists, mechanical conversion
- **Adapted** — Quarry equivalent exists but syntax/pattern differs (e.g., EF `Include` → Quarry Join, change tracking → explicit Update)
- **RawSql** — Complex SQL best kept as `db.RawSqlAsync<T>(...)` initially; convert to typed chains later if desired
- **Redesign** — Pattern requires architectural change (e.g., cross-method IQueryable composition, Unit of Work, >4-table joins)

### 2.2 Plan Structure

```
Migration Plan: MyApp.Data
═══════════════════════════

1. NuGet Changes
   Remove: Microsoft.EntityFrameworkCore.*, Dapper
   Add:    Quarry, Quarry.Generator, Quarry.Analyzers

2. .csproj Configuration
   Add: <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>

3. Schema Classes (new files)
   Schemas/UserSchema.cs       ← from User entity + EF Fluent API config
   Schemas/OrderSchema.cs      ← from Order entity + EF Fluent API config
   Schemas/OrderItemSchema.cs  ← from OrderItem entity

4. Context Definition
   AppDb.cs ← replaces ApplicationDbContext
   Dialect: PostgreSQL

5. DI Registration Changes
   Remove: builder.Services.AddDbContext<ApplicationDbContext>(...)
   Remove: builder.Services.AddScoped<IDbConnection>(...)
   Add:    builder.Services.AddScoped(sp => new AppDb(new NpgsqlConnection(connectionString)))

6. Query Conversions (14 sites, rated)
   [Direct]   UserRepository.cs:23  — .Where().ToListAsync() → .Where().Select().ExecuteFetchAllAsync()
   [Adapted]  UserRepository.cs:45  — .Include().FirstAsync() → .Join().Select().ExecuteFetchFirstAsync()
   [RawSql]   ReportService.cs:12   — complex 5-table join → db.RawSqlAsync<ReportDto>(sql)
   [Direct]   ReportService.cs:34   — scalar count → .Select(Sql.Count()).ExecuteScalarAsync<int>()
   ...

7. Modification Conversions — Explicit (6 sites, rated)
   [Adapted]  UserRepository.cs:67  — Add+SaveChanges → .Insert().ExecuteNonQueryAsync()
   [Adapted]  UserRepository.cs:89  — entity update+SaveChanges → .Update().Set().Where().ExecuteNonQueryAsync()
   ...

8. Modification Conversions — Implicit (5 sites, rated)
   [Adapted]  UserService.cs:34     — load+mutate Name,Email+save → .Update().Set(u => { u.Name=; u.Email=; }).Where().ExecuteNonQueryAsync()
   [Adapted]  OrderService.cs:55    — load+mutate Status+save → .Update().Set(o => o.Status=).Where().ExecuteNonQueryAsync()
   [Adapted]  OrderService.cs:78    — nav.Add(child)+save → .Insert(new Order { ..., UserId = }).ExecuteNonQueryAsync()
   [Adapted]  UserService.cs:92     — nav.Remove(child)+save [Cascade] → .Delete().Where().ExecuteNonQueryAsync()
   [Direct]   BulkService.cs:30     — BulkInsert → .InsertBatch(selector).Values(entities).ExecuteNonQueryAsync()

9. Migration System Setup
   Install:  dotnet tool install --global Quarry.Tool
   Scaffold: quarry scaffold -d PostgreSQL -c "..." -o ./Schemas
   Initial:  quarry migrate add InitialCreate --project ... --context AppDb

10. Deletions (after verification)
    Remove: ApplicationDbContext.cs
    Remove: EntityConfigurations/ folder
    Remove: EF Migrations/ folder
    Remove: Dapper extension/helper classes
    Remove: Bulk operation library packages (EFCore.BulkExtensions, etc.)
    Remove: Old entity classes (replaced by generated entities)
```

Present plan. Wait for user approval before executing.

---

## Phase 3: Schema Conversion

### 3.1 Preferred Approach — Scaffold

For all source libraries, prefer `quarry scaffold` from the existing database when database access is available:

```sh
dotnet tool install --global Quarry.Tool
quarry scaffold --dialect PostgreSQL --connection "Host=...;Database=..." --output ./Schemas --namespace MyApp.Data.Schemas
```

Review generated schemas and adjust:
- Add `Sensitive()` to password/token/secret columns
- Add `Many<T>` navigations not auto-detected
- Verify naming style matches codebase convention
- Add custom type mappings (`Mapped<TMapping>()`) for value objects

If database access is unavailable, use the manual mapping rules below.

### 3.2 From EF Core Entities (Manual)

| EF Core | Quarry Schema |
|---|---|
| `public int Id { get; set; }` + `[Key]` | `public Key<int> Id => Identity();` |
| `[Key, DatabaseGenerated(None)]` | `public Key<int> Id => ClientGenerated();` |
| `public Guid Id { get; set; }` + `[Key]` | `public Key<Guid> Id => ClientGenerated();` |
| `[MaxLength(100)]` or `.HasMaxLength(100)` | `public Col<string> Name => Length(100);` |
| nullable property `string?` | `public Col<string?> Email { get; }` |
| `.HasDefaultValue(true)` | `public Col<bool> IsActive => Default(true);` |
| `.HasDefaultValueSql("GETUTCDATE()")` | `public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);` |
| `.HasPrecision(18, 2)` | `public Col<decimal> Total => Precision(18, 2);` |
| enum property | `public Col<MyEnum> Status { get; }` |
| `[ForeignKey]` / `.HasForeignKey()` | `public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();` |
| `ICollection<Order>` navigation | `public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);` |
| `[Table("my_table")]` / `.ToTable("my_table")` | `public static string Table => "my_table";` |
| `.HasIndex(x => x.Email).IsUnique()` | `public Index IX_Email => Index(Email).Unique();` |
| `[Column("user_name")]` / `.HasColumnName()` | `public Col<string> UserName => MapTo("user_name");` |
| `.HasKey(x => new { x.A, x.B })` | `public CompositeKey PK => PrimaryKey(A, B);` |
| `.HasConversion<MoneyConverter>()` | `public Col<Money> Balance => Mapped<Money, MoneyMapping>();` |
| `[Computed]` / `.HasComputedColumnSql()` | `public Col<string> FullName => Computed();` |

Table name: use `[Table]` value, `ToTable()` value, or EF convention. If snake_case naming used throughout, set `protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;`.

EF value converters → Quarry `TypeMapping<TCustom, TDb>`:
```csharp
// EF:
builder.Property(x => x.Money).HasConversion(v => v.Amount, v => new Money(v));
// Quarry:
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}
// Schema: public Col<Money> Balance => Mapped<Money, MoneyMapping>();
```

### 3.3 From Dapper POCOs (Manual)

No metadata on Dapper POCOs. Infer from SQL DDL or naming convention:

- `int Id` (first property, ends in "Id") → `Key<int> Id => Identity();`
- `string PropertyName` → `Col<string> PropertyName { get; }` (add `Length()` from DDL)
- `int? NullableProperty` → `Col<int?> NullableProperty { get; }`
- `int UserId` (where `User` table exists) → `Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();`

### 3.4 From SqlKata (Manual)

No entity model. Infer from query usage:
- `new Query("table_name")` → table name
- `.Select("col1", "col2")` → column names
- `.Where("col", value)` → column + type from value
- Result types `.Get<T>()` → same as Dapper POCO rules

### 3.5 From Raw ADO.NET (Manual)

Infer from SQL strings and reader calls:
- SQL strings in command constructors → table/column names
- `reader.GetString(n)`, `reader.GetInt32(n)` → column types
- `reader["column_name"]` → column names
- `cmd.Parameters.AddWithValue("@name", value)` → parameter types

---

## Phase 4: Context Setup

### 4.1 Create QuarryContext

```csharp
[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    // One method per schema/entity
}
```

One `partial IEntityAccessor<TEntity>` method per entity. Entity names come from generated entity classes (not schema classes). Entity accessors are methods (not properties) — `db.Users()` not `db.Users`.

### 4.2 .csproj Changes

Remove old packages. Add Quarry packages and interceptor namespace:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Quarry" Version="*" />
  <PackageReference Include="Quarry.Generator" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <PackageReference Include="Quarry.Analyzers" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The namespace in `InterceptorsNamespaces` must match the namespace of the `QuarryContext` class. If the context has no namespace, use `Quarry.Generated`.

### 4.3 DI Registration — Microsoft.Extensions.DependencyInjection

**Replacing EF Core:**
```csharp
// REMOVE:
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ADD:
var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddScoped(_ => new AppDb(new NpgsqlConnection(connectionString)));
```

**Replacing Dapper connection factory:**
```csharp
// REMOVE:
builder.Services.AddScoped<IDbConnection>(_ =>
    new NpgsqlConnection(connectionString));

// ADD:
builder.Services.AddScoped(_ => new AppDb(new NpgsqlConnection(connectionString)));
```

**Replacing both (mixed codebase):** Single `AppDb` registration replaces both. All data access goes through `AppDb`.

Key points:
- `QuarryContext` is `IAsyncDisposable`. Scoped lifetime handles disposal automatically.
- Connection passed to constructor. QuarryContext uses the connection, does not own it.
- Repository classes that took `IDbConnection` or `DbContext` now take `AppDb`.

**ASP.NET project** (reference `Quarry.Sample.WebApp`):
```csharp
// Program.cs
var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddScoped(_ => new AppDb(new SqliteConnection(connectionString)));
```

Inject into services/controllers:
```csharp
public class UserService(AppDb db)
{
    public async Task<List<User>> GetActiveUsers() =>
        await db.Users()
            .Where(u => u.IsActive)
            .Select(u => u)
            .ExecuteFetchAllAsync();
}
```

### 4.4 DI Registration — No DI

```csharp
await using var connection = new NpgsqlConnection(connectionString);
await using var db = new AppDb(connection);
```

### 4.5 DI Registration — Custom Container

Apply the same pattern: register `AppDb` with scoped/per-request lifetime, pass a new connection to the constructor. Adapt syntax to the container's registration API. If the user described a custom system in Q3, match its patterns.

---

## Phase 5: Query Migration

Convert every query/modification site identified in Phase 1. Apply the complexity rating from Phase 2 to determine approach.

### 5.1 Dapper → Quarry

| Dapper | Quarry |
|---|---|
| `conn.QueryAsync<User>("SELECT * FROM users WHERE active = @a", new { a = true })` | `db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync()` |
| `conn.QueryAsync<User>("SELECT * FROM users WHERE id = @id", new { id })` | `db.Users().Where(u => u.UserId == id).Select(u => u).ExecuteFetchAllAsync()` |
| `conn.QueryFirstAsync<User>(...)` | `db.Users().Where(...).Select(u => u).ExecuteFetchFirstAsync()` |
| `conn.QueryFirstOrDefaultAsync<User>(...)` | `db.Users().Where(...).Select(u => u).ExecuteFetchFirstOrDefaultAsync()` |
| `conn.QuerySingleAsync<User>(...)` | `db.Users().Where(...).Select(u => u).ExecuteFetchSingleAsync()` |
| `conn.QueryAsync<UserDto>("SELECT name, email FROM users")` | `db.Users().Select(u => new UserDto { Name = u.UserName, Email = u.Email }).ExecuteFetchAllAsync()` |
| `conn.QueryAsync<int>("SELECT id FROM users")` | `db.Users().Select(u => u.UserId).ExecuteFetchAllAsync()` |
| `conn.ExecuteAsync("INSERT INTO users (name) VALUES (@n)", new { n = name })` | `db.Users().Insert(new User { UserName = name }).ExecuteNonQueryAsync()` |
| `conn.ExecuteScalarAsync<int>("INSERT ... RETURNING id", ...)` | `db.Users().Insert(new User { UserName = name }).ExecuteScalarAsync<int>()` |
| `conn.ExecuteAsync("UPDATE users SET name = @n WHERE id = @id", ...)` | `db.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == id).ExecuteNonQueryAsync()` |
| `conn.ExecuteAsync("DELETE FROM users WHERE id = @id", ...)` | `db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync()` |
| `conn.QueryAsync<T>(complexSql, params)` | `db.RawSqlAsync<T>(complexSql, params)` |

**DynamicParameters / conditional SQL string building:**

Dapper pattern:
```csharp
var sql = "SELECT * FROM users WHERE 1=1";
var p = new DynamicParameters();
if (name != null) { sql += " AND name = @name"; p.Add("name", name); }
if (active.HasValue) { sql += " AND active = @active"; p.Add("active", active.Value); }
```

Quarry — conditional branches (up to 8 bits, 256 pre-built SQL variants):
```csharp
db.Users()
    .Where(u => name != null, u => u.UserName == name)
    .Where(u => active.HasValue, u => u.IsActive == active.Value)
    .Select(u => u)
    .ExecuteFetchAllAsync();
```

### 5.2 EF Core → Quarry

| EF Core | Quarry |
|---|---|
| `ctx.Users.ToListAsync()` | `db.Users().Select(u => u).ExecuteFetchAllAsync()` |
| `ctx.Users.Where(u => u.IsActive).ToListAsync()` | `db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync()` |
| `ctx.Users.FirstOrDefaultAsync(u => u.Id == id)` | `db.Users().Where(u => u.UserId == id).Select(u => u).ExecuteFetchFirstOrDefaultAsync()` |
| `ctx.Users.SingleAsync(u => u.Id == id)` | `db.Users().Where(u => u.UserId == id).Select(u => u).ExecuteFetchSingleAsync()` |
| `ctx.Users.CountAsync()` | `db.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>()` |
| `ctx.Users.AnyAsync(u => u.IsActive)` | `db.Users().Where(u => u.IsActive).Select(u => Sql.Count()).ExecuteScalarAsync<int>() > 0` |
| `ctx.Users.OrderBy(u => u.Name).Take(10).Skip(20).ToListAsync()` | `db.Users().Select(u => u).OrderBy(u => u.UserName).Limit(10).Offset(20).ExecuteFetchAllAsync()` |
| `ctx.Users.OrderByDescending(u => u.Name)` | `db.Users().Select(u => u).OrderByDescending(u => u.UserName)` |
| `ctx.Users.Select(u => new { u.Name }).ToListAsync()` | `db.Users().Select(u => new NameDto { Name = u.UserName }).ExecuteFetchAllAsync()` — NO anonymous types, use DTO or tuple |
| `ctx.Users.Select(u => new { u.Name, u.Email }).ToListAsync()` | `db.Users().Select(u => (u.UserName, u.Email)).ExecuteFetchAllAsync()` — tuple alternative |
| `ctx.Users.Include(u => u.Orders).FirstAsync(...)` | `db.Users().Join(u => u.Orders).Select((u, o) => ...).ExecuteFetchAllAsync()` or separate queries |
| `ctx.Users.GroupBy(u => u.Status).Select(g => new { g.Key, Count = g.Count() })` | `db.Users().GroupBy(u => u.Status).Select(u => (u.Status, Sql.Count())).ExecuteFetchAllAsync()` |
| `ctx.Users.Where(u => ids.Contains(u.Id)).ToListAsync()` | `db.Users().Where(u => ids.Contains(u.UserId)).Select(u => u).ExecuteFetchAllAsync()` |
| `ctx.Users.Where(u => u.Name.Contains("x")).ToListAsync()` | `db.Users().Where(u => u.UserName.Contains("x")).Select(u => u).ExecuteFetchAllAsync()` |
| `ctx.Users.Distinct().ToListAsync()` | `db.Users().Select(u => u).Distinct().ExecuteFetchAllAsync()` |
| `ctx.Users.AsNoTracking().ToListAsync()` | `db.Users().Select(u => u).ExecuteFetchAllAsync()` — Quarry never tracks; drop AsNoTracking |

**EF patterns requiring adaptation:**

| EF Core Pattern | Quarry Approach |
|---|---|
| Change tracking + `SaveChangesAsync()` | Explicit `.Insert()` / `.Update().Set()` / `.Delete()` per operation |
| `Include()`/`ThenInclude()` eager loading | `.Join()` (max 4 tables) or separate queries |
| Lazy loading | Not supported — fetch needed data explicitly |
| `IQueryable` composition across methods | Chain must stay in single method scope (compile-time analysis) |
| Transactions via `DbContext` | Use `DbTransaction` on the connection directly |
| `FromSqlRaw()` + LINQ chaining | `db.RawSqlAsync<T>(sql, params)` — no further chaining |
| Global query filters | Add `.Where()` to every relevant query or encapsulate in a helper that returns the full result |
| Owned entities / value objects | Flatten into parent schema, or separate schema + `TypeMapping<,>` |
| `SaveChangesAsync()` with multiple tracked changes | Split into individual Insert/Update/Delete calls |
| `ExecuteSqlRawAsync()` | `db.RawSqlNonQueryAsync(sql, params)` |
| `FromSqlInterpolated()` | `db.RawSqlAsync<T>(sql, params)` with explicit parameter extraction |

**EF implicit modification conversions (from Phase 1.4 trace results):**

Load-mutate-save → explicit Update with traced columns:
```csharp
// EF (implicit — change tracker detects Name and Email mutations):
var user = await ctx.Users.FirstAsync(u => u.Id == id);
user.Name = newName;
user.Email = newEmail;
await ctx.SaveChangesAsync();

// Quarry (explicit):
await db.Users().Update()
    .Set(u => { u.UserName = newName; u.Email = newEmail; })
    .Where(u => u.UserId == id)
    .ExecuteNonQueryAsync();
```

Navigation collection add → explicit Insert with FK:
```csharp
// EF (implicit — change tracker inserts child with FK):
var parent = await ctx.Users.Include(u => u.Orders).FirstAsync(u => u.Id == id);
parent.Orders.Add(new Order { Total = 100m });
await ctx.SaveChangesAsync();

// Quarry (explicit):
await db.Orders().Insert(new Order { UserId = id, Total = 100m }).ExecuteNonQueryAsync();
```

Navigation collection remove → explicit Delete or FK null (check cascade behavior):
```csharp
// EF (implicit — cascade behavior determines DELETE vs SET NULL):
parent.Orders.Remove(order);
await ctx.SaveChangesAsync();

// Quarry — if DeleteBehavior.Cascade:
await db.Orders().Delete().Where(o => o.OrderId == orderId).ExecuteNonQueryAsync();
// Quarry — if DeleteBehavior.SetNull:
await db.Orders().Update().Set(o => o.UserId = null).Where(o => o.OrderId == orderId).ExecuteNonQueryAsync();
```

Multi-entity SaveChanges → split into individual operations:
```csharp
// EF (implicit — single SaveChanges flushes all tracked changes):
ctx.Users.Add(newUser);
existingOrder.Status = "shipped";
ctx.Orders.Remove(cancelledOrder);
await ctx.SaveChangesAsync();

// Quarry (explicit — one call per operation):
await db.Users().Insert(newUser).ExecuteNonQueryAsync();
await db.Orders().Update().Set(o => o.Status = "shipped").Where(o => o.OrderId == existingOrder.OrderId).ExecuteNonQueryAsync();
await db.Orders().Delete().Where(o => o.OrderId == cancelledOrder.OrderId).ExecuteNonQueryAsync();
```

Bulk library → Quarry batch or raw SQL:
```csharp
// EFCore.BulkExtensions:
await ctx.BulkInsertAsync(users);

// Quarry:
await db.Users()
    .InsertBatch(u => (u.UserName, u.Email, u.IsActive))
    .Values(users)
    .ExecuteNonQueryAsync();
```

### 5.3 SqlKata → Quarry

| SqlKata | Quarry |
|---|---|
| `query.From("users").Where("id", id).Get<User>()` | `db.Users().Where(u => u.UserId == id).Select(u => u).ExecuteFetchAllAsync()` |
| `query.From("users").Select("name", "email").Get<UserDto>()` | `db.Users().Select(u => new UserDto { Name = u.UserName, Email = u.Email }).ExecuteFetchAllAsync()` |
| `query.From("users").Where("active", true).First<User>()` | `db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchFirstAsync()` |
| `query.From("users").Join("orders", "users.id", "orders.user_id").Get<T>()` | `db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => ...).ExecuteFetchAllAsync()` |
| `query.From("users").OrderBy("name").Limit(10).Offset(5).Get<User>()` | `db.Users().Select(u => u).OrderBy(u => u.UserName).Limit(10).Offset(5).ExecuteFetchAllAsync()` |
| `query.From("users").WhereLike("name", "%john%")` | `db.Users().Where(u => u.UserName.Contains("john"))` |
| `query.From("users").WhereStarts("name", "j")` | `db.Users().Where(u => u.UserName.StartsWith("j"))` |
| `query.From("users").WhereEnds("name", "n")` | `db.Users().Where(u => u.UserName.EndsWith("n"))` |
| `query.From("users").WhereIn("id", ids)` | `db.Users().Where(u => ids.Contains(u.UserId))` |
| `query.From("users").WhereNull("email")` | `db.Users().Where(u => u.Email == null)` |
| `query.From("users").WhereNotNull("email")` | `db.Users().Where(u => u.Email != null)` |
| `query.From("users").AsInsert(new { Name = "x" })` | `db.Users().Insert(new User { UserName = "x" }).ExecuteNonQueryAsync()` |
| `query.From("users").Where("id", id).AsUpdate(new { Name = "x" })` | `db.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).ExecuteNonQueryAsync()` |
| `query.From("users").Where("id", id).AsDelete()` | `db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync()` |
| `query.From("users").SelectRaw("COUNT(*) as total")` | `db.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>()` |
| `query.From("users").GroupBy("status").SelectRaw("status, COUNT(*)")` | `db.Users().GroupBy(u => u.Status).Select(u => (u.Status, Sql.Count())).ExecuteFetchAllAsync()` |

### 5.4 Raw ADO.NET → Quarry

| ADO.NET | Quarry |
|---|---|
| `cmd.CommandText = "SELECT ..."; reader = cmd.ExecuteReaderAsync()` + manual read loop | `db.Users().Where(...).Select(u => u).ExecuteFetchAllAsync()` |
| `cmd.CommandText = "INSERT ..."; cmd.ExecuteNonQueryAsync()` | `db.Users().Insert(new User { ... }).ExecuteNonQueryAsync()` |
| `cmd.ExecuteScalarAsync()` for COUNT | `db.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>()` |
| Complex stored proc / multi-result set | `db.RawSqlAsync<T>(sql, params)` or keep raw ADO.NET for complex scenarios |
| `DataTable` / `DataSet` | Not applicable — use typed entities |
| Manual `DbParameter` construction | Quarry handles parameterization from lambda expressions |
| Manual connection open/close | `QuarryContext` manages connection state |

### 5.5 Conversion Gotchas

Critical differences to watch for during conversion:

1. **Entity accessors are methods**: `db.Users()` NOT `db.Users`
2. **No anonymous types in Select**: Use DTOs or tuples. `new { x.Name }` → `new NameDto { Name = x.UserName }` or `(x.UserName,)`
3. **OrderBy position**: Not available directly on `IEntityAccessor<T>`. Must chain after `.Where()` or `.Select()` first: `db.Users().Select(u => u).OrderBy(u => u.UserName)`, NOT `db.Users().OrderBy(...)`
4. **Update .Set() syntax**: Assignment syntax `u => u.Name = value` or block `u => { u.Name = value; u.IsActive = true; }`. No two-argument `Set(selector, value)` overload.
5. **Delete/Update safety**: Requires `.Where()` or `.All()` — compile-time enforcement, will not compile without
6. **Max 4-table joins**: Queries joining more than 4 tables must be split or use `RawSqlAsync`
7. **Single method scope**: Chains must complete in one method. No passing partial chains to other methods, no `IQueryable`-style composition. QRY032 error if violated.
8. **PreparedQuery scope**: `.Prepare()` result cannot escape method scope — no returning, no passing as argument, no lambda capture. QRY035 error.
9. **Func<> not Expression<Func<>>**: Lambda syntax is identical — no code change needed. Analysis happens at compile time via source generator.
10. **No lazy loading or change tracking**: Every data fetch and modification is explicit.
11. **Select is required for queries**: Always call `.Select()` before terminal. `db.Users().Where(...).ExecuteFetchAllAsync()` will not work — needs `.Select(u => u)` or a projection.

---

## Phase 6: Migration System Setup

Skip this phase if Q4 answer is "None".

### 6.1 Install Tooling

```sh
dotnet tool install --global Quarry.Tool
```

### 6.2 Scaffold from Existing Database

```sh
quarry scaffold \
  --dialect PostgreSQL \
  --connection "Host=localhost;Database=mydb;Username=user;Password=pass" \
  --output ./Schemas \
  --namespace MyApp.Data.Schemas
```

Options:
- `--naming-style SnakeCase` if database uses snake_case names
- `--no-navigations` to skip `Many<T>` generation
- `--tables "users,orders"` to filter specific tables
- `--schema "public"` to filter by database schema

Review output. This produces schema classes that match the existing database exactly — use as the starting point, then adjust per Phase 3 rules.

### 6.3 Create Initial Migration

```sh
quarry migrate add InitialCreate \
  --project ./src/MyApp.Data/MyApp.Data.csproj \
  --context AppDb
```

Verify the generated migration matches existing DDL:
```sh
quarry migrate diff --project ./src/MyApp.Data/MyApp.Data.csproj --context AppDb
```

### 6.4 Runtime Migration Application

```csharp
await using var db = new AppDb(connection);
await db.MigrateAsync(connection, new MigrationOptions
{
    DryRun = false,
    RunBackups = true,
    Logger = migrationLogger
});
```

ASP.NET startup pattern:
```csharp
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDb>();
await db.MigrateAsync(db.Connection, new MigrationOptions { RunBackups = true });
```

### 6.5 Ongoing Migration Workflow

After any schema class changes:

```sh
# Generate migration from schema diff
quarry migrate add DescriptiveName --project ... --context AppDb

# Review what changed
quarry migrate diff --project ... --context AppDb

# List all migrations
quarry migrate list --project ... --context AppDb

# Validate integrity
quarry migrate validate --project ... --context AppDb

# Generate SQL script for CI/CD or DBA review
quarry migrate script --project ... --context AppDb --output ./migrations.sql

# Bundle for deployment
quarry migrate bundle --project ... --context AppDb
```

### 6.6 EF Migration Data Preservation

If the existing database has data and EF migrations were used previously:
1. Scaffold produces schemas matching current DB state
2. `InitialCreate` migration matches current DB state
3. Mark `InitialCreate` as already applied if DB is already at that state
4. Delete EF `Migrations/` folder and `__EFMigrationsHistory` table after cutover
5. Quarry uses `__quarry_migrations` table for its own history

---

## Phase 7: Verification

### 7.1 Build

Run `dotnet build`. Must succeed with zero QRY errors.

Expected informational output:
- QRY030 (info): prebuilt dispatch confirmations — normal, means carriers are generated
- QRA-series warnings: advisory analyzer suggestions — address if desired

Errors requiring action:
- QRY032: chain not statically analyzable — restructure the chain to complete in one method
- QRY033: forked chain — a builder variable is used in multiple terminal calls; use `.Prepare()` for multi-terminal
- QRY035: PreparedQuery escapes scope — keep PreparedQuery local to the method
- Any QRY001–QRY029: schema/query structure issues — fix per the error message

### 7.2 SQL Inspection

Use `ToDiagnostics()` on critical queries to inspect generated SQL:

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .ToDiagnostics();

// diag.Sql — the generated SQL string
// diag.Parameters — bound parameters
// diag.Dialect — confirms dialect
// diag.ProjectionKind — Entity, Dto, Tuple, SingleColumn
```

Compare generated SQL against original queries from the source library to verify semantic equivalence.

### 7.3 Testing

- Run existing tests against the converted code
- Use `QueryTestHarness` pattern for cross-dialect SQL verification if multi-dialect support is needed
- For integration tests, use SQLite in-memory for fast iteration
- During transition: run old and new implementations side-by-side, compare result sets

### 7.4 Cleanup

After all tests pass:

1. Remove old NuGet packages (Dapper, EF Core, SqlKata, their providers)
2. Remove old `DbContext` classes, entity configurations, `OnModelCreating`
3. Remove EF `Migrations/` folder
4. Remove old entity/POCO classes (replaced by Quarry-generated entity classes)
5. Remove old repository interfaces/implementations if fully replaced
6. Remove Dapper extension methods / helper classes
7. Remove SqlKata `QueryFactory` setup
8. Remove unused `using` statements
9. Clean DI registrations of old services
10. Remove `__EFMigrationsHistory` table from database (if EF was used)

---

## Execution Order

1. Phase 0 — prompt all five questions
2. Phase 1 — discovery scan, present inventory, get user confirmation
3. Phase 2 — generate migration plan with ratings, present for approval
4. Phase 3 — create schema classes (build to verify)
5. Phase 4 — create context + update .csproj + update DI (build to verify)
6. Phase 5 — convert all query/modification sites (build to verify)
7. Phase 6 — migration system setup (if opted in)
8. Phase 7 — full verification, SQL inspection, cleanup

Build after phases 3, 4, and 5 individually to catch errors incrementally. Present build results to user before proceeding to next phase.

**Task tracking:** After the plan is approved and before beginning execution, create tasks (via TaskCreate) for each phase and major sub-step. Mark each task in_progress when starting and completed when done. This gives the user visibility into migration progress and ensures no steps are skipped across long or multi-conversation migrations.
