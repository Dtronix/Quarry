# Implementation Plan: Quarry.Sample.WebApp

A self-contained ASP.NET Core Razor Pages application demonstrating Quarry's compile-time SQL builder with DI, cookie authentication, and full CRUD coverage.

**Location:** `src/Samples/Quarry.Sample.WebApp/`
**Target:** .NET 10, SQLite, Razor Pages
**Auth:** ASP.NET Core cookie authentication with custom login flow backed by Quarry queries
**Password hashing:** PBKDF2 via `Rfc2898DeriveBytes` (SHA-512, 600,000 iterations, 32-byte salt)
**Sessions:** Random 32-byte base64 tokens stored in a `sessions` table, validated via `PreparedQuery` in middleware
**Seed data:** Admin + demo users via `BatchInsert` on first run

---

## 1. Project Structure

```
src/Samples/
  Quarry.Sample.WebApp/
    Quarry.Sample.WebApp.csproj
    Program.cs
    Data/
      Schemas/
        UserSchema.cs
        AuditLogSchema.cs
        SessionSchema.cs
      AppDb.cs
      SeedData.cs
    Migrations/
      Snapshot_001_InitialCreate.cs
      Migration_001_InitialCreate.cs
    Services/
      PasswordHasher.cs
      SessionService.cs
      AuditService.cs
    Auth/
      SessionAuthHandler.cs
      SessionAuthDefaults.cs
    Pages/
      _ViewImports.cshtml
      _Layout.cshtml
      Index.cshtml / .cshtml.cs
      Register.cshtml / .cshtml.cs
      Login.cshtml / .cshtml.cs
      Logout.cshtml.cs
      Account/
        Index.cshtml / .cshtml.cs        (change password)
      Admin/
        Users/
          Index.cshtml / .cshtml.cs      (list + filter)
          Edit.cshtml / .cshtml.cs       (role/active/delete)
        Dashboard.cshtml / .cshtml.cs
        AuditLog.cshtml / .cshtml.cs
      Dev/
        Sql.cshtml / .cshtml.cs          (diagnostics catalog)
    wwwroot/
      css/
        site.css
    Logging/
      LogsmithBridge.cs
```

### 1.1 Project File

**Dependencies:** `Microsoft.Data.Sqlite`, `Quarry`, `Quarry.Generator` (analyzer ref). No other packages.

**Key MSBuild properties:**
- `<TargetFramework>net10.0</TargetFramework>`
- Quarry.Generator referenced with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`
- Project reference to `../Quarry/Quarry.csproj` and `../Quarry.Generator/Quarry.Generator.csproj` (local development, relative from `src/Samples/Quarry.Sample.WebApp/`)

**Tool dependency:** `Quarry.Tool` (local tool manifest or global install) — used to scaffold migration files via `quarry migrate add`

---

## 2. Schema Definitions

### 2.1 UserSchema

```csharp
public class UserSchema : Schema
```

**Table:** `users`

| Property | Type | Modifier |
|---|---|---|
| `UserId` | `Key<int>` | `Identity()` |
| `Email` | `Col<string>` | `Length(255)` |
| `UserName` | `Col<string>` | `Length(100)` |
| `PasswordHash` | `Col<string>` | `Sensitive()` |
| `Salt` | `Col<string>` | `Sensitive()` |
| `Role` | `Col<UserRole>` | (enum, stored as int) |
| `IsActive` | `Col<bool>` | `Default(true)` |
| `CreatedAt` | `Col<DateTime>` | `Default(() => DateTime.UtcNow)` |
| `LastLoginAt` | `Col<DateTime?>` | (nullable) |

**Navigations:**
- `Many<SessionSchema> Sessions => HasMany<SessionSchema>(s => s.UserId)`
- `Many<AuditLogSchema> AuditLogs => HasMany<AuditLogSchema>(a => a.UserId)`

**Indexes:**
- `IX_Email => Index(Email).Unique()`
- `IX_Role => Index(Role)`

**Enum:**

```csharp
public enum UserRole { User = 0, Admin = 1 }
```

The Quarry generator detects `Col<UserRole>` as an enum column. It stores the underlying `int` value in SQLite and casts back to `UserRole` on read. No special configuration needed — the `SchemaParser` sets `isEnum = true` automatically.

### 2.2 SessionSchema

```csharp
public class SessionSchema : Schema
```

**Table:** `sessions`

| Property | Type | Modifier |
|---|---|---|
| `SessionId` | `Key<int>` | `Identity()` |
| `UserId` | `Ref<UserSchema, int>` | `ForeignKey<UserSchema, int>()` |
| `Token` | `Col<string>` | `Length(64)` |
| `ExpiresAt` | `Col<DateTime>` | |
| `CreatedAt` | `Col<DateTime>` | `Default(() => DateTime.UtcNow)` |

**Indexes:**
- `IX_Token => Index(Token).Unique()`
- `IX_ExpiresAt => Index(ExpiresAt)`

The `Ref<UserSchema, int>` column generates an `EntityRef<User, int>` property on the `Session` entity. Access the raw FK value via `.Id` and the loaded navigation via `.Value`.

### 2.3 AuditLogSchema

```csharp
public class AuditLogSchema : Schema
```

**Table:** `audit_logs`

| Property | Type | Modifier |
|---|---|---|
| `AuditLogId` | `Key<int>` | `Identity()` |
| `UserId` | `Ref<UserSchema, int>` | `ForeignKey<UserSchema, int>()` |
| `Action` | `Col<AuditAction>` | (enum) |
| `Detail` | `Col<string?>` | `Length(500)` |
| `IpAddress` | `Col<string?>` | `Length(45)` |
| `CreatedAt` | `Col<DateTime>` | `Default(() => DateTime.UtcNow)` |

**Enum:**

```csharp
public enum AuditAction
{
    Login = 0,
    Logout = 1,
    PasswordChange = 2,
    RoleChange = 3,
    AccountCreated = 4,
    AccountDeleted = 5,
    AccountDeactivated = 6,
    AccountReactivated = 7
}
```

### 2.4 Generated Entities

The Quarry generator reads each `*Schema` class and emits a corresponding entity class (e.g., `User`, `Session`, `AuditLog`) with:
- Plain properties for `Col<T>` columns
- `EntityRef<TEntity, TKey>` for `Ref<T, TKey>` columns
- `NavigationList<T>` for `Many<T>` navigations

These entity types are what `IQueryBuilder<T>` and `IEntityAccessor<T>` operate on.

---

## 3. Context

### 3.1 AppDb

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
```

