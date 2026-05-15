using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable CS0162 // Unreachable code — boundary tests use if(true) literals intentionally

/// <summary>
/// Cross-dialect SQL-output coverage for the <c>ToAsyncEnumerable</c> streaming
/// terminal. The terminal flows through a different dispatch path than
/// <c>ToDiagnostics</c>/<c>ExecuteFetchAll</c>, so the SQL is asserted via a
/// parallel <c>ToDiagnostics()</c> chain (same clauses, different terminal) and
/// the streaming iteration is exercised separately to verify rows materialize.
/// </summary>
[TestFixture]
internal class CrossDialectStreamingTests
{
    [Test]
    public async Task ToAsyncEnumerable_BasicSelect_StreamsAllRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Parallel ToDiagnostics chains lock in the SQL emitted on the streaming path.
        QueryTestHarness.AssertDialects(
            Lite.Users().Select(u => u.UserName).ToDiagnostics(),
            Pg.Users().Select(u => u.UserName).ToDiagnostics(),
            My.Users().Select(u => u.UserName).ToDiagnostics(),
            Ss.Users().Select(u => u.UserName).ToDiagnostics(),
            sqlite: "SELECT \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserName` FROM `users`",
            ss:     "SELECT [UserName] FROM [users]");

        // Streaming materializes the same three seeded rows on every dialect.
        var liteNames = new List<string>();
        await foreach (var n in Lite.Users().Select(u => u.UserName).ToAsyncEnumerable())
            liteNames.Add(n);
        Assert.That(liteNames, Is.EquivalentTo(new[] { "Alice", "Bob", "Charlie" }), "SQLite");

        var pgNames = new List<string>();
        await foreach (var n in Pg.Users().Select(u => u.UserName).ToAsyncEnumerable())
            pgNames.Add(n);
        Assert.That(pgNames, Is.EquivalentTo(new[] { "Alice", "Bob", "Charlie" }), "PostgreSQL");

        var myNames = new List<string>();
        await foreach (var n in My.Users().Select(u => u.UserName).ToAsyncEnumerable())
            myNames.Add(n);
        Assert.That(myNames, Is.EquivalentTo(new[] { "Alice", "Bob", "Charlie" }), "MySQL");

        var ssNames = new List<string>();
        await foreach (var n in Ss.Users().Select(u => u.UserName).ToAsyncEnumerable())
            ssNames.Add(n);
        Assert.That(ssNames, Is.EquivalentTo(new[] { "Alice", "Bob", "Charlie" }), "SQL Server");
    }

    [Test]
    public async Task ToAsyncEnumerable_BreakAfterFirst_YieldsOrderedFirstRow()
    {
        // Verifies `break` inside `await foreach` over `ToAsyncEnumerable()` is
        // well-formed and surfaces the OrderBy'd first row across all four
        // dialects. Note: this is a behavioral assertion on what the consumer
        // observes, not a proof of streaming vs. buffering — the underlying
        // implementation could materialize all rows and still pass. A true
        // streaming proof would require observing reader-side row reads (see
        // CrossDialectLoggingTests for the logger-based pattern).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        await AssertFirstRowIsAliceAsync(Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQLite");
        await AssertFirstRowIsAliceAsync(Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "PostgreSQL");
        await AssertFirstRowIsAliceAsync(My.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "MySQL");
        await AssertFirstRowIsAliceAsync(Ss.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQL Server");
    }

    private static async Task AssertFirstRowIsAliceAsync(IAsyncEnumerable<string> source, string label)
    {
        var seen = new List<string>();
        await foreach (var n in source)
        {
            seen.Add(n);
            break;
        }
        Assert.That(seen, Has.Count.EqualTo(1), $"{label}: enumeration should surface exactly one row before break");
        Assert.That(seen[0], Is.EqualTo("Alice"), $"{label}: ordered first row should be Alice");
    }

    [Test]
    public async Task ToAsyncEnumerable_ConditionalWhere_RendersConditionalSql()
    {
        // Conditional clauses (if (true) query = query.Where(...)) feed into the
        // streaming terminal's prebuilt-mask dispatch the same way they do for
        // ToDiagnostics. Both terminals share the clause analysis, so the SQL
        // emitted for streaming must match the SQL surfaced by ToDiagnostics.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (true) { lt = lt.Where(u => u.IsActive); }
        if (true) { pg = pg.Where(u => u.IsActive); }
        if (true) { my = my.Where(u => u.IsActive); }
        if (true) { ss = ss.Where(u => u.IsActive); }

        QueryTestHarness.AssertDialects(
            lt.Select(u => u.UserName).ToDiagnostics(),
            pg.Select(u => u.UserName).ToDiagnostics(),
            my.Select(u => u.UserName).ToDiagnostics(),
            ss.Select(u => u.UserName).ToDiagnostics(),
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserName] FROM [users] WHERE [IsActive] = 1");

        // Streaming surfaces only the active users (Alice + Bob; Charlie is inactive).
        IQueryBuilder<User> ltStream = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pgStream = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> myStream = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ssStream = Ss.Users().Where(u => true);

        if (true) { ltStream = ltStream.Where(u => u.IsActive); }
        if (true) { pgStream = pgStream.Where(u => u.IsActive); }
        if (true) { myStream = myStream.Where(u => u.IsActive); }
        if (true) { ssStream = ssStream.Where(u => u.IsActive); }

        var liteActive = new List<string>();
        await foreach (var n in ltStream.Select(u => u.UserName).ToAsyncEnumerable())
            liteActive.Add(n);
        Assert.That(liteActive, Is.EquivalentTo(new[] { "Alice", "Bob" }), "SQLite");

        var pgActive = new List<string>();
        await foreach (var n in pgStream.Select(u => u.UserName).ToAsyncEnumerable())
            pgActive.Add(n);
        Assert.That(pgActive, Is.EquivalentTo(new[] { "Alice", "Bob" }), "PostgreSQL");

        var myActive = new List<string>();
        await foreach (var n in myStream.Select(u => u.UserName).ToAsyncEnumerable())
            myActive.Add(n);
        Assert.That(myActive, Is.EquivalentTo(new[] { "Alice", "Bob" }), "MySQL");

        var ssActive = new List<string>();
        await foreach (var n in ssStream.Select(u => u.UserName).ToAsyncEnumerable())
            ssActive.Add(n);
        Assert.That(ssActive, Is.EquivalentTo(new[] { "Alice", "Bob" }), "SQL Server");
    }
}
