# Scaffolding

Reverse-engineer an existing database into Quarry schema classes and a context -- the database-first workflow.

## Database-First vs Code-First

Quarry supports two workflows for defining your data model:

- **Code-first** -- you write schema classes by hand and use `quarry migrate` to generate migration files that create or update the database. This is the default path described in the [schema definition](schema-definition.md) and [migrations](migrations.md) guides.
- **Database-first (scaffolding)** -- you point `quarry scaffold` at an existing database and it generates schema classes and a context for you. You then review, adjust, and use them like any hand-written schema.

Use scaffolding when:

- You have an existing database that predates your Quarry project.
- You are migrating from another data access library (EF Core, Dapper, etc.) and want a starting point.
- You need a quick schema snapshot for a database you don't control.

After scaffolding, you own the generated files. Edit them freely, then use `quarry migrate add` to start tracking future changes as code-first migrations.

## Usage

```sh
# SQLite
quarry scaffold -d sqlite --database school.db -o Schemas --namespace MyApp

# PostgreSQL
quarry scaffold -d postgresql --server localhost --user admin --password secret --database mydb -o Schemas

# SQL Server with connection string, non-interactive
quarry scaffold -c "Server=localhost;Database=mydb" -d sqlserver -o Schemas --ni
```

## Options

| Flag | Description |
|---|---|
| `-d, --dialect` | SQL dialect (required): `sqlite`, `postgresql`, `mysql`, `sqlserver` |
| `--database` | Database file (SQLite) or name |
| `--server`, `--port`, `--user`, `--password` | Connection parameters |
| `-c, --connection` | Connection string (alternative to individual params) |
| `-o, --output` | Output directory (default: `.`) |
| `--namespace` | Namespace for generated classes |
| `--schema` | Schema filter (e.g., `public`, `dbo`) |
| `--tables` | Comma-separated table filter |
| `--naming-style` | `Exact`, `SnakeCase`, `CamelCase`, `LowerCase` |
| `--no-navigations` | Skip generating `Many<T>` navigation properties |
| `--no-singularize` | Don't singularize table names to class names |
| `--context` | Custom context class name |
| `--ni` | Non-interactive mode (auto-accept implicit FKs) |

## What It Generates

For each table, the scaffolder produces a schema class file. For the entire database, it produces one context class file.

### Schema classes

Each table becomes a class inheriting `Schema` with:

- A `static string Table` property set to the original table name.
- A `NamingStyle` override if you specified `--naming-style` (anything other than `Exact`).
- **Primary key columns** as `Key<T>` properties. Identity/auto-increment columns get the `Identity()` modifier. GUID primary keys get `ClientGenerated()`.
- **Foreign key columns** as `Ref<TSchema, TKey>` properties pointing at the referenced table's schema class. ON DELETE/ON UPDATE actions are preserved as comments.
- **Regular columns** as `Col<T>` properties with appropriate modifiers: `Length(n)` for sized strings, `Precision(p, s)` for decimals, `Default(value)` for simple defaults, and `Computed()` for generated columns.
- **Composite primary keys** as a `CompositeKey PK` property listing the participating columns.
- **Navigation properties** as `Many<T>` properties for each incoming foreign key from another table (unless `--no-navigations` is set). When multiple FKs from the same table point here, the property names are disambiguated with a `By{Column}` suffix.
- **Indexes** as `Index` properties, with `.Unique()` applied where appropriate.
- **Junction table annotations** -- if the table is detected as a many-to-many junction, a comment at the top identifies the two related entities.

Nullable database columns map to nullable CLR types (`string?`, `int?`, etc.). Type warnings appear as comments above the property when the mapper encounters an ambiguous or unmapped SQL type.

Example output for a `students` table:

```csharp
// Auto-scaffolded by quarry from database 'school' on 2026-03-27
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace MyApp;

public class StudentSchema : Schema
{
    public static string Table => "students";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> StudentId => Identity();
    public Col<string> FirstName => Length(100);
    public Col<string> LastName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> EnrolledAt { get; }

    // Navigations
    public Many<EnrollmentSchema> Enrollments => HasMany<EnrollmentSchema>(x => x.StudentId);

    // Indexes
    public Index IxStudentsEmail => Index(Email).Unique();
}
```

### Context class

A single `QuarryContext` subclass is generated with one `IEntityAccessor<T>` method per table:

```csharp
// Auto-scaffolded by quarry from database 'school' on 2026-03-27
// Review and adjust before using with quarry migrate

using Quarry;

namespace MyApp;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class SchoolDbContext : QuarryContext
{
    public partial IEntityAccessor<Course> Courses();
    public partial IEntityAccessor<Enrollment> Enrollments();
    public partial IEntityAccessor<Student> Students();
}
```

The context class name defaults to `{DatabaseName}DbContext`. Override it with `--context`.

## Automatic Detection

The scaffolder runs several heuristic passes after introspecting the raw schema.

### Junction tables (many-to-many)

A table is classified as a junction table when it has exactly two foreign keys whose columns together form the composite primary key (or a unique composite index), and at most two additional non-FK columns (e.g., `created_at`, `sort_order`). Junction tables are annotated with a comment identifying the two related entities.