**Entity accessors:**

```csharp
public partial IEntityAccessor<User> Users();
public partial IEntityAccessor<Session> Sessions();
public partial IEntityAccessor<AuditLog> AuditLogs();
```

The constructor takes `IDbConnection`. The generator emits the partial implementations for each accessor method, producing typed carrier classes for every query chain in the project.

`QuarryContext` implements `IAsyncDisposable`. It manages connection open/close lifecycle — if the connection was already open when passed in, it leaves it open on dispose; otherwise it closes it.

---

## 3.2 Migrations (Quarry.Tool)

The sample ships with a pre-scaffolded migration generated by `quarry migrate add InitialCreate`. This demonstrates the full Quarry.Tool migration lifecycle: schema definition → migration scaffolding → runtime application.

**Generated files (checked into the repo):**

- `Migrations/Snapshot_001_InitialCreate.cs` — Compilable C# snapshot capturing the full schema state (all three tables, columns, indexes, foreign keys). The tool compiles this snapshot in memory when scaffolding future migrations to diff against the current schema.
- `Migration_001_InitialCreate.cs` — Migration class with `Upgrade()`, `Downgrade()`, and `Backup()` methods plus partial hooks (`BeforeUpgrade`/`AfterUpgrade`/`BeforeDowngrade`/`AfterDowngrade`).

**Upgrade() contents:**

```csharp
// Creates tables: users, sessions, audit_logs
// Creates indexes: IX_Email (unique), IX_Role, IX_Token (unique), IX_ExpiresAt
// Creates foreign keys: sessions.UserId → users.UserId, audit_logs.UserId → users.UserId
```

**Scaffolding command (for reference / to extend the sample):**

```sh
# From the sample project directory:
quarry migrate add InitialCreate -p .

# To preview changes without generating files:
quarry migrate diff -p .

# To add a subsequent migration after schema changes:
quarry migrate add <MigrationName> -p .
```

**How it was generated:** After defining `UserSchema`, `SessionSchema`, `AuditLogSchema`, and `AppDb`, run `quarry migrate add InitialCreate` from the project directory. The tool opens the project via Roslyn, discovers all `Schema` subclasses, extracts table/column/index/FK metadata, and generates the snapshot + migration files.

---

## 4. Dependency Injection

### 4.1 Service Registration (Program.cs)

```csharp
// Connection factory — scoped so each request gets its own connection
builder.Services.AddScoped<SqliteConnection>(sp =>
    new SqliteConnection(builder.Configuration.GetConnectionString("Default")));

// Quarry context — scoped, takes connection
builder.Services.AddScoped<AppDb>(sp =>
    new AppDb(sp.GetRequiredService<SqliteConnection>()));

// Application services
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<PasswordHasher>();
```

**Connection string:** `"Data Source=quarry_sample.db"` in `appsettings.json`.

