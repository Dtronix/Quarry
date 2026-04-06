// ============================================================================
// Quarry.Migration Sample: Dapper → Quarry Conversion
//
// This sample demonstrates the Quarry.Migration tooling:
//
// 1. SCHEMA DEFINITIONS (Data/Schemas.cs)
//    Quarry entity schemas that map to the database tables.
//    These must exist before the migration tool can convert Dapper calls.
//
// 2. DAPPER CALLS (DapperQueries.cs)
//    Typical Dapper usage patterns. Open this file in your IDE to see
//    QRM001 lightbulb diagnostics offering to convert each call.
//
// 3. CLI SCAN
//    Run: quarry convert --from dapper -p src/Samples/4_DapperMigration
//    This scans the project and reports all convertible Dapper calls.
//
// 4. IDE CODE FIX
//    With Quarry.Migration referenced as an analyzer, click the lightbulb
//    on any QRM001 diagnostic to replace the Dapper call with a Quarry
//    chain API call automatically.
//
// ============================================================================

using Microsoft.Data.Sqlite;
using DapperMigration;

Console.WriteLine("Quarry.Migration Sample: Dapper → Quarry Conversion");
Console.WriteLine("====================================================");
Console.WriteLine();
Console.WriteLine("This project contains Dapper calls in DapperQueries.cs.");
Console.WriteLine("Open it in your IDE to see QRM001 diagnostics, or run:");
Console.WriteLine("  quarry convert --from dapper -p src/Samples/4_DapperMigration");
Console.WriteLine();

// Quick demo: create an in-memory database and run a Dapper query
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

// Create tables
await using var cmd = connection.CreateCommand();
cmd.CommandText = """
    CREATE TABLE users (
        user_id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_name TEXT NOT NULL,
        email TEXT NOT NULL,
        is_active INTEGER NOT NULL DEFAULT 1,
        created_at TEXT NOT NULL DEFAULT (datetime('now'))
    );
    INSERT INTO users (user_name, email) VALUES ('alice', 'alice@example.com');
    INSERT INTO users (user_name, email) VALUES ('bob', 'bob@example.com');
    INSERT INTO users (user_name, email, is_active) VALUES ('charlie', 'charlie@example.com', 0);
    """;
await cmd.ExecuteNonQueryAsync();

// Run Dapper queries
var queries = new DapperQueries(connection);

var allUsers = await queries.GetAllUsers();
Console.WriteLine($"All users: {string.Join(", ", allUsers.Select(u => u.UserName))}");

var activeUsers = await queries.GetActiveUsers();
Console.WriteLine($"Active users: {string.Join(", ", activeUsers.Select(u => u.UserName))}");

var alice = await queries.GetUserById(1);
Console.WriteLine($"User #1: {alice.UserName} ({alice.Email})");

var searched = await queries.SearchUsers("%ali%");
Console.WriteLine($"Search '%ali%': {string.Join(", ", searched.Select(u => u.UserName))}");

Console.WriteLine();
Console.WriteLine("Each of these Dapper calls has a Quarry equivalent.");
Console.WriteLine("See QRM001 diagnostics in your IDE for conversion suggestions.");
