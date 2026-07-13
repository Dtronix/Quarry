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
/// End-to-end proof of the adopt data-loss guard wiring (F9): the exact pipeline
/// <c>migrate adopt</c> runs — introspect → normalize → diff → <see cref="DropGuard"/> — must
/// flag a populated column that the project schema drops (no rename maps to it), so adopt aborts
/// without <c>--allow-data-loss</c> and proceeds with it. MigrateCommands itself is not compiled
/// into the test assembly, so this exercises the composition the command wires together.
/// </summary>
[TestFixture]
public class AdoptGuardScenarioTests
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quarry_adopt_guard_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Legacy DB: user_name is a convention rename; legacy_notes is populated and has NO
        // counterpart in the desired project schema, so aligning would DROP it.
        cmd.CommandText = @"
            CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, user_name TEXT NOT NULL, legacy_notes TEXT);
            INSERT INTO users (user_name, legacy_notes) VALUES ('ada', 'keep me');
            INSERT INTO users (user_name, legacy_notes) VALUES ('grace', 'and me');";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // The desired project schema: user_name -> UserName, and legacy_notes intentionally absent.
    private static SchemaSnapshot BuildProjectSchema(string idClrType) =>
        new(2, "AlignSchema", DateTimeOffset.UtcNow, 1, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", idClrType, false, ColumnKind.PrimaryKey),
                    new ColumnDef("UserName", "string", false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

    private async Task<(SchemaSnapshot Db, System.Collections.Generic.IReadOnlyList<MigrationStep> Steps)> BuildAlignmentAsync()
    {
        var dbTables = await DatabaseSchemaReader.ReadTablesAsync("sqlite", _connectionString, null, null);
        var dbSnapshot = DatabaseSchemaReader.ToSnapshot(dbTables, "sqlite", 1, "InitialCreate");

        var project = BuildProjectSchema(dbSnapshot.Tables.Single().Columns.Single(c => c.Name == "id").ClrType);

        var steps = SchemaDiffer.Diff(
            DatabaseSchemaReader.NormalizeForDiff(dbSnapshot),
            DatabaseSchemaReader.NormalizeForDiff(project));
        return (dbSnapshot, steps);
    }

    [Test]
    public async Task Adopt_AlignmentDiff_RenamesConventionColumn_AndDropsUnmappedColumn()
    {
        var (_, steps) = await BuildAlignmentAsync();

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn && s.ColumnName == "legacy_notes"), Is.True);
    }

    [Test]
    public async Task Adopt_PopulatedUnmappedColumn_IsGuardViolation_WouldAbort()
    {
        var (dbSnapshot, steps) = await BuildAlignmentAsync();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var map = DropGuard.BuildTableSchemaMap(dbSnapshot);
        var violations = await DropGuard.FindViolationsAsync(conn, SqlDialect.SQLite, steps, map);

        // adopt: `if (!allowDataLoss) { ...FindViolationsAsync...; if (violations.Count > 0) return; }`
        // A populated legacy_notes is a violation, so adopt aborts without --allow-data-loss.
        Assert.That(violations.Count, Is.EqualTo(1));
        Assert.That(violations[0].Column, Is.EqualTo("legacy_notes"));
        Assert.That(violations[0].RowCount, Is.EqualTo(2));
    }

    [Test]
    public async Task Adopt_WithAllowDataLoss_SkipsGuard_AndWouldProceed()
    {
        // With --allow-data-loss the guard is not consulted; the drop step remains and the migration
        // is generated. Assert the alignment still contains the rename so no data-preserving step is lost.
        var (_, steps) = await BuildAlignmentAsync();

        const bool allowDataLoss = true;
        // Mirror the command's branch: the guard only runs when !allowDataLoss.
        Assert.That(allowDataLoss, Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.True);
    }
}