**Key DI concept:** `AppDb` wraps a `DbConnection` and is scoped per-request. This is the standard pattern for data access in ASP.NET Core — the DI container creates and disposes the connection + context at the end of each request.

### 4.2 Authentication Wiring

```csharp
builder.Services.AddAuthentication(SessionAuthDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(
        SessionAuthDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireClaim(ClaimTypes.Role, "Admin"));
});
```

### 4.3 Middleware Pipeline Order

```csharp
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
```

### 4.4 Database Initialization

On startup, before `app.Run()`:

1. Open a scoped `AppDb`
2. Call `db.MigrateAsync(connection)` to apply the scaffolded `Migration_001_InitialCreate` (creates `users`, `sessions`, `audit_logs` tables with indexes and FKs on first run; no-ops on subsequent runs)
3. Call `SeedData.InitializeAsync(db)` to seed if `users` table is empty

`MigrateAsync` reads the `[MigrationSnapshot]` and `[Migration]` attributed classes discovered at compile time, tracks applied versions in a `__quarry_migrations` metadata table, and applies any pending migrations in order. This is the runtime counterpart to the `quarry migrate add` scaffolding tool.

---

## 5. Password Hashing

### 5.1 PasswordHasher Service

```csharp
public sealed class PasswordHasher
```

**Algorithm:** PBKDF2 with SHA-512, 600,000 iterations (OWASP 2023 recommendation), 32-byte salt, 64-byte hash output.

**Methods:**

```csharp
public (string hash, string salt) Hash(string password);
public bool Verify(string password, string hash, string salt);
```

**Hash algorithm:**

1. Generate 32 random bytes via `RandomNumberGenerator.Fill()`
2. Derive 64-byte key via `Rfc2898DeriveBytes(password, saltBytes, 600_000, HashAlgorithmName.SHA512)`
3. Return both as base64 strings

**Verify algorithm:**

1. Decode salt and hash from base64
2. Re-derive key from password + decoded salt with same parameters
3. Compare via `CryptographicOperations.FixedTimeEquals()` — constant-time comparison prevents timing attacks

**Registered as singleton** — stateless, thread-safe, no instance data.

---

## 6. Session Management

### 6.1 SessionService

```csharp
public sealed class SessionService
```

**Constructor injection:** `AppDb`, `AuditService`

**Methods:**

```csharp
public Task<string> CreateSessionAsync(int userId, string ipAddress);
public Task<User?> ValidateSessionAsync(string token);
public Task InvalidateSessionAsync(string token);
public Task PurgeExpiredSessionsAsync();
```

**CreateSessionAsync:**

1. Generate 32 random bytes via `RandomNumberGenerator.Fill()`
2. Convert to base64 → token string
3. Insert into `sessions` via `db.Sessions().Insert(new Session { ... }).ExecuteNonQueryAsync()`
4. Session expiry: 24 hours from creation
5. Return token string

**Quarry operations demonstrated:**
- `Insert` — create session row
- `Select + Where` — validate token lookup
- `Delete + Where` — invalidate single session
- `Delete + Where` — purge expired sessions (batch delete with date comparison)

### 6.2 Session Validation (PreparedQuery in Auth Handler)

The auth handler validates sessions on every authenticated request. This is the ideal `PreparedQuery` showcase — a query executed on every request with the same structure but different parameter values.

The auth handler calls `SessionService.ValidateSessionAsync(token)` which performs a join between `sessions` and `users`:

```csharp
// In SessionService — the chain is built once per scope, executed once
db.Sessions()
    .Join<User>((s, u) => s.UserId.Id == u.UserId)
    .Where((s, u) => s.Token == token && s.ExpiresAt > DateTime.UtcNow && u.IsActive)
    .Select((s, u) => u)
    .ExecuteFetchFirstOrDefaultAsync();
```

This demonstrates: `Join`, `Where` with multiple conditions, `Select` with entity projection, `ExecuteFetchFirstOrDefaultAsync`.

### 6.3 SessionAuthHandler

```csharp
public class SessionAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
```

Extends ASP.NET Core's `AuthenticationHandler<T>`. Overrides `HandleAuthenticateAsync()`.

**Flow:**

1. Read cookie `quarry_session` from request
2. If missing → `AuthenticateResult.NoResult()`
3. Call `SessionService.ValidateSessionAsync(token)`
4. If null → `AuthenticateResult.Fail("Invalid session")`
5. Build `ClaimsPrincipal` with `ClaimTypes.NameIdentifier` (UserId), `ClaimTypes.Name` (UserName), `ClaimTypes.Email`, `ClaimTypes.Role` (UserRole)
6. Return `AuthenticateResult.Success(ticket)`

---

## 7. Audit Logging

### 7.1 AuditService

```csharp
public sealed class AuditService
```

**Constructor injection:** `AppDb`

**Methods:**

