using Microsoft.Data.Sqlite;
using Quarry;
using Quarry.Migration;

// =========================================================================
// Quarry Migration Scripting Sample
//
// A minimal example showing how to use tool-generated migrations.
// Run `./migrate.sh` to generate the initial migration from the schemas,
// then `dotnet run` to apply it against a local SQLite database.
// =========================================================================

var dbPath = Path.Combine(AppContext.BaseDirectory, "sample.db");
var connectionString = $"Data Source={dbPath}";

using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();

var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);

// Migration definitions — wired to generated classes in Migrations/ folder.
// After running `./migrate.sh`, add entries here matching the generated classes.
var migrations = Array.Empty<(int Version, string Name,
    Action<MigrationBuilder> Upgrade,
    Action<MigrationBuilder> Downgrade,
    Action<MigrationBuilder> Backup)>();

if (migrations.Length == 0)
{
    Console.WriteLine("No migrations registered yet.");
    Console.WriteLine("Run ./migrate.sh to generate the initial migration,");
    Console.WriteLine("then update Program.cs to reference the generated classes.");
    return;
}

// Apply all pending migrations
await MigrationRunner.RunAsync(connection, dialect, migrations);

// Show result
using var cmd = connection.CreateCommand();

cmd.CommandText = "SELECT version, name, applied_at FROM __quarry_migrations ORDER BY version;";
using var reader = await cmd.ExecuteReaderAsync();

Console.WriteLine("Applied migrations:");
Console.WriteLine($"  {"Ver",-5} {"Name",-30} {"Applied At"}");
Console.WriteLine($"  {"---",-5} {"----",-30} {"----------"}");

while (await reader.ReadAsync())
{
    Console.WriteLine($"  {reader.GetInt32(0),-5} {reader.GetString(1),-30} {reader.GetString(2)}");
}

Console.WriteLine();
Console.WriteLine("Tables:");

cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
using var tableReader = await cmd.ExecuteReaderAsync();

while (await tableReader.ReadAsync())
{
    var table = tableReader.GetString(0);
    Console.Write($"  {table}");

    using var colCmd = connection.CreateCommand();
    colCmd.CommandText = $"PRAGMA table_info(\"{table}\");";
    using var colReader = await colCmd.ExecuteReaderAsync();

    var cols = new List<string>();
    while (await colReader.ReadAsync())
        cols.Add(colReader.GetString(1));

    Console.WriteLine($" ({string.Join(", ", cols)})");
}
