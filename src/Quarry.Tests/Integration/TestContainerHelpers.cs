using System;
using System.Threading.Tasks;
using MySqlConnector;

namespace Quarry.Tests.Integration;

/// <summary>
/// Shared plumbing for the MySQL Testcontainers helpers. Both
/// <see cref="MySqlTestContainer"/> and <see cref="MySqlDefaultModeTestContainer"/>
/// boot a MySQL 8.4 container and need the same Docker-availability heuristics
/// and small SQL-helpers; this class de-duplicates that.
/// </summary>
internal static class TestContainerHelpers
{
    /// <summary>
    /// Heuristic for "Docker is not installed / not running" — Testcontainers
    /// surfaces a handful of different exception types depending on which probe
    /// failed. Used by all container helpers to decide whether to skip rather
    /// than fail when Docker is unavailable.
    /// </summary>
    public static bool IsDockerUnavailable(Exception ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException!)
        {
            var typeName = cur.GetType().FullName ?? "";
            var message = cur.Message ?? "";
            if (typeName.Contains("Docker", StringComparison.Ordinal) ||
                typeName.Contains("Testcontainers", StringComparison.Ordinal))
                return true;
            if (message.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("daemon", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("named pipe", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cur.InnerException is null) break;
        }
        return false;
    }

    /// <summary>
    /// Returns true when a table named <paramref name="table"/> exists in
    /// <paramref name="database"/> on the given MySQL connection.
    /// </summary>
    public static async Task<bool> TableExistsAsync(MySqlConnection conn, string database, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM information_schema.tables WHERE table_schema = @db AND table_name = @tb";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@db"; p1.Value = database; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@tb"; p2.Value = table;    cmd.Parameters.Add(p2);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    /// <summary>
    /// Executes a non-query SQL statement against the given MySQL connection.
    /// Wraps the create-command/dispose boilerplate so callers stay focused on
    /// the SQL text.
    /// </summary>
    public static async Task ExecAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