```csharp
public Task LogAsync(int userId, AuditAction action, string? detail = null, string? ipAddress = null);
```

**Implementation:** Single `Insert` call:

```csharp
db.AuditLogs().Insert(new AuditLog
{
    UserId = userId,    // EntityRef<User, int> — set via int
    Action = action,
    Detail = detail,
    IpAddress = ipAddress,
    CreatedAt = DateTime.UtcNow
}).ExecuteNonQueryAsync();
```

Demonstrates initializer-aware insert — only the properties set in the object initializer generate INSERT columns. `AuditLogId` (identity) is omitted automatically.

---

## 8. Seed Data

### 8.1 SeedData.InitializeAsync

```csharp
public static class SeedData
```

```csharp
public static async Task InitializeAsync(AppDb db, PasswordHasher hasher);
```

**Algorithm:**

1. Check if any users exist: `db.Users().Select(u => u.UserId).Limit(1).ExecuteFetchFirstOrDefaultAsync()`
2. If result is non-default → return (already seeded)
3. Create admin user with `PasswordHasher.Hash("admin123")`
4. Create 5 demo users with known passwords
5. Insert all via `db.Users().InsertBatch(u => (u.Email, u.UserName, u.PasswordHash, u.Salt, u.Role, u.IsActive, u.CreatedAt)).Values(users).ExecuteNonQueryAsync()`

**Quarry operation demonstrated:** `BatchInsert` — the column selector lambda `u => (u.Email, u.UserName, ...)` is analyzed at compile time by the generator. The `Values(users)` call provides the runtime collection. The generator emits a carrier that builds the multi-row INSERT SQL with parameter expansion.

**Seed users:**

| Email | UserName | Role | Password |
|---|---|---|---|
| admin@example.com | admin | Admin | admin123 |
| alice@example.com | alice | User | password1 |
| bob@example.com | bob | User | password2 |
| carol@example.com | carol | User | password3 |
| dave@example.com | dave | User | password4 |
| eve@example.com | eve | User | password5 |

---

## 9. Pages

### 9.1 Register (`/Register`)

**PageModel:** `RegisterModel`

```csharp
public class RegisterModel : PageModel
```

**Constructor injection:** `AppDb`, `PasswordHasher`, `SessionService`, `AuditService`

**Bind properties:** `Email`, `UserName`, `Password`, `ConfirmPassword`

**OnPostAsync flow:**

1. Validate model state
2. Check email uniqueness: `db.Users().Where(u => u.Email == Input.Email).Select(u => u.UserId).ExecuteFetchFirstOrDefaultAsync()`
3. Hash password via `PasswordHasher.Hash()`
4. Insert user: `db.Users().Insert(new User { ... }).ExecuteScalarAsync<int>()` — returns generated `UserId`
5. Create session via `SessionService.CreateSessionAsync(userId, ipAddress)`
6. Set cookie `quarry_session` with token
7. Audit log: `AuditService.LogAsync(userId, AuditAction.AccountCreated)`
8. Redirect to `/`

**Quarry operations:** `Select + Where` (uniqueness check), `Insert + ExecuteScalarAsync` (insert with identity return), audit `Insert`.

### 9.2 Login (`/Login`)

**PageModel:** `LoginModel`

```csharp
public class LoginModel : PageModel
```

**Bind properties:** `Email`, `Password`

**OnPostAsync flow:**

1. Look up user by email: `db.Users().Where(u => u.Email == Input.Email).Select(u => u).ExecuteFetchFirstOrDefaultAsync()`
2. If null → add model error, return page
3. Verify password via `PasswordHasher.Verify()`
4. If invalid → add model error, return page
5. Check `IsActive` — if false → add model error "Account deactivated"
6. Update last login: `db.Users().Update().Set(u => u.LastLoginAt = DateTime.UtcNow).Where(u => u.UserId == user.UserId).ExecuteNonQueryAsync()`
7. Create session, set cookie
8. Audit log: `AuditAction.Login`
9. Redirect to return URL or `/`

**Quarry operations:** `Select + Where` (user lookup), `Update + Set + Where` (last login timestamp), session `Insert`, audit `Insert`.

### 9.3 Logout (`/Logout`)

**PageModel:** `LogoutModel` (POST only, no GET page)

**OnPostAsync flow:**

1. Read cookie token
2. `SessionService.InvalidateSessionAsync(token)` — performs `db.Sessions().Delete().Where(s => s.Token == token).ExecuteNonQueryAsync()`
3. Delete cookie
4. Audit log: `AuditAction.Logout`
5. Redirect to `/Login`

**Quarry operations:** `Delete + Where` (session removal), audit `Insert`.

### 9.4 Account (`/Account`)

**Authorization:** `[Authorize]`

