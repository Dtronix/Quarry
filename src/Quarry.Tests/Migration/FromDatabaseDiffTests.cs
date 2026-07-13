using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Quarry.Shared.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Exercises the <c>--from-database</c> data path (step 6): introspect a live database into
/// a snapshot and diff it against the desired schema. This composes the same units the CLI
/// wires together (DatabaseSchemaReader + SchemaDiffer) end-to-end against a real SQLite DB.
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
        // Legacy snake_case schema.
        cmd.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_name TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Test]
    public async Task DiffLiveDbAgainstPascalCaseSchema_EmitsRenameColumn_NotDropAdd()
    {
        // "from" = the live legacy database.
        var tables = await DatabaseSchemaReader.ReadTablesAsync("sqlite", _connectionString, null, null);
        var dbSnapshot = DatabaseSchemaReader.ToSnapshot(tables, "sqlite", 0, "FromDatabase");

        var dbUsers = dbSnapshot.Tables.Single(t => t.TableName == "users");
        var dbId = dbUsers.Columns.Single(c => c.Name == "id");
        var dbUserName = dbUsers.Columns.Single(c => c.Name == "user_name");

        // "to" = the desired PascalCase schema. Match the introspected id column exactly so the
        // only change is the user_name -> UserName rename.
        var schemaSnapshot = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", dbId.ClrType, dbId.IsNullable, ColumnKind.PrimaryKey, isIdentity: dbId.IsIdentity),
                    new ColumnDef("UserName", dbUserName.ClrType, dbUserName.IsNullable, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        // Default (null) callback — the always-on canonical pass must detect the rename.
        var steps = SchemaDiffer.Diff(dbSnapshot, schemaSnapshot);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn), Is.False);
    }
}
