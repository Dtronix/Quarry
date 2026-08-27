using System.Threading.Tasks;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

/// <summary>
/// Concurrency guardrails. Nothing in the suite previously executed two Quarry
/// operations at the same time, so the thread-safety of everything the runtime
/// shares between call sites — carrier statics, the compiled-SQL fields, reader
/// and mapper caches, the OpId counter — rested entirely on being correct by
/// construction. These tests are the regression insurance for that.
/// </summary>
/// <remarks>
/// <para>
/// Writes run against SQLite only. Every harness owns a private in-memory
/// database, so concurrent writers are isolated at the storage layer and any
/// cross-talk that does appear must have come from state Quarry shares in the
/// process. The container dialects, by contrast, share one seeded baseline
/// schema across harnesses with a per-harness transaction, so concurrent
/// writes there would block on row locks and produce timeouts rather than
/// findings — they are exercised read-only.
/// </para>
/// <para>
/// Harnesses are created sequentially and only the Quarry operations run in
/// parallel: container setup has its own first-call initialization, and racing
/// that would test the fixtures rather than the library.
/// </para>
/// <para>
/// Each worker body is a named method rather than an inline lambda, and must stay that way.
/// The original reason was issue #333 (a chain inside a lambda emitted an interceptor
/// referencing captured locals directly, CS0103). That is fixed, and these bodies were
/// briefly inlined — but inlining them made this fixture depend on a display-class name
/// the generator PREDICTS, and that prediction is wrong under <c>&lt;Optimize&gt;</c>.
/// Roslyn's <c>ClosureConversion</c> runs <c>MergeEnvironments()</c> only when
/// <c>OptimizationLevel == Release</c>; a merged-away environment never consumes a closure
/// ordinal, so every later ordinal shifts down. This method has enough capture scopes for
/// that to move the clause's display class from <c>_3</c> to <c>_2</c>, and the interceptor
/// then fails with <c>TypeLoadException</c>. <c>dotnet test</c> defaults to Debug while CI
/// runs <c>-c Release</c>, which is why it passed locally and failed in CI. Tracked as #344.
/// </para>
/// <para>
/// Hoisting each body into a named method makes the chain's captures ordinary method locals,
/// which are not affected by environment merging. Do not inline them again until #344 is
/// resolved. Note the multi-scope guard cannot catch this: the mispredicted clause is an
/// ordinary single-scope capture, and it is an unrelated lambda elsewhere in the method that
/// triggers the merge.
/// </para>
/// </remarks>
[TestFixture]
internal class ConcurrencyTests
{
    /// <summary>
    /// Each harness holds four open connections — one SQLite plus three container
    /// transactions — so the open-connection count is this number times four.
    /// </summary>
    /// <remarks>
    /// Four, not eight. Measured with warm containers, both tests here run in well under a
    /// second combined, so worker count is not a runtime concern either way — but four is
    /// already enough for worker-to-worker cross-talk to be observable, and eight was an
    /// arbitrary number holding twice the connections against the shared baseline.
    /// </remarks>
    private const int Workers = 4;

    /// <summary>
    /// How long a worker will wait at the shared barrier before failing. The barrier is
    /// released only once every worker arrives, and each worker occupies a thread-pool
    /// thread while it waits — so on a small runner the last few depend on thread-pool
    /// injection. Without a timeout that becomes an indefinite hang and the CI job dies
    /// with no assertion message.
    /// </summary>
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Creates <paramref name="count"/> harnesses, disposing any already created if one
    /// throws part-way. Each harness holds an open SQLite connection plus open PG/MySQL/
    /// SQL Server transactions against the *shared* baseline, so leaking a partial batch
    /// would cascade into lock timeouts in every fixture that runs afterwards.
    /// </summary>
    private static async Task<QueryTestHarness[]> CreateHarnessesAsync(int count)
    {
        var harnesses = new QueryTestHarness[count];
        try
        {
            for (int i = 0; i < count; i++)
                harnesses[i] = await QueryTestHarness.CreateAsync();
        }
        catch
        {
            await DisposeAllAsync(harnesses);
            throw;
        }

        return harnesses;
    }