**PageModel:** `AccountModel`

**OnGetAsync:** Load current user by claim UserId: `db.Users().Where(u => u.UserId == userId).Select(u => new { u.UserName, u.Email, u.CreatedAt }).ExecuteFetchFirstAsync()`

**Bind properties:** `CurrentPassword`, `NewPassword`, `ConfirmNewPassword`

**OnPostAsync (change password):**

1. Load user (full entity for hash/salt): `db.Users().Where(u => u.UserId == userId).Select(u => u).ExecuteFetchFirstAsync()`
2. Verify current password
3. Hash new password
4. Update: `db.Users().Update().Set(u => { u.PasswordHash = newHash; u.Salt = newSalt; }).Where(u => u.UserId == userId).ExecuteNonQueryAsync()`
5. Audit log: `AuditAction.PasswordChange`

**Quarry operations:** `Select + Where` (user load), `Update + Set (multi-column assignment) + Where`, audit `Insert`.

The multi-column `Set` lambda demonstrates the block-body assignment syntax where multiple columns are set in a single lambda. The generator analyzes each assignment statement and emits the corresponding SET clauses.

### 9.5 Admin Users List (`/Admin/Users`)

**Authorization:** `[Authorize(Policy = "Admin")]`

**PageModel:** `AdminUsersModel`

**Query parameters (from form/query string):** `Search` (string?), `RoleFilter` (UserRole?), `ActiveOnly` (bool?), `Page` (int, default 1)

**OnGetAsync — conditional chain construction:**

```csharp
var query = db.Users().Select(u => new { u.UserId, u.UserName, u.Email, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt });

if (!string.IsNullOrEmpty(Search))
    query = query.Where(u => u.UserName.Contains(Search) || u.Email.Contains(Search));

if (RoleFilter.HasValue)
    query = query.Where(u => u.Role == RoleFilter.Value);

if (ActiveOnly == true)
    query = query.Where(u => u.IsActive);

var users = await query
    .OrderBy(u => u.UserName)
    .Limit(PageSize)
    .Offset((Page - 1) * PageSize)
    .ExecuteFetchAllAsync();
```

This is the conditional chain showcase. Each `if` branch is a conditional clause. The generator assigns each conditional `Where` a bit index and pre-builds all SQL variants (up to 8 variants for 3 conditional bits). At runtime, the carrier's `ClauseMask` switch selects the correct pre-built SQL string.

**Total count for pagination:** Separate query with same conditional structure:

```csharp
// Same conditional pattern, different terminal
var totalCount = await db.Users()
    .Where(u => /* same conditions */)
    .Select(u => Sql.Count())
    .ExecuteScalarAsync<int>();
```

**Navigation subquery — recently active users indicator:**

```csharp
// Users who have a non-expired session (logged in recently)
db.Users()
    .Where(u => u.Sessions.Any(s => s.ExpiresAt > DateTime.UtcNow))
    .Select(u => u.UserId)
    .ExecuteFetchAllAsync();
```

This generates a correlated `EXISTS (SELECT 1 FROM "sessions" WHERE ...)` subquery. The `Many<SessionSchema>` navigation's FK is used for correlation.

**Quarry operations:** Conditional `Where` chains, `OrderBy`, `Limit`/`Offset` pagination, `Sql.Count()` aggregate, navigation `Any()` subquery.

### 9.6 Admin User Edit (`/Admin/Users/{id}`)

**Authorization:** `[Authorize(Policy = "Admin")]`

**Route:** `@page "{id:int}"`

**PageModel:** `AdminUserEditModel`

**OnGetAsync:**

1. Load user: `db.Users().Where(u => u.UserId == id).Select(u => u).ExecuteFetchFirstOrDefaultAsync()`
2. Load recent audit logs for this user (join with Users for display):

```csharp
db.AuditLogs()
    .Where(a => a.UserId.Id == id)
    .OrderBy(a => a.CreatedAt, Direction.Descending)
    .Select(a => new { a.Action, a.Detail, a.IpAddress, a.CreatedAt })
    .Limit(10)
    .ExecuteFetchAllAsync();
```

**OnPostAsync — toggle active:**

```csharp
db.Users().Update()
    .Set(u => u.IsActive = !currentState)
    .Where(u => u.UserId == id)
    .ExecuteNonQueryAsync();
```

Audit: `AuditAction.AccountDeactivated` or `AuditAction.AccountReactivated`.

**OnPostAsync — change role:**

```csharp
db.Users().Update()
    .Set(u => u.Role = newRole)
    .Where(u => u.UserId == id)
    .ExecuteNonQueryAsync();
```

Audit: `AuditAction.RoleChange` with detail string showing old → new role.

**OnPostAsync — delete user:**

