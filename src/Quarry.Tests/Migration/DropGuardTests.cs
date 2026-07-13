using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Quarry.Shared.Migration;
using Quarry.Shared.Sql;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for the data-loss <see cref="DropGuard"/> (step 7): destructive drops against
/// populated objects are reported as violations; empty objects and all-null columns are not.
/// </summary>
[TestFixture]
public class DropGuardTests
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quarry_dropguard_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT, note TEXT);
            INSERT INTO customers (name, note) VALUES ('Ada', NULL);
            INSERT INTO customers (name, note) VALUES ('Grace', NULL);

            CREATE TABLE empty_table (id INTEGER PRIMARY KEY, val TEXT);";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static MigrationStep Drop(MigrationStepType type, string table, string? column) =>
        new(type, StepClassification.Destructive, table, null, column, null, null, "drop");

    private async Task<System.Collections.Generic.IReadOnlyList<DropGuard.Violation>> FindAsync(params MigrationStep[] steps)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        return await DropGuard.FindViolationsAsync(conn, SqlDialect.SQLite, steps);
    }

    [Test]
    public async Task DropColumn_OnPopulatedColumn_IsViolation()
    {
        var v = await FindAsync(Drop(MigrationStepType.DropColumn, "customers", "name"));
        Assert.That(v.Count, Is.EqualTo(1));
        Assert.That(v[0].Column, Is.EqualTo("name"));
        Assert.That(v[0].RowCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DropColumn_OnAllNullColumn_IsNotViolation()
    {
        var v = await FindAsync(Drop(MigrationStepType.DropColumn, "customers", "note"));
        Assert.That(v, Is.Empty);
    }

    [Test]
    public async Task DropTable_OnPopulatedTable_IsViolation()
    {
        var v = await FindAsync(Drop(MigrationStepType.DropTable, "customers", null));
        Assert.That(v.Count, Is.EqualTo(1));
        Assert.That(v[0].Column, Is.Null);
        Assert.That(v[0].RowCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DropOnEmptyTable_IsNotViolation()
    {
        var v = await FindAsync(
            Drop(MigrationStepType.DropTable, "empty_table", null),
            Drop(MigrationStepType.DropColumn, "empty_table", "val"));
        Assert.That(v, Is.Empty);
    }

    [Test]
    public async Task NonDestructiveSteps_AreIgnored()
    {
        var v = await FindAsync(
            new MigrationStep(MigrationStepType.RenameColumn, StepClassification.Cautious, "customers", null, "name", "name", "FullName", "rename"),
            new MigrationStep(MigrationStepType.AddColumn, StepClassification.Safe, "customers", null, "age", null, null, "add"));
        Assert.That(v, Is.Empty);
    }

    // --- Schema qualification (F5): a normalized diff strips the schema, so the guard must
    //     re-qualify the drop with the live table's real schema on multi-schema dialects. ---

    [Test]
    public void FormatTable_NonSqliteDialect_QualifiesWithSchema()
    {
        var expected = $"{SqlFormatting.QuoteIdentifier(SqlDialect.PostgreSQL, "sales")}." +
                       $"{SqlFormatting.QuoteIdentifier(SqlDialect.PostgreSQL, "customers")}";
        Assert.That(DropGuard.FormatTable(SqlDialect.PostgreSQL, "sales", "customers"), Is.EqualTo(expected));
        // Without a schema it is just the (quoted) table.
        Assert.That(DropGuard.FormatTable(SqlDialect.PostgreSQL, null, "customers"),
            Is.EqualTo(SqlFormatting.QuoteIdentifier(SqlDialect.PostgreSQL, "customers")));
    }

    [Test]
    public void FormatTable_Sqlite_IgnoresSchema()
    {
        // SQLite has no schema qualifier; a supplied schema must be ignored (never mis-qualified).
        Assert.That(DropGuard.FormatTable(SqlDialect.SQLite, "sales", "customers"),
            Is.EqualTo(DropGuard.FormatTable(SqlDialect.SQLite, null, "customers")));
    }

    [Test]
    public void ResolveSchema_PrefersStepSchema_ThenLiveMap()
    {
        var live = new SchemaSnapshot(1, "db", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("customers", "sales", NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });
        var map = DropGuard.BuildTableSchemaMap(live);

        // Normalized step (schema == null) -> resolved from the live map.
        var stripped = new MigrationStep(MigrationStepType.DropTable, StepClassification.Destructive, "customers", null, null, null, null, "d");
        Assert.That(DropGuard.ResolveSchema(stripped, map), Is.EqualTo("sales"));

        // A step that already carries a schema keeps it (map is only a fallback).
        var explicitSchema = new MigrationStep(MigrationStepType.DropTable, StepClassification.Destructive, "customers", "audit", null, null, null, "d");
        Assert.That(DropGuard.ResolveSchema(explicitSchema, map), Is.EqualTo("audit"));

        // Unknown table with no step schema -> null (unqualified).
        var unknown = new MigrationStep(MigrationStepType.DropTable, StepClassification.Destructive, "orders", null, null, null, null, "d");
        Assert.That(DropGuard.ResolveSchema(unknown, map), Is.Null);
    }

    [Test]
    public void BuildTableSchemaMap_IsCaseInsensitiveOnTableName()
    {
        var live = new SchemaSnapshot(1, "db", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("Customers", "sales", NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });
        var map = DropGuard.BuildTableSchemaMap(live);
        Assert.That(map.TryGetValue("customers", out var schema), Is.True);
        Assert.That(schema, Is.EqualTo("sales"));
    }

    [Test]
    public async Task FindViolations_WithLiveSchemaMap_StillCountsOnSqlite()
    {
        // Wiring smoke test: passing a schema map must not change SQLite counting (schema ignored).
        var live = new SchemaSnapshot(1, "db", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("customers", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });
        var map = DropGuard.BuildTableSchemaMap(live);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var v = await DropGuard.FindViolationsAsync(conn, SqlDialect.SQLite,
            new[] { Drop(MigrationStepType.DropColumn, "customers", "name") }, map);
        Assert.That(v.Count, Is.EqualTo(1));
        Assert.That(v[0].RowCount, Is.EqualTo(2));
    }
}
