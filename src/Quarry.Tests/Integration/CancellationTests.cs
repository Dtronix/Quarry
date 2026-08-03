using System.Threading.Tasks;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

/// <summary>
/// Runtime cancellation coverage. Every terminal accepts a
/// <see cref="CancellationToken"/> and every executor deliberately excludes
/// <see cref="OperationCanceledException"/> from the catch that wraps failures
/// (<c>when (ex is not OperationCanceledException)</c>), so a cancelled
/// operation is supposed to surface OCE unwrapped. None of that was exercised:
/// the only tokens in the suite were <c>CancellationToken.None</c> placeholders
/// and generator-side signature detection.
/// </summary>
/// <remarks>
/// <para>
/// Cancelling is only half the assertion. The harness holds one long-lived
/// connection per dialect, so a terminal that abandons an open reader on the
/// cancellation path poisons that connection for everything after it. Each
/// test therefore runs a normal query afterwards on the same connection —
/// the same shape that gives the streaming disposal tests their teeth.
/// </para>
/// <para>
/// Operations are passed to the assertion helper already started, rather than
/// as lambdas: a Quarry chain written inside a lambda nested in another lambda
/// emits an interceptor that does not compile (see the step-8 note in
/// <c>workflow.md</c>), and passing the <see cref="Task"/> keeps every chain in
/// ordinary method scope.
/// </para>
/// </remarks>
[TestFixture]
internal class CancellationTests
{
    /// <summary>
    /// Awaits an already-started operation and asserts it faulted with
    /// <see cref="OperationCanceledException"/> rather than completing or
    /// throwing something the caller cannot distinguish from a real failure.
    /// </summary>
    private static async Task AssertCancelledAsync(Task operation, string label)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"{label}: expected OperationCanceledException, got {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Assert.Fail($"{label}: expected OperationCanceledException, but the operation completed");
    }

    /// <summary>
    /// A pre-cancelled token into every fetch terminal. Covers the full
    /// terminal surface on SQLite and PostgreSQL; MySQL and SQL Server get the
    /// list terminal, which is enough to catch a provider-specific cancellation
    /// path breaking.
    /// </summary>
    [Test]
    public async Task PreCancelledToken_EveryFetchTerminal_ThrowsOperationCanceled()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var ct = cts.Token;

        await AssertCancelledAsync(
            Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync(ct), "SQLite FetchAll");
        await AssertCancelledAsync(
            Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ExecuteFetchFirstAsync(ct),
            "SQLite FetchFirst");
        await AssertCancelledAsync(
            Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ExecuteFetchFirstOrDefaultAsync(ct),
            "SQLite FetchFirstOrDefault");
        await AssertCancelledAsync(
            Lite.Users().Where(u => u.UserId == 1).Select(u => u.UserName).ExecuteFetchSingleAsync(ct),
            "SQLite FetchSingle");
        await AssertCancelledAsync(
            Lite.Users().Where(u => u.UserId == 1).Select(u => u.UserName).ExecuteFetchSingleOrDefaultAsync(ct),
            "SQLite FetchSingleOrDefault");
        await AssertCancelledAsync(
            Lite.Users().Where(u => u.UserId == 1).Select(u => u.UserId).ExecuteScalarAsync<int>(ct),
            "SQLite Scalar");
        await AssertCancelledAsync(
            Lite.Users().Update().Set(u => u.UserName = "cancelled").Where(u => u.UserId == 1).ExecuteNonQueryAsync(ct),
            "SQLite NonQuery");

        await AssertCancelledAsync(
            Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync(ct), "PostgreSQL FetchAll");
        await AssertCancelledAsync(
            Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ExecuteFetchFirstAsync(ct),
            "PostgreSQL FetchFirst");
        await AssertCancelledAsync(
            Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ExecuteFetchFirstOrDefaultAsync(ct),
            "PostgreSQL FetchFirstOrDefault");
        await AssertCancelledAsync(
            Pg.Users().Where(u => u.UserId == 1).Select(u => u.UserName).ExecuteFetchSingleAsync(ct),
            "PostgreSQL FetchSingle");
        await AssertCancelledAsync(
            Pg.Users().Where(u => u.UserId == 1).Select(u => u.UserName).ExecuteFetchSingleOrDefaultAsync(ct),
            "PostgreSQL FetchSingleOrDefault");
        await AssertCancelledAsync(
            Pg.Users().Where(u => u.UserId == 1).Select(u => u.UserId).ExecuteScalarAsync<int>(ct),
            "PostgreSQL Scalar");
        await AssertCancelledAsync(
            Pg.Users().Update().Set(u => u.UserName = "cancelled").Where(u => u.UserId == 1).ExecuteNonQueryAsync(ct),
            "PostgreSQL NonQuery");

        await AssertCancelledAsync(
            My.Users().Select(u => u.UserName).ExecuteFetchAllAsync(ct), "MySQL FetchAll");
        await AssertCancelledAsync(
            Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync(ct), "SQL Server FetchAll");

        // Nothing should have been executed, and every connection must still work.
        var lite = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        var pg = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        var my = await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        var ss = await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(lite, Has.Count.EqualTo(3), "SQLite usable after cancellation");
            Assert.That(pg, Has.Count.EqualTo(3), "PostgreSQL usable after cancellation");
            Assert.That(my, Has.Count.EqualTo(3), "MySQL usable after cancellation");
            Assert.That(ss, Has.Count.EqualTo(3), "SQL Server usable after cancellation");
            Assert.That(lite, Does.Not.Contain("cancelled"), "cancelled UPDATE must not have been applied");
        });
    }

    /// <summary>
    /// Cancelling part-way through a stream must leave the connection usable on
    /// every dialect: whether or not the provider surfaces the cancellation, the
    /// reader and command still have to be released.
    /// </summary>
    /// <remarks>
    /// This does not assert that OCE is thrown — see
    /// <see cref="MidStreamCancellation_SurfacesOperationCanceled_WhenProviderAwaitsIo"/>
    /// for why that is not universal.
    /// </remarks>
    [Test]
    public async Task MidStreamCancellation_LeavesConnectionUsable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // A fresh token source per dialect: a cancelled token stays cancelled.
        // The chain also has to terminate at the call site — handing a partial
        // builder to a helper leaves the accessor call unintercepted and throws
        // NotSupportedException at runtime.
        using var liteCts = new CancellationTokenSource();
        await CancelAfterFirstRowAsync(
            Lite.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(liteCts.Token),
            liteCts, "SQLite");
        var lite = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(lite, Has.Count.EqualTo(3), "SQLite usable after mid-stream cancellation");

        using var pgCts = new CancellationTokenSource();
        await CancelAfterFirstRowAsync(
            Pg.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(pgCts.Token),
            pgCts, "PostgreSQL");
        var pg = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(pg, Has.Count.EqualTo(3), "PostgreSQL usable after mid-stream cancellation");

        using var myCts = new CancellationTokenSource();
        await CancelAfterFirstRowAsync(
            My.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(myCts.Token),
            myCts, "MySQL");
        var my = await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(my, Has.Count.EqualTo(3), "MySQL usable after mid-stream cancellation");

        using var ssCts = new CancellationTokenSource();
        await CancelAfterFirstRowAsync(
            Ss.Users().OrderBy(u => u.UserId).Select(u => u.UserName).ToAsyncEnumerable(ssCts.Token),
            ssCts, "SQL Server");
        var ss = await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ss, Has.Count.EqualTo(3), "SQL Server usable after mid-stream cancellation");
    }

    /// <summary>
    /// Mid-stream cancellation surfaces <see cref="OperationCanceledException"/>
    /// on SQLite, where the reader still consults the token after the first row.
    /// </summary>
    /// <remarks>
    /// Asserted on SQLite only, and deliberately so. The iterator reads rows
    /// inside <c>while (await reader.ReadAsync(ct))</c>, but a provider that has
    /// already buffered the whole result set never awaits I/O again and so never
    /// observes the token — the three seeded rows arrive from PostgreSQL in a
    /// single response, and enumeration there runs to completion after
    /// cancellation. Forcing the container dialects to stream would mean bulk
    /// -inserting thousands of rows through four dialect-specific statements,
    /// which buys little over the connection-usability check above. Recorded in
    /// the step-10 note in <c>workflow.md</c>.
    /// </remarks>
    [Test]
    public async Task MidStreamCancellation_SurfacesOperationCanceled_WhenProviderAwaitsIo()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        using var cts = new CancellationTokenSource();
        var enumerator = Lite.Users()
            .OrderBy(u => u.UserId)
            .Select(u => u.UserName)
            .ToAsyncEnumerable(cts.Token)
            .GetAsyncEnumerator();

        var cancelled = false;
        try
        {
            Assert.That(await enumerator.MoveNextAsync(), Is.True, "expected a first row");

            await cts.CancelAsync();

            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    // Drain until the token is observed.
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.That(cancelled, Is.True,
            "SQLite enumeration ran to completion after the token was cancelled");

        var rows = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(rows, Has.Count.EqualTo(3), "connection usable after cancelled enumeration");
    }

    /// <summary>
    /// Reads one row, cancels, then drains whatever the provider still has
    /// buffered. Tolerates both outcomes — the caller asserts on the connection
    /// afterwards.
    /// </summary>
    private static async Task CancelAfterFirstRowAsync(
        IAsyncEnumerable<string> stream, CancellationTokenSource cts, string label)
    {
        var enumerator = stream.GetAsyncEnumerator();
        try
        {
            Assert.That(await enumerator.MoveNextAsync(), Is.True, $"{label}: expected a first row");

            await cts.CancelAsync();

            try
            {
                while (await enumerator.MoveNextAsync())
                {
                }
            }
            catch (OperationCanceledException)
            {
                // Expected where the provider consults the token.
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>
    /// The raw-SQL streaming overload takes its token as an explicit parameter
    /// ahead of the <c>params</c> array, which is easy to get wrong at a call
    /// site and was never exercised with a live token.
    /// </summary>
    [Test]
    public async Task PreCancelledToken_RawSqlStreaming_ThrowsOperationCanceled()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await AssertRawSqlCancelledAsync(
            Lite.RawSqlAsync<UserWithEmailDto>(
                "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\"", cts.Token),
            "SQLite");

        await AssertRawSqlCancelledAsync(
            Pg.RawSqlAsync<UserWithEmailDto>(
                "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\"", cts.Token),
            "PostgreSQL");

        var lite = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        var pg = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.Multiple(() =>
        {
            Assert.That(lite, Has.Count.EqualTo(3), "SQLite usable after cancelled raw SQL");
            Assert.That(pg, Has.Count.EqualTo(3), "PostgreSQL usable after cancelled raw SQL");
        });
    }

    private static async Task AssertRawSqlCancelledAsync(IAsyncEnumerable<UserWithEmailDto> stream, string label)
    {
        var enumerator = stream.GetAsyncEnumerator();
        try
        {
            await enumerator.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"{label}: expected OperationCanceledException, got {ex.GetType().Name}: {ex.Message}");
            return;
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail($"{label}: raw-SQL stream yielded a row despite a pre-cancelled token");
    }
}
