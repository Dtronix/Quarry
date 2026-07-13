using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Quarry.Migration;
using Quarry.Shared.Sql;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// End-to-end proof of the adopt guarantee (step 8): an existing populated database is
/// baselined (its InitialCreate recorded as applied and skipped), and the pending alignment
/// migration's column rename is applied WITHOUT losing data. This composes the exact runtime
/// mechanics the <c>migrate adopt</c> command wires together.
/// </summary>
[TestFixture]
public class AdoptScenarioTests
{
    [Test]
    public async Task Adopt_BaselineSkipped_RenameApplied_DataPreserved()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        // An existing legacy database with data (snake_case column).
        using (var seed = conn.CreateCommand())
        {
            seed.CommandText = @"
                CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, user_name TEXT NOT NULL);
                INSERT INTO users (user_name) VALUES ('ada');
                INSERT INTO users (user_name) VALUES ('grace');";
            await seed.ExecuteNonQueryAsync();
        }

        // adopt step: record v1 (InitialCreate) as applied without running it.
        await MigrationHistoryWriter.EnsureHistoryTableAsync(conn, SqlDialect.SQLite);
        await MigrationHistoryWriter.MarkAppliedAsync(conn, SqlDialect.SQLite, 1, "InitialCreate", "baseline");

        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            // v1 would CREATE the (already-existing) table — must be skipped, or it errors.
            (1, "InitialCreate",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("user_name", c => c.ClrType("string").NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            // v2 is the pending alignment: rename the column. Must apply and preserve data.
            (2, "AlignSchema",
                b => b.RenameColumn("users", "user_name", "UserName"),
                b => b.RenameColumn("users", "UserName", "user_name"),
                _ => { })
        };

        await MigrationRunner.RunAsync(conn, SqlDialect.SQLite, migrations);

        // The rename was applied and no rows were lost.
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT UserName FROM users ORDER BY id;";
        var names = new List<string>();
        using (var reader = await check.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
        }

        Assert.That(names, Is.EqualTo(new[] { "ada", "grace" }));
    }
}
