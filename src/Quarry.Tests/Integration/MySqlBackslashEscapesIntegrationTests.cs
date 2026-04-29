using System.Threading.Tasks;
using MySqlConnector;
using Quarry;
using Quarry.Tests.Samples;
using MyDefault = Quarry.Tests.Samples.MyDefault;

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