### Implicit foreign keys by naming convention

Many databases -- especially SQLite -- lack formal `FOREIGN KEY` constraints even when columns clearly reference other tables. The scaffolder detects these by matching column names against known patterns:

| Pattern | Examples |
|---|---|
| `{table}_id` | `order_id`, `student_id` |
| `{Table}Id` | `OrderId`, `StudentId` |
| `{table}_fk` | `order_fk` |
| `{table}_key` | `order_key` |

The singularized form of the table name is also checked (`orders` matches `order_id`). Each candidate is scored:

- **+40** if the column name prefix exactly matches the target table name.
- **+30** if it matches the singularized table name.
- **+20** if the column's SQL type matches the target table's primary key type.
- **-30** if the column has a unique index (less likely to be an FK).
- **-20** if multiple tables match (ambiguity penalty).

Only candidates scoring 50 or above are surfaced. In interactive mode you decide per-candidate; in non-interactive mode all qualifying candidates are accepted automatically.

### Naming style

When you specify `--naming-style`, the scaffolder sets `NamingStyle` on each generated schema class so that Quarry maps PascalCase property names back to the original column names at query time. Column names like `user_name` become property `UserName`, and the `SnakeCase` naming style ensures the generated SQL still references `user_name`. If you omit `--naming-style`, the default `Exact` style is used and property names match column names verbatim.

## Interactive vs Non-Interactive Mode

By default, the scaffolder runs in **interactive mode** when a terminal is detected. Interactive mode prompts you for two kinds of decisions:

### Implicit FK acceptance

For each implicit foreign key candidate, you see the source column, target table, target column, and a confidence percentage. You choose one of:

- **Accept** -- include this FK as a `Ref<T, TKey>` property.
- **Skip** -- ignore this candidate.
- **Accept all >=80%** -- auto-accept every remaining candidate scoring 80% or higher; skip the rest.
- **Skip all implicit FKs** -- stop processing implicit FKs entirely.

### Ambiguous type resolution

When a SQL type is ambiguous (e.g., MySQL `TINYINT(1)` could be `bool` or `byte`, or `CHAR(36)` could be `string` or `Guid`), interactive mode asks which CLR type to use.

### Non-interactive mode (`--ni`)

Pass `--ni` to suppress all prompts. In this mode:

- All implicit FK candidates scoring >= 50 are accepted automatically.
- Ambiguous type mappings use the scaffolder's recommended default.

Use `--ni` in CI pipelines, scripts, or whenever you want a deterministic output you can review afterward.

## Example Workflow

This walkthrough scaffolds from an existing SQLite database, reviews the output, makes adjustments, and wires everything up.

### 1. Install the CLI tool

```sh
dotnet tool install --global Quarry.Tool
```

### 2. Scaffold

```sh
quarry scaffold -d sqlite --database school.db -o Schemas --namespace MyApp --naming-style SnakeCase
```

Output:

```
Connecting to sqlite database...
Introspecting database schema...
Found 4 table(s).
  Detected junction table: student_courses
  Created: Schemas/StudentSchema.cs
  Created: Schemas/CourseSchema.cs
  Created: Schemas/EnrollmentSchema.cs
  Created: Schemas/StudentCourseSchema.cs
  Created: Schemas/SchoolDbContext.cs

Scaffolded 5 file(s) to Schemas/
  1 junction table(s) detected
  2 implicit FK(s) accepted

Next steps:
  1. Review and adjust the generated schema files
  2. Run: quarry migrate add InitialBaseline
```

### 3. Review generated files

Open the schema files in `Schemas/`. Check that:

- Column types are correct (especially for SQLite, where everything is affinity-based).
- Implicit FKs were detected accurately. Remove any that are wrong; add any that were missed.
- Navigation properties point in the right direction.
- The naming style produces property names you are happy with.

### 4. Customize

Common adjustments after scaffolding:

```csharp
// Add a default that the scaffolder couldn't parse
public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);

// Mark a column as sensitive for log redaction
public Col<string> PasswordHash => Sensitive();

// Add a missing FK that the scaffolder didn't detect
public Ref<DepartmentSchema, int> DepartmentId => ForeignKey<DepartmentSchema, int>();

// Remove a navigation you don't need
// (delete the Many<T> property line)
```

### 5. Add packages and configure your project

```xml
<PackageReference Include="Quarry" Version="1.0.0" />
<PackageReference Include="Quarry.Generator" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />

<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp</InterceptorsNamespaces>
</PropertyGroup>
```

### 6. Query

```csharp
await using var db = new SchoolDbContext(connection);

var activeStudents = await db.Students()
    .Where(s => s.IsActive)
    .Select(s => (s.FirstName, s.LastName, s.Email))
    .OrderBy(s => s.LastName)
    .Limit(20)
    .ExecuteFetchAllAsync();
```

### 7. Baseline migration (optional)

If you want to track future schema changes with code-first migrations, create an initial baseline:

```sh
quarry migrate add InitialBaseline
```

This snapshots the current schema so that subsequent `migrate add` commands produce incremental diffs.
