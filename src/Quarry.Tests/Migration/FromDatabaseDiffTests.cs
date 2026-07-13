using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Quarry.Shared.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Exercises the <c>--from-database</c> / adopt diff data path (steps 6 &amp; 8): introspect a live
/// database into a rich snapshot and diff it against the sparse snapshot the project-schema reader
/// produces. Verifies that <see cref="DatabaseSchemaReader.NormalizeForDiff"/> removes the metadata
/// asymmetry (identity/length/default/FK/index) so only real structural changes (renames, type
/// changes) surface.
/// </summary>
[TestFixture]
public class FromDatabaseDiffTests
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quarry_fromdb_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Legacy snake_case schema with an identity PK, a length, a default, and an index.
        cmd.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_name TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX ix_users_name ON users(user_name);";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // The desired PascalCase schema, built the sparse way ProjectSchemaReader does: only
    // name/clrType/nullable/kind are set (no identity/length/default/index metadata).
    private static SchemaSnapshot BuildSparseSchema(string idClrType, string activeClrType) =>
        new(2, "v2", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", idClrType, false, ColumnKind.PrimaryKey),
                    new ColumnDef("UserName", "string", false, ColumnKind.Standard),
                    new ColumnDef("IsActive", activeClrType, false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

    private async Task<SchemaSnapshot> IntrospectAsync()
    {
        var tables = await DatabaseSchemaReader.ReadTablesAsync("sqlite", _connectionString, null, null);
        return DatabaseSchemaReader.ToSnapshot(tables, "sqlite", 1, "FromDatabase");
    }

    [Test]
    public async Task RawDiff_WithoutNormalization_ProducesSpuriousAlterColumns()
    {
        // Demonstrates why normalization is needed: the rich DB snapshot vs the sparse schema
        // snapshot disagree on identity/length/default, so raw diffing over-reports AlterColumns.
        var db = await IntrospectAsync();
        var dbUsers = db.Tables.Single();
        var schema = BuildSparseSchema(dbUsers.Columns.Single(c => c.Name == "id").ClrType,
                                       dbUsers.Columns.Single(c => c.Name == "is_active").ClrType);

        var steps = SchemaDiffer.Diff(db, schema, _ => false);

        // The identity PK 'id' matches by name but differs on IsIdentity -> spurious AlterColumn.
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "id"), Is.True);
    }

    [Test]
    public async Task NormalizedDiff_EmitsOnlyRename_NoSpuriousAlterOrDropAdd()
    {
        var db = await IntrospectAsync();
        var dbUsers = db.Tables.Single();
        var schema = BuildSparseSchema(dbUsers.Columns.Single(c => c.Name == "id").ClrType,
                                       dbUsers.Columns.Single(c => c.Name == "is_active").ClrType);

        var steps = SchemaDiffer.Diff(
            DatabaseSchemaReader.NormalizeForDiff(db),
            DatabaseSchemaReader.NormalizeForDiff(schema));

        // user_name -> UserName and is_active -> IsActive are canonical renames.
        Assert.That(steps.Count(s => s.StepType == MigrationStepType.RenameColumn), Is.EqualTo(2));
        // No spurious alters (identity/length/default cleared), no drop+add, no index churn.
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType is MigrationStepType.DropIndex or MigrationStepType.AddIndex), Is.False);
    }

    [Test]
    public async Task NormalizedDiff_RealTypeChange_StillDetected()
    {
        // Normalization keeps ClrType, so a genuine type change is still surfaced as an alter/drop.
        var db = await IntrospectAsync();
        // Schema keeps user_name's name but changes its type to int (a real change, not a rename).
        var schema = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", db.Tables.Single().Columns.Single(c => c.Name == "id").ClrType, false, ColumnKind.PrimaryKey),
                    new ColumnDef("user_name", "int", false, ColumnKind.Standard),
                    new ColumnDef("is_active", "int", false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var steps = SchemaDiffer.Diff(
            DatabaseSchemaReader.NormalizeForDiff(db),
            DatabaseSchemaReader.NormalizeForDiff(schema));

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "user_name"), Is.True);
    }
}