    /// <summary>
    /// Disposes every harness even if one throws, so a single bad teardown cannot strand
    /// the rest. The first failure is rethrown once the whole batch has been attempted.
    /// </summary>
    private static async Task DisposeAllAsync(QueryTestHarness[] harnesses)
    {
        List<Exception>? failures = null;

        foreach (var harness in harnesses)
        {
            if (harness is null)
                continue;

            try
            {
                await harness.DisposeAsync();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more harnesses failed to dispose.", failures);
    }

    // ── Mixed read/write ─────────────────────────────────────────────────────

    private readonly record struct MixedResult(
        int Index, int Renamed, int Patched, string UserName, string? Email, int ActiveCount);

    private static async Task<MixedResult> RunMixedWorkerAsync(QueryTestHarness harness, int index)
    {
        var (Lite, _, _, _) = harness;

        var name = $"Worker{index}";
        var email = $"worker{index}@test.com";

        // Plain UPDATE — worker-specific value, identical chain shape.
        var renamed = await Lite.Users()
            .Update()
            .Set(u => u.UserName = name)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        // Partial update through the Patch path, which carries its own column
        // mask and parameter layout.
        var patched = await Lite.Users()
            .Update()
            .Set(new User.Patch { Email = email })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        // Read back both writes plus an unrelated filtered projection.
        var row = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.Email))
            .ExecuteFetchFirstAsync();

        var activeIds = await Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        return new MixedResult(index, renamed, patched, row.UserName, row.Email, activeIds.Count);
    }

