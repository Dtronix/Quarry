using System.Threading.Tasks;
using MySqlConnector;
using Quarry;
using Quarry.Tests.Samples;
using MyDefault = Quarry.Tests.Samples.MyDefault;
using MyAnsi = Quarry.Tests.Samples.MyAnsi;

namespace Quarry.Tests.Integration;

/// <summary>
/// Integration regression for issue #273. Boots a MySQL 8.4 container with
/// stock <c>sql_mode</c> (no <c>NO_BACKSLASH_ESCAPES</c>) and runs the same
/// <c>Contains(...)</c> queries that previously threw 1064 against a real
/// default-mode MySQL server. Proves the generator's
/// <c>MySqlBackslashEscapes = true</c> emit path produces parseable SQL on
/// stock MySQL where backslash IS a string-literal escape character.
/// </summary>
[TestFixture]
[Category("MySqlIntegration")]
public class MySqlBackslashEscapesIntegrationTests
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await MySqlDefaultModeTestContainer.EnsureBaselineAsync();
    }

    /// <summary>
    /// Default-mode MySQL with attribute MySqlBackslashEscapes = true: the
    /// generator emits <c>LIKE '%user\\_name%' ESCAPE '\\'</c>. The MySQL
    /// parser collapses <c>\\</c> to a single backslash, leaving the LIKE
    /// pattern <c>%user\_name%</c> with escape character <c>\</c> — matches
    /// the literal underscore in <c>"user_name"</c>.
    /// </summary>
    [Test]
    public async Task Where_Contains_LiteralUnderscore_DefaultMode_ReturnsMatch()
    {
        var cs = await MySqlDefaultModeTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();
        await using var db = new MyDefault.MyDefaultDb(conn);

        var rows = await db.Users()
            .Where(u => u.UserName.Contains("user_name"))
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        // Seeded row 1 has UserName = "user_name". Row 2 ("50% off") and row 3
        // ("a\b") do NOT contain the literal substring "user_name", so the
        // metachar escape must be applied correctly to exclude them.
        Assert.That(rows, Is.EquivalentTo(new[] { 1 }));
    }

    /// <summary>
    /// The most subtle escape case: the user's literal contains an actual backslash.
    /// Pipeline: input <c>"a\b"</c> (1 backslash) → <c>EscapeLikeMetaChars</c> doubles to
    /// <c>"a\\b"</c> (2) → renderer doubles again for MySQL+default to <c>"a\\\\b"</c>
    /// (4 backslashes in SQL source) → MySQL parser collapses each <c>\\</c> to <c>\</c>,
    /// yielding the LIKE pattern <c>"a\\b"</c> (2 backslashes in the runtime pattern) →
    /// with <c>ESCAPE '\\'</c> (parsed: <c>\</c>), the LIKE evaluator interprets the second
    /// <c>\\</c> in the pattern as escape+<c>\</c> = literal <c>\</c>, matching <c>"a\b"</c>
    /// (1 literal backslash) in the column value.
    /// </summary>
    [Test]
    public async Task Where_Contains_LiteralBackslash_DefaultMode_ReturnsMatch()
    {
        var cs = await MySqlDefaultModeTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();
        await using var db = new MyDefault.MyDefaultDb(conn);

        var rows = await db.Users()
            .Where(u => u.UserName.Contains("a\\b"))
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(rows, Is.EquivalentTo(new[] { 3 }));
    }

    [Test]
    public async Task Where_Contains_LiteralPercent_DefaultMode_ReturnsMatch()
    {
        var cs = await MySqlDefaultModeTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();
        await using var db = new MyDefault.MyDefaultDb(conn);

        var rows = await db.Users()
            .Where(u => u.UserName.Contains("50%"))
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(rows, Is.EquivalentTo(new[] { 2 }));
    }

    /// <summary>
    /// Opt-out roundtrip: <c>MyAnsiSessionDb</c> has <c>MySqlBackslashEscapes = false</c>,
    /// so the generator emits ANSI single-backslash form (<c>'%user\_name%' ESCAPE '\'</c>).
    /// We boot the default-mode container (stock sql_mode = backslash IS an escape),
    /// then explicitly flip the session's <c>sql_mode</c> to add
    /// <c>NO_BACKSLASH_ESCAPES</c> on this connection only. With the session-level
    /// override, the ANSI form parses correctly and matches the expected row.
    /// Proves both directions of the carrier flag → emit path work end-to-end.
    /// </summary>
    [Test]
    public async Task Where_Contains_AnsiForm_NoBackslashEscapesSession_ReturnsMatch()
    {
        var cs = await MySqlDefaultModeTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();

        // Session-level override: switch THIS connection to NO_BACKSLASH_ESCAPES
        // sql_mode without affecting the container's server-wide mode (which other
        // tests in this fixture rely on for the default-mode behavior).
        await using (var setMode = conn.CreateCommand())
        {
            setMode.CommandText =
                "SET SESSION sql_mode = " +
                "'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE," +
                "ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,NO_BACKSLASH_ESCAPES'";
            await setMode.ExecuteNonQueryAsync();
        }

        await using var db = new MyAnsi.MyAnsiSessionDb(conn);

        var rows = await db.Users()
            .Where(u => u.UserName.Contains("user_name"))
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(rows, Is.EquivalentTo(new[] { 1 }));
    }

    /// <summary>
    /// Sanity probe: under default sql_mode the broken-on-#273 emit shape
    /// (single ESCAPE backslash, ANSI form) actually fails with 1064.
    /// Establishes the failure mode the generator fix avoids.
    /// </summary>
    [Test]
    public async Task RawSql_AnsiEscape_DefaultMode_Throws1064()
    {
        var cs = await MySqlDefaultModeTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT `UserId` FROM `users` WHERE `UserName` LIKE '%user\\_name%' ESCAPE '\\'";
        var ex = Assert.ThrowsAsync<MySqlException>(async () => await cmd.ExecuteReaderAsync());
        Assert.That(ex!.Number, Is.EqualTo(1064));
    }
}
