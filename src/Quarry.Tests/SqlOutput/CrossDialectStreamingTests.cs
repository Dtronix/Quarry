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

    /// <summary>
    /// Abandoning a stream must release the reader and command. The iterator
    /// disposes them through <c>await using</c> declarations in its body, but
    /// <c>FinalizeQuery</c> only runs on natural completion, so the early-break
    /// path had no coverage at all.
    /// </summary>
    /// <remarks>
    /// A follow-up query on the same connection is the assertion that matters:
    /// the harness holds one long-lived connection per dialect, and a leaked
    /// reader poisons it — MySqlConnector refuses a second command while a
    /// reader is open, and SqlClient does the same without MARS. If disposal
    /// regressed, this fails at the follow-up rather than at the break.
    /// </remarks>
    [Test]
    public async Task ToAsyncEnumerable_AbandonedAfterFirstRow_LeavesConnectionUsable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        await ConsumeFirstRowThenBreakAsync(
            Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQLite");
        var liteAfter = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(liteAfter, Has.Count.EqualTo(3), "SQLite: query after abandoned stream");

        await ConsumeFirstRowThenBreakAsync(
            Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "PostgreSQL");
        var pgAfter = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(pgAfter, Has.Count.EqualTo(3), "PostgreSQL: query after abandoned stream");

        await ConsumeFirstRowThenBreakAsync(
            My.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "MySQL");
        var myAfter = await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(myAfter, Has.Count.EqualTo(3), "MySQL: query after abandoned stream");

        await ConsumeFirstRowThenBreakAsync(
            Ss.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQL Server");
        var ssAfter = await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ssAfter, Has.Count.EqualTo(3), "SQL Server: query after abandoned stream");
    }

    /// <summary>
    /// The same abandonment via an explicitly driven enumerator rather than
    /// <c>await foreach</c>'s generated disposal, so the iterator's own
    /// <c>DisposeAsync</c> is what releases the reader.
    /// </summary>
    [Test]
    public async Task ToAsyncEnumerable_EnumeratorDisposedEarly_LeavesConnectionUsable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        await DisposeEnumeratorAfterFirstRowAsync(
            Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQLite");
        var liteAfter = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(liteAfter, Has.Count.EqualTo(3), "SQLite: query after disposed enumerator");

        await DisposeEnumeratorAfterFirstRowAsync(
            Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "PostgreSQL");
        var pgAfter = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(pgAfter, Has.Count.EqualTo(3), "PostgreSQL: query after disposed enumerator");

        await DisposeEnumeratorAfterFirstRowAsync(
            My.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "MySQL");
        var myAfter = await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(myAfter, Has.Count.EqualTo(3), "MySQL: query after disposed enumerator");

        await DisposeEnumeratorAfterFirstRowAsync(
            Ss.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQL Server");
        var ssAfter = await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ssAfter, Has.Count.EqualTo(3), "SQL Server: query after disposed enumerator");
    }

    /// <summary>
    /// Tears the harness down with streams abandoned on all four dialects and
    /// asserts the PostgreSQL / MySQL / SQL Server rollbacks still complete.
    /// </summary>
    /// <remarks>
    /// This covers teardown, not disposal: forcing a reader leak leaves this
    /// test passing while the two above fail, because the providers tolerate a
    /// rollback with a reader outstanding. The follow-up-query tests are what
    /// detect a leak; this one guards against abandonment breaking teardown for
    /// some other reason.
    /// </remarks>
    [Test]
    public async Task ToAsyncEnumerable_AbandonedStreams_DoNotBlockHarnessRollback()
    {
        var t = await QueryTestHarness.CreateAsync();
        var disposed = false;
        try
        {
            var (Lite, Pg, My, Ss) = t;

            await ConsumeFirstRowThenBreakAsync(
                Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQLite");
            await ConsumeFirstRowThenBreakAsync(
                Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "PostgreSQL");
            await ConsumeFirstRowThenBreakAsync(
                My.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "MySQL");
            await ConsumeFirstRowThenBreakAsync(
                Ss.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(), "SQL Server");

            Assert.DoesNotThrowAsync(async () =>
            {
                await t.DisposeAsync();
                disposed = true;
            }, "Harness rollback must not be blocked by an abandoned stream");
        }
        finally
        {
            if (!disposed)
            {
                try { await t.DisposeAsync(); } catch { /* teardown already reported above */ }
            }
        }
    }

    private static async Task ConsumeFirstRowThenBreakAsync(IAsyncEnumerable<string> source, string label)
    {
        var seen = 0;
        await foreach (var n in source)
        {
            _ = n;
            seen++;
            break;
        }
        Assert.That(seen, Is.EqualTo(1), $"{label}: expected one row before abandoning the stream");
    }

    private static async Task DisposeEnumeratorAfterFirstRowAsync(IAsyncEnumerable<string> source, string label)
    {
        var enumerator = source.GetAsyncEnumerator();
        try
        {
            Assert.That(await enumerator.MoveNextAsync(), Is.True, $"{label}: expected a first row");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
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
