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
/// Each worker body is a named method rather than an inline lambda. Chains
/// written inside a lambda nested in another lambda (the natural
/// <c>Select(... => Task.Run(async () => ...))</c> shape) capture their locals
/// in a display class the generator cannot resolve, and the emitted
/// interceptor fails to compile with CS0103. See the step-8 note in
/// <c>workflow.md</c>.
/// </para>
/// </remarks>
[TestFixture]
internal class ConcurrencyTests
{
    /// <summary>
    /// Kept modest deliberately: each harness holds four open connections, so
    /// the connection count is this number times four.
    /// </summary>
    private const int Workers = 8;

    private static async Task<QueryTestHarness[]> CreateHarnessesAsync(int count)
    {
        var harnesses = new QueryTestHarness[count];
        for (int i = 0; i < count; i++)
            harnesses[i] = await QueryTestHarness.CreateAsync();
        return harnesses;
    }

    private static async Task DisposeAllAsync(QueryTestHarness[] harnesses)
    {
        foreach (var harness in harnesses)
        {
            if (harness is not null)
                await harness.DisposeAsync();
        }
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
        // initialization is genuinely contended.
        gate.SignalAndWait();

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
    /// The chain shape here is intentionally not used anywhere else in the
    /// suite, so in a full-suite run this is the process's first touch of that
    /// carrier. NUnit does not guarantee fixture order, so first-touch is not
    /// guaranteed in a filtered run — the contended execution itself is the
    /// assertion either way.
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

    private readonly record struct DialectCounts(int Index, int Threshold, int Lite, int Pg, int My, int Ss);

    private static async Task<DialectCounts> RunAllDialectsWorkerAsync(QueryTestHarness harness, int index)
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

        return new DialectCounts(index, threshold, lite.Count, pg.Count, my.Count, ss.Count);
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
            var tasks = new Task<DialectCounts>[Workers];
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
                    // Seed has three users; threshold 1 excludes UserId 1.
                    var expected = r.Threshold == 0 ? 3 : 2;
                    Assert.That(r.Lite, Is.EqualTo(expected), $"worker {r.Index} SQLite");
                    Assert.That(r.Pg, Is.EqualTo(expected), $"worker {r.Index} PostgreSQL");
                    Assert.That(r.My, Is.EqualTo(expected), $"worker {r.Index} MySQL");
                    Assert.That(r.Ss, Is.EqualTo(expected), $"worker {r.Index} SQL Server");
                }
            });
        }
        finally
        {
            await DisposeAllAsync(harnesses);
        }
    }
}