1. Delete all sessions: `db.Sessions().Delete().Where(s => s.UserId.Id == id).ExecuteNonQueryAsync()`
2. Delete all audit logs: `db.AuditLogs().Delete().Where(a => a.UserId.Id == id).ExecuteNonQueryAsync()`
3. Delete user: `db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync()`
4. Redirect to `/Admin/Users`

Related rows must be deleted first due to FK constraints. This demonstrates multi-table `Delete + Where` with FK reference.

**Quarry operations:** `Select + Where`, `Update + Set + Where` (single column), `Delete + Where` (cascading across 3 tables), audit `Insert`.

### 9.7 Admin Dashboard (`/Admin/Dashboard`)

**Authorization:** `[Authorize(Policy = "Admin")]`

**PageModel:** `AdminDashboardModel`

**Aggregate queries:**

**Users by role:**

```csharp
db.Users()
    .GroupBy(u => u.Role)
    .Select(u => (u.Role, Sql.Count()))
    .ExecuteFetchAllAsync();
```

**Logins per day (last 7 days):**

```csharp
db.AuditLogs()
    .Where(a => a.Action == AuditAction.Login && a.CreatedAt > sevenDaysAgo)
    .GroupBy(a => a.CreatedAt)  // Note: SQLite date grouping needs Sql.Raw
    .Select(a => (a.CreatedAt, Sql.Count()))
    .ExecuteFetchAllAsync();
```

For SQLite date grouping (since SQLite has no native DATE function on grouped columns), use `Sql.Raw`:

```csharp
db.RawSqlAsync<DailyLoginCount>(
    "SELECT date(\"CreatedAt\") as Day, COUNT(*) as Count FROM \"audit_logs\" WHERE \"Action\" = @p0 AND \"CreatedAt\" > @p1 GROUP BY date(\"CreatedAt\") ORDER BY Day DESC",
    (int)AuditAction.Login, sevenDaysAgo);
```

This demonstrates RawSql as a fallback for dialect-specific functions.

**Total counts:**

```csharp
var totalUsers = await db.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
var activeUsers = await db.Users().Where(u => u.IsActive).Select(u => Sql.Count()).ExecuteScalarAsync<int>();
var totalSessions = await db.Sessions().Where(s => s.ExpiresAt > DateTime.UtcNow).Select(s => Sql.Count()).ExecuteScalarAsync<int>();
```

**Quarry operations:** `GroupBy + Select` with aggregates (`Sql.Count()`), `ExecuteScalarAsync`, `RawSqlAsync` for dialect-specific SQL.

### 9.8 Admin Audit Log (`/Admin/AuditLog`)

**Authorization:** `[Authorize(Policy = "Admin")]`

**PageModel:** `AdminAuditLogModel`

**Query parameters:** `Page` (int), `UserFilter` (int?), `ActionFilter` (AuditAction?)

**OnGetAsync — join query with pagination:**

```csharp
db.AuditLogs()
    .Join<User>((a, u) => a.UserId.Id == u.UserId)
    .Where((a, u) => /* optional filters */)
    .OrderBy((a, u) => a.CreatedAt, Direction.Descending)
    .Select((a, u) => new AuditLogEntry
    {
        AuditLogId = a.AuditLogId,
        UserName = u.UserName,
        Action = a.Action,
        Detail = a.Detail,
        IpAddress = a.IpAddress,
        CreatedAt = a.CreatedAt
    })
    .Limit(PageSize)
    .Offset((Page - 1) * PageSize)
    .ExecuteFetchAllAsync();
```

**Quarry operations:** `Join` (2-table), joined `Select` with DTO projection, joined `Where` with conditional filters, `OrderBy` descending, `Limit`/`Offset`.

### 9.9 Dev SQL Inspector (`/Dev/Sql`)

**Condition:** Only available in Development environment. Use `[Authorize(Policy = "Admin")]` as additional guard.

**PageModel:** `DevSqlModel`

**Implementation:** A fixed catalog of ~10 representative queries. Each calls `ToDiagnostics()` and the page renders the results.

**Query catalog (each entry: label, description, diagnostics):**