    /// <summary>
    /// Mixed SELECT / UPDATE / Patch load, each worker driving the same chain
    /// shapes with its own parameter values. A shared mutable parameter buffer
    /// or a cached-per-carrier command would surface here as one worker
    /// reading back another worker's value.
    /// </summary>
    [Test]
    public async Task ParallelHarnesses_MixedReadWrite_DoNotShareParameterState()
    {
        var harnesses = await CreateHarnessesAsync(Workers);
        try
        {
            var tasks = new Task<MixedResult>[Workers];
            for (int i = 0; i < Workers; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() => RunMixedWorkerAsync(harnesses[index], index));
            }

            var results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                foreach (var r in results)
                {
                    Assert.That(r.Renamed, Is.EqualTo(1), $"worker {r.Index} UPDATE row count");
                    Assert.That(r.Patched, Is.EqualTo(1), $"worker {r.Index} Patch row count");
                    Assert.That(r.UserName, Is.EqualTo($"Worker{r.Index}"),
                        $"worker {r.Index} read back another worker's UPDATE parameter");
                    Assert.That(r.Email, Is.EqualTo($"worker{r.Index}@test.com"),
                        $"worker {r.Index} read back another worker's Patch parameter");
                    Assert.That(r.ActiveCount, Is.EqualTo(2),
                        $"worker {r.Index} saw a foreign row set — seed has two active users");
                }
            });
        }
        finally
        {
            await DisposeAllAsync(harnesses);
        }
    }

    // ── Contended first touch of one carrier ─────────────────────────────────

    private static async Task<List<(int OrderId, string Status, decimal Total)>> RunFirstTouchWorkerAsync(
        QueryTestHarness harness, Barrier gate)
    {
        var (Lite, _, _, _) = harness;

        // Release every worker into the chain at the same moment so the static
        // initialization is genuinely contended. Bounded so a thread-pool starved
        // runner fails with a message instead of hanging until the job times out.
        if (!gate.SignalAndWait(BarrierTimeout))
        {
            throw new TimeoutException(
                $"Workers did not all reach the barrier within {BarrierTimeout.TotalSeconds:F0}s. " +
                "This is a thread-pool starvation problem in the test host, not a Quarry defect — " +
                $"each of the {Workers} workers blocks a pool thread while waiting.");
        }

        return await Lite.Orders()
            .Where(o => o.Total > 100.00m)
            .OrderBy(o => o.OrderId)
            .Select(o => (o.OrderId, o.Status, o.Total))
            .ExecuteFetchAllAsync();
    }

    /// <summary>
    /// All workers execute one identical chain simultaneously. The generated
    /// carrier holds its SQL and reader in static fields, so if that
    /// initialization is not safe to race, the first concurrent execution of a
    /// chain is where it breaks.
    /// </summary>
    /// <remarks>
    /// The chain shape here is intentionally not used anywhere else in the suite. That is
    /// what makes the first-touch claim hold regardless of NUnit's fixture ordering: no
    /// other test can have initialized this carrier, so whenever this test runs, it runs
    /// that carrier's static initialization — and it runs it from
    /// <see cref="Workers"/> threads released simultaneously.
    /// </remarks>
    [Test]
    public async Task ParallelFirstTouch_IdenticalChain_InitializesSafely()
    {
        var harnesses = await CreateHarnessesAsync(Workers);
        try
        {
            using var gate = new Barrier(Workers);

            var tasks = new Task<List<(int OrderId, string Status, decimal Total)>>[Workers];
            for (int i = 0; i < Workers; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() => RunFirstTouchWorkerAsync(harnesses[index], gate));
            }

            var results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                for (int i = 0; i < results.Length; i++)
                {
                    // Seed: orders 1 (250.00) and 3 (150.00) exceed 100.00.
                    Assert.That(results[i].Select(r => r.OrderId), Is.EqualTo(new[] { 1, 3 }),
                        $"worker {i} rows");
                    Assert.That(results[i].Select(r => r.Status), Is.EqualTo(new[] { "Shipped", "Shipped" }),
                        $"worker {i} statuses");
                }
            });
        }
        finally
        {
            await DisposeAllAsync(harnesses);
        }
    }

    // ── Independent contexts, all dialects ───────────────────────────────────

    private readonly record struct DialectRows(
        int Index,
        int Threshold,
        List<string> Lite,
        List<string> Pg,
        List<string> My,
        List<string> Ss);

    private static async Task<DialectRows> RunAllDialectsWorkerAsync(QueryTestHarness harness, int index)
    {
        var (Lite, Pg, My, Ss) = harness;

        // A worker-specific bound parameter on every dialect, so a shared
        // parameter buffer would cross dialects as well as workers.
        var threshold = index % 2 == 0 ? 0 : 1;

        var lite = await Lite.Users().Where(u => u.UserId > threshold)
            .Select(u => u.UserName).ExecuteFetchAllAsync();
        var pg = await Pg.Users().Where(u => u.UserId > threshold)
            .Select(u => u.UserName).ExecuteFetchAllAsync();
        var my = await My.Users().Where(u => u.UserId > threshold)
            .Select(u => u.UserName).ExecuteFetchAllAsync();
        var ss = await Ss.Users().Where(u => u.UserId > threshold)
            .Select(u => u.UserName).ExecuteFetchAllAsync();

        return new DialectRows(index, threshold, lite, pg, my, ss);
    }

    /// <summary>
    /// The documented-supported deployment shape: independent contexts on
    /// separate connections, queried concurrently. Covers all four dialects
    /// read-only against the shared baseline.
    /// </summary>
    [Test]
    public async Task ParallelContexts_SeparateConnections_AllDialectsQueryConcurrently()
    {
        var harnesses = await CreateHarnessesAsync(Workers);
        try
        {
            var tasks = new Task<DialectRows>[Workers];
            for (int i = 0; i < Workers; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() => RunAllDialectsWorkerAsync(harnesses[index], index));
            }

            var results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                foreach (var r in results)
                {
                    // Seed has three users; threshold 1 excludes UserId 1 (Alice).
                    // Assert the names, not just the cardinality: cross-talk that returned
                    // another worker's rows at the same count would be invisible otherwise.
                    // The query carries no ORDER BY, so compare as an unordered set.
                    var expected = r.Threshold == 0
                        ? new[] { "Alice", "Bob", "Charlie" }
                        : new[] { "Bob", "Charlie" };

                    Assert.That(r.Lite, Is.EquivalentTo(expected), $"worker {r.Index} SQLite");
                    Assert.That(r.Pg, Is.EquivalentTo(expected), $"worker {r.Index} PostgreSQL");
                    Assert.That(r.My, Is.EquivalentTo(expected), $"worker {r.Index} MySQL");
                    Assert.That(r.Ss, Is.EquivalentTo(expected), $"worker {r.Index} SQL Server");
                }
            });
        }
        finally
        {
            await DisposeAllAsync(harnesses);
        }
    }
}
