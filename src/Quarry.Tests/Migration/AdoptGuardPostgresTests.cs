using System;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using Quarry.Shared.Migration;
using Quarry.Shared.Sql;
using Quarry.Tests.Integration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Real-PostgreSQL proof of the multi-schema drop-guard fix (F5). A normalized adopt diff strips
/// the schema qualifier from its steps, so the guard must re-qualify each drop with the live
/// table's real schema (from <see cref="DropGuard.BuildTableSchemaMap"/>). This test places the
/// table in a non-default schema and runs the guard on a connection whose <c>search_path</c> does
/// NOT include it: only the schema-qualified query resolves the table. Without the fix the guard
/// would query an unqualified name and fail to find (or mis-count) the populated column.
/// </summary>
[TestFixture]
[Category("NpgsqlIntegration")]
public class AdoptGuardPostgresTests
{
    private string _connectionString = null!;
    private NpgsqlConnection _connection = null!;
    private string _schema = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connectionString = await PostgresTestContainer.GetConnectionStringAsync();
        _connection = new NpgsqlConnection(_connectionString);
        await _connection.OpenAsync();

        _schema = "adopttest_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        await using var create = _connection.CreateCommand();
        create.CommandText = $@"
            CREATE SCHEMA ""{_schema}"";
            CREATE TABLE ""{_schema}"".users (
                id SERIAL PRIMARY KEY,
                user_name TEXT NOT NULL,
                legacy_notes TEXT
            );
            INSERT INTO ""{_schema}"".users (user_name, legacy_notes) VALUES ('ada', 'keep me');
            INSERT INTO ""{_schema}"".users (user_name, legacy_notes) VALUES ('grace', 'and me');";
        await create.ExecuteNonQueryAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_connection.State == System.Data.ConnectionState.Open)
        {
            try
            {
                await using var drop = _connection.CreateCommand();
                drop.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;";
                await drop.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"[AdoptGuardPostgresTests] DROP SCHEMA {_schema} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        await _connection.DisposeAsync();
    }

    private async Task<(SchemaSnapshot Db, System.Collections.Generic.IReadOnlyList<MigrationStep> Steps)> BuildAlignmentAsync()
    {
        // Introspect only our schema, so the snapshot carries SchemaName = _schema.
        var dbTables = await DatabaseSchemaReader.ReadTablesAsync("postgresql", _connectionString, _schema, null);
        var dbSnapshot = DatabaseSchemaReader.ToSnapshot(dbTables, "postgresql", 1, "InitialCreate");

        var idType = dbSnapshot.Tables.Single().Columns.Single(c => c.Name == "id").ClrType;
        var project = new SchemaSnapshot(2, "AlignSchema", DateTimeOffset.UtcNow, 1, new[]
        {
            new TableDef("users", _schema, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", idType, false, ColumnKind.PrimaryKey),
                    new ColumnDef("UserName", "string", false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var steps = SchemaDiffer.Diff(
            DatabaseSchemaReader.NormalizeForDiff(dbSnapshot),
            DatabaseSchemaReader.NormalizeForDiff(project));
        return (dbSnapshot, steps);
    }

    [Test]
    public async Task DropGuard_ReQualifiesSchema_FindsPopulatedColumnInNonDefaultSchema()
    {
        var (dbSnapshot, steps) = await BuildAlignmentAsync();
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn && s.ColumnName == "legacy_notes"), Is.True);

        // Fresh connection: default search_path (public) does NOT include _schema, so only a
        // schema-qualified query can resolve the table.
        await using var guardConn = new NpgsqlConnection(_connectionString);
        await guardConn.OpenAsync();

        var map = DropGuard.BuildTableSchemaMap(dbSnapshot);
        var violations = await DropGuard.FindViolationsAsync(guardConn, SqlDialect.PostgreSQL, steps, map);

        Assert.That(violations.Count, Is.EqualTo(1));
        Assert.That(violations[0].Column, Is.EqualTo("legacy_notes"));
        Assert.That(violations[0].RowCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DropGuard_WithoutSchemaMap_CannotResolveTable_InNonDefaultSchema()
    {
        // Negative control: without the schema map the guard queries the unqualified name, which
        // does not exist under the default search_path — demonstrating why F5 needed the fix.
        var (_, steps) = await BuildAlignmentAsync();

        await using var guardConn = new NpgsqlConnection(_connectionString);
        await guardConn.OpenAsync();

        Assert.That(async () => await DropGuard.FindViolationsAsync(guardConn, SqlDialect.PostgreSQL, steps),
            Throws.Exception);
    }
}