| # | Label | Chain | Features demonstrated |
|---|---|---|---|
| 1 | Simple Select | `Users().Select(u => u).ToDiagnostics()` | Entity projection, full column list |
| 2 | Filtered Select | `Users().Where(u => u.IsActive && u.Role == UserRole.Admin).Select(u => new { u.UserName, u.Email }).ToDiagnostics()` | Where with enum, DTO projection |
| 3 | Pagination | `Users().Select(u => u).OrderBy(u => u.UserName).Limit(10).Offset(20).ToDiagnostics()` | OrderBy, Limit, Offset |
| 4 | Join | `AuditLogs().Join<User>((a, u) => a.UserId.Id == u.UserId).Select((a, u) => (u.UserName, a.Action)).ToDiagnostics()` | 2-table join, tuple projection |
| 5 | Aggregate | `Users().GroupBy(u => u.Role).Select(u => (u.Role, Sql.Count())).ToDiagnostics()` | GroupBy, Sql.Count() |
| 6 | Navigation subquery | `Users().Where(u => u.Sessions.Any(s => s.ExpiresAt > DateTime.UtcNow)).Select(u => u).ToDiagnostics()` | EXISTS subquery |
| 7 | Insert | `Users().Insert(new User { UserName = "x", Email = "x@x.com" }).ToDiagnostics()` | Initializer-aware insert |
| 8 | Update | `Users().Update().Set(u => { u.UserName = "x"; u.IsActive = true; }).Where(u => u.UserId == 1).ToDiagnostics()` | Multi-column Set, Where |
| 9 | Delete | `Users().Delete().Where(u => u.UserId == 1).ToDiagnostics()` | Delete with Where |
| 10 | Batch Insert | `Users().InsertBatch(u => (u.UserName, u.Email)).Values(sampleList).ToDiagnostics()` | Batch insert column selector |

**Page display per query:**

- Label and description
- `diag.Sql` — the generated SQL string
- `diag.Dialect` — SQLite
- `diag.Tier` — PrebuiltDispatch
- `diag.Kind` — Select/Insert/Update/Delete
- `diag.IsCarrierOptimized` — true
- `diag.Parameters` — name/value pairs
- `diag.Clauses` — per-clause breakdown with `ClauseType`, `SqlFragment`, `IsConditional`, `IsActive`
- `diag.ProjectionKind` — Entity/Dto/Tuple/SingleColumn (where applicable)

`QueryDiagnostics` is a rich metadata object returned by `ToDiagnostics()`. It contains the complete compile-time analysis of the query chain — the SQL that will execute, the parameters that will bind, the optimization tier, and a clause-by-clause breakdown. This is a compile-time artifact, not a runtime query plan.

---

## 10. Logsmith Integration

### 10.1 LogsmithBridge

Bridges Quarry's Logsmith logging to ASP.NET Core's `ILogger` infrastructure.

```csharp
public sealed class LogsmithBridge : ILogsmithLogger
```

**Constructor injection:** `ILoggerFactory`

**Implementation:**

```csharp
public bool IsEnabled(LogLevel level, string category);
public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message);
```

`IsEnabled` maps Logsmith `LogLevel` to Microsoft `LogLevel` and checks against `ILoggerFactory.CreateLogger(category).IsEnabled()`.

`Write` decodes the UTF-8 message span and forwards to the appropriate `ILogger` instance by category name.

**Registration in Program.cs:**

```csharp
// After building the app, wire Logsmith to ILogger
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
LogsmithOutput.Logger = new LogsmithBridge(loggerFactory);
```

**Log level mapping:**

| Logsmith | Microsoft |
|---|---|
| `Trace` | `Trace` |
| `Debug` | `Debug` |
| `Information` | `Information` |
| `Warning` | `Warning` |
| `Error` | `Error` |

**What appears in console output (at Debug level):**

- `[DBG] Quarry.Query: [42] SQL: SELECT "UserId", "Email" ...`
- `[DBG] Quarry.Query: [42] Fetched 10 rows in 1.2ms`
- `[TRC] Quarry.Parameters: [42] @p0 = true` (but `PasswordHash`/`Salt` show as `***` due to `Sensitive()`)
- `[INF] Quarry.Connection: Connection opened`
- `[WRN] Quarry.Execution: [42] Slow query (512.3ms): SELECT ...`

The `Sensitive()` modifier on `PasswordHash` and `Salt` in the schema causes the generator to emit `***` for those parameter values in all log output. The redaction happens at the carrier level — the actual value is never passed to the logging system.

---

## 11. Expired Session Cleanup

### 11.1 Inline Cleanup

Rather than a background service, purge expired sessions opportunistically during login:

```csharp
// In SessionService.CreateSessionAsync, after inserting the new session:
db.Sessions().Delete()
    .Where(s => s.ExpiresAt < DateTime.UtcNow)
    .ExecuteNonQueryAsync();
```

This keeps the sample simple — no `IHostedService`, no timers. The delete runs on every login and removes all expired rows in one statement.

---

## 12. UI/Layout

### 12.1 _Layout.cshtml

Minimal layout with:
- Navigation bar showing: app name, conditional links based on auth state
- If authenticated: show username, Account link, Logout button
- If Admin role: show Admin dropdown (Users, Dashboard, Audit Log)
- If Development environment: show Dev/SQL link
- Basic CSS via `site.css` (no framework, minimal custom styles for readability)

### 12.2 Page Forms

