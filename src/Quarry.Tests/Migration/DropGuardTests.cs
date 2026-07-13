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
}
