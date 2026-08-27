using NUnit.Framework;

namespace Quarry.Tests.Generation;

/// <summary>
/// Execution coverage for closure-capture resolution (issue #333). Companion to
/// <see cref="LambdaCaptureScopeTests"/>, which only proves the generated interceptor compiles.
/// <para>
/// <b>Compiling is not passing.</b> The display-class name and closure ordinal are *predicted* from
/// syntax; a wrong prediction still produces perfectly valid C# and then throws
/// <c>MissingFieldException</c> or <c>InvalidCastException</c> the first time the chain runs. The
/// original fix for #333 passed a full codegen suite and still threw on execution. Every shape below
/// therefore runs a real query and asserts row state.
/// </para>
/// <para>
/// SQLite only: these assert captured-value plumbing, not dialect behaviour, and the container dialects
/// share one seeded baseline (see <c>llm-testing.md</c>).
/// </para>
/// </summary>
[TestFixture]
public class LambdaCaptureExecutionTests
{
    private static readonly string[] Names = { "Alice", "Bob" };

    /// <summary>Baseline: all captures in one scope.</summary>
    [Test]
    public async Task SingleScope_MethodLocals()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var minId = 0;
        var name = "Alice";
        var ids = await Lite.Users()
            .Where(u => u.UserId > minId && u.UserName == name)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(ids, Is.EqualTo(new[] { 1 }));
    }

    /// <summary>A `foreach` variable alone — its own per-iteration display class.</summary>
    [Test]
    public async Task LoopVariableAlone()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var found = new List<int>();
        foreach (var name in Names)
        {
            found.AddRange(await Lite.Users()
                .Where(u => u.UserName == name)
                .Select(u => u.UserId)
                .ExecuteFetchAllAsync());
        }

        Assert.That(found, Is.EqualTo(new[] { 1, 2 }));
    }

    /// <summary>
    /// A loop variable and a method-scope local, split across separate clauses so each captures from a
    /// single scope. This is the workaround the multi-scope diagnostic recommends, so it must work.
    /// </summary>
    [Test]
    public async Task SeparateClauses_LoopVariableAndMethodLocal()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var minId = 0;
        var found = new List<int>();
        foreach (var name in Names)
        {
            found.AddRange(await Lite.Users()
                .Where(u => u.UserName == name)
                .Where(u => u.UserId > minId)
                .Select(u => u.UserId)
                .ExecuteFetchAllAsync());
        }

        Assert.That(found, Is.EqualTo(new[] { 1, 2 }));
    }

    /// <summary>An instance field alone: the delegate target IS the containing instance.</summary>
    private readonly int _minId = 0;

    [Test]
    public async Task InstanceFieldAlone()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = await Lite.Users()
            .Where(u => u.UserId > _minId)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(ids, Has.Count.EqualTo(3));
    }

    /// <summary>
    /// Instance field mixed with a local. Adding the local interposes a display class, so the field is
    /// only reachable through its <c>&lt;&gt;4__this</c> back-reference.
    /// </summary>
    [Test]
    public async Task InstanceFieldMixedWithLocal()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var name = "Alice";
        var ids = await Lite.Users()
            .Where(u => u.UserId > _minId && u.UserName == name)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(ids, Is.EqualTo(new[] { 1 }));
    }

    /// <summary>
    /// The `<>4__this` hop must read the field LIVE, not a value snapshotted at chain construction.
    /// </summary>
    [Test]
    public async Task InstanceFieldMixedWithLocal_ReadsCurrentValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var name = "Charlie";
        _mutableMin = 0;
        var all = await Lite.Users()
            .Where(u => u.UserId > _mutableMin && u.UserName == name)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        _mutableMin = 99;
        var none = await Lite.Users()
            .Where(u => u.UserId > _mutableMin && u.UserName == name)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EqualTo(new[] { 3 }), "field read with _mutableMin = 0");
            Assert.That(none, Is.Empty, "field must be re-read, not snapshotted");
        });
    }

    private int _mutableMin;

    /// <summary>A chain inside a single lambda — the context arrives as the lambda's parameter.</summary>
    [Test]
    public async Task InsideSingleLambda()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var contexts = new[] { Lite };
        var results = contexts.Select(async db =>
        {
            var name = "Alice";
            return await db.Users()
                .Where(u => u.UserName == name)
                .Select(u => u.UserId)
                .ExecuteFetchAllAsync();
        }).ToList();

        Assert.That(await results[0], Is.EqualTo(new[] { 1 }));
    }

    /// <summary>
    /// Issue #333's shape: a chain inside a lambda nested in another lambda, where the captured value
    /// differs per iteration — so a display class that resolved to the wrong scope would return the
    /// wrong rows rather than merely throwing.
    /// <para>
    /// Each worker is awaited before the next is started. <c>SqliteConnection</c> is not safe for
    /// concurrent commands on one connection, and this test is about capture plumbing, not concurrency
    /// (<c>Integration/ConcurrencyTests</c> owns that).
    /// </para>
    /// </summary>
    [TestCase("Alice", 1)]
    [TestCase("Bob", 2)]
    [TestCase("Charlie", 3)]
    public async Task InsideNestedLambdas(string worker, int expectedId)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var contexts = new[] { Lite };
        var tasks = contexts.Select((db, i) => Task.Run(async () =>
        {
            var name = worker;
            return await db.Users()
                .Where(u => u.UserName == name)
                .Select(u => u.UserId)
                .ExecuteFetchAllAsync();
        })).ToList();

        Assert.That(await tasks[0], Is.EqualTo(new[] { expectedId }));
    }

    /// <summary>Issue #333's exact repro, including the `Update().Set(...)` extraction path.</summary>
    [Test]
    public async Task InsideNestedLambdas_UpdateSet()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var contexts = new[] { Lite };
        var tasks = contexts.Select((db, i) => Task.Run(async () =>
        {
            var name = $"Worker{i}";
            return await db.Users()
                .Update()
                .Set(u => u.UserName = name)
                .Where(u => u.UserId == 1)
                .ExecuteNonQueryAsync();
        })).ToList();

        var affected = await tasks[0];
        var updated = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();

        Assert.Multiple(() =>
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(updated, Is.EqualTo("Worker0"));
        });
    }

    /// <summary>
    /// A `catch`-clause variable owns its own display class, so it must not resolve to the block
    /// enclosing the `try`. A wrong ordinal here throws MissingFieldException at execution.
    /// </summary>
    [Test]
    public async Task CatchClauseVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        List<int> ids;
        try
        {
            throw new InvalidOperationException("Alice");
        }
        catch (InvalidOperationException ex)
        {
            ids = await Lite.Users()
                .Where(u => u.UserName == ex.Message)
                .Select(u => u.UserId)
                .ExecuteFetchAllAsync();
        }

        Assert.That(ids, Is.EqualTo(new[] { 1 }));
    }

    /// <summary>
    /// Two clauses on one chain, each mixing an instance field with a local — each needs its own
    /// `&lt;&gt;4__this` hop. Also checks both clauses read the RIGHT field, not one shared value.
    /// </summary>
    [Test]
    public async Task TwoClausesEachMixingFieldAndLocal()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var name = "Alice";
        var other = "Bob";
        var ids = await Lite.Users()
            .Where(u => u.UserId > _minId && u.UserName == name)
            .Where(u => u.UserId < _maxId && u.UserName != other)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(ids, Is.EqualTo(new[] { 1 }));
    }

    private readonly int _maxId = 99;

    /// <summary>A `for`-declaration variable captured by a clause.</summary>
    [Test]
    public async Task ForDeclarationVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var counts = new List<int>();
        for (int i = 0; i < 2; i++)
        {
            var ids = await Lite.Users()
                .Where(u => u.UserId > i)
                .Select(u => u.UserId)
                .ExecuteFetchAllAsync();
            counts.Add(ids.Count);
        }

        // i = 0 matches all three seeded users; i = 1 excludes Alice.
        Assert.That(counts, Is.EqualTo(new[] { 3, 2 }));
    }
}