All forms use standard Razor tag helpers (`asp-for`, `asp-validation-for`, `asp-page-handler`). Model validation via data annotations on bind properties (`[Required]`, `[EmailAddress]`, `[MinLength]`, `[Compare]`).

Anti-forgery tokens are automatic with Razor Pages `asp-page` forms.

### 12.3 Styling

Plain CSS in `wwwroot/css/site.css`. No CSS framework. Simple table styles, form layout, flash messages via TempData. The focus is on the backend code, not the UI.

---

## 13. Quarry Feature Coverage Matrix

| Quarry Feature | Where Used | Section |
|---|---|---|
| `Insert` (single) | Register, Login (session), Audit log | 9.1, 9.2, 7.1 |
| `Insert + ExecuteScalarAsync` | Register (return UserId) | 9.1 |
| `BatchInsert` | Seed data | 8.1 |
| `Select + Where` | Login (email lookup), Account (profile load) | 9.2, 9.4 |
| `Select + Where + OrderBy + Limit + Offset` | Admin Users list, Admin Audit Log | 9.5, 9.8 |
| `Select + Join` | Session validation (Sessions+Users), Audit log list | 6.2, 9.8 |
| `Select + DTO projection` | Account page, Audit log list | 9.4, 9.8 |
| `Select + Tuple projection` | Dev SQL page | 9.9 |
| `Select + Entity projection` | Login, Admin user edit | 9.2, 9.6 |
| `Sql.Count()` aggregate | Dashboard totals, pagination count | 9.7, 9.5 |
| `GroupBy + Select` | Dashboard (users by role) | 9.7 |
| `Update + Set (single col)` | Toggle active, Change role, Update LastLoginAt | 9.6, 9.2 |
| `Update + Set (multi col)` | Change password (hash + salt) | 9.4 |
| `Delete + Where` | Logout (session), Delete user (cascade), Purge expired | 9.3, 9.6, 11.1 |
| `Conditional Where chains` | Admin Users list (search, role, active filters) | 9.5 |
| `Navigation subquery (Any)` | Admin Users (recently active indicator) | 9.5 |
| `PreparedQuery` | Session validation middleware | 6.2 |
| `ToDiagnostics()` | Dev SQL inspector page | 9.9 |
| `Sensitive()` | PasswordHash, Salt columns | 2.1 |
| `ExecuteFetchFirstOrDefaultAsync` | Login, session validation | 9.2, 6.2 |
| `ExecuteFetchAllAsync` | User lists, audit log, dashboard | 9.5, 9.8, 9.7 |
| `ExecuteScalarAsync` | Register (identity return), counts | 9.1, 9.7 |
| `ExecuteNonQueryAsync` | All inserts/updates/deletes | throughout |
| `RawSqlAsync` | Dashboard (SQLite date grouping) | 9.7 |
| `Enum columns` | UserRole, AuditAction | 2.1, 2.3 |
| `Indexes` | Email (unique), Token (unique), ExpiresAt, Role | 2.1, 2.2 |
| `MigrateAsync` | Startup DB initialization (applies scaffolded migrations) | 4.4 |
| `quarry migrate add` | Migration scaffolding via Quarry.Tool CLI | 3.2 |
| `MigrationSnapshot` | Compiled C# snapshot for diff-based migration generation | 3.2 |
| `Logsmith → ILogger` | All queries logged to ASP.NET Core logging | 10.1 |
| Cookie auth + DI | Full ASP.NET Core auth pipeline with scoped AppDb | 4.1, 4.2, 6.3 |

---

## 14. Implementation Order

1. Project scaffolding — `.csproj`, `Program.cs` skeleton, `appsettings.json`
2. Schema definitions — `UserSchema`, `SessionSchema`, `AuditLogSchema`, enums
3. Context — `AppDb` with entity accessors
4. **Scaffold migrations** — run `quarry migrate add InitialCreate` to generate `Snapshot_001_InitialCreate.cs` and `Migration_001_InitialCreate.cs` in `Migrations/`
5. `PasswordHasher` service
6. `AuditService`
7. `SessionService`
8. DI registration and middleware pipeline in `Program.cs`
9. `SessionAuthHandler` + `SessionAuthDefaults`
10. `SeedData`
11. Database initialization in `Program.cs` — `MigrateAsync()` applies scaffolded migration, then seed
12. `_Layout.cshtml`, `_ViewImports.cshtml`, `site.css`
13. Pages: Register → Login → Logout → Account
14. Pages: Admin/Users (list + conditional chains) → Admin/Users/Edit
15. Pages: Admin/Dashboard (aggregates) → Admin/AuditLog (joins)
16. Pages: Dev/Sql (diagnostics catalog)
17. `LogsmithBridge`
18. Verify build + manual test flow
