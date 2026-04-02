using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Cross-dialect SQL output tests for collection + scalar parameter combinations.
/// Verifies parameter index shifting produces distinct, non-colliding names.
/// Regression tests for #140: collection parameter collision.
/// </summary>
[TestFixture]
internal class CollectionParameterCollisionTests
{
    #region Collection + scalar — parameter index shifting

    [Test]
    public async Task Where_CollectionPlusScalar_ParameterIndicesDistinct()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Use captured variable for scalar param (not a constant that gets inlined)
        var ids = new List<int> { 1, 2, 3 };
        var minId = 0;

        var ltDiag = Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ToDiagnostics();

        var pgDiag = Pg.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ToDiagnostics();

        var myDiag = My.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ToDiagnostics();

        var ssDiag = Ss.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ToDiagnostics();

        // Collection has 3 elements at @p0/@p1/@p2, scalar shifts to @p3
        Assert.Multiple(() =>
        {
            Assert.That(ltDiag.Sql, Does.Contain("IN (@p0, @p1, @p2)"), "SQLite: collection expansion");
            Assert.That(ltDiag.Sql, Does.Contain("@p3"), "SQLite: shifted scalar");
            Assert.That(ltDiag.Parameters, Has.Count.EqualTo(4), "SQLite: 3 collection + 1 scalar");

            Assert.That(pgDiag.Sql, Does.Contain("IN ($1, $2, $3)"), "PG: collection expansion");
            Assert.That(pgDiag.Sql, Does.Contain("$4"), "PG: shifted scalar");
            Assert.That(pgDiag.Parameters, Has.Count.EqualTo(4), "PG: 3 collection + 1 scalar");

            Assert.That(myDiag.Sql, Does.Contain("IN (?, ?, ?)"), "MySQL: collection expansion");
            Assert.That(myDiag.Parameters, Has.Count.EqualTo(4), "MySQL: 3 collection + 1 scalar");

            Assert.That(ssDiag.Sql, Does.Contain("IN (@p0, @p1, @p2)"), "SS: collection expansion");
            Assert.That(ssDiag.Sql, Does.Contain("@p3"), "SS: shifted scalar");
            Assert.That(ssDiag.Parameters, Has.Count.EqualTo(4), "SS: 3 collection + 1 scalar");
        });
    }

    [Test]
    public async Task Where_EmptyCollectionPlusScalar_ShiftsDown()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int>();
        var minId = 0;
        var diag = Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ToDiagnostics();

        // Empty collection: IN becomes "SELECT 1 WHERE 1=0"
        // Scalar shifts down: originally @p1, shift = -1, so @p0
        Assert.That(diag.Sql, Does.Contain("SELECT 1 WHERE 1=0"), "Empty collection guard");
        Assert.That(diag.Parameters, Has.Count.EqualTo(1), "Only scalar param remains");
    }

    [Test]
    public async Task Where_SingleElementCollectionPlusScalar_NoShift()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1 };
        var minId = 0;
        var diag = Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ToDiagnostics();

        // Single element: shift = 0, scalar stays at @p1
        Assert.That(diag.Sql, Does.Contain("IN (@p0)"), "Single element collection");
        Assert.That(diag.Sql, Does.Contain("@p1"), "Scalar at original index");
        Assert.That(diag.Parameters, Has.Count.EqualTo(2));
    }

    #endregion

    #region Collection + pagination — index shifting

    [Test]
    public async Task Where_CollectionPlusScalarWithLimit_PaginationShifted()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1, 2, 3 };
        var minId = 0;
        var limit = 10;
        var diag = Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .Limit(limit)
            .ToDiagnostics();

        // Collection @p0,@p1,@p2 + scalar @p3 + LIMIT @p4
        Assert.That(diag.Sql, Does.Contain("IN (@p0, @p1, @p2)"));
        Assert.That(diag.Sql, Does.Contain("@p3")); // scalar
        Assert.That(diag.Sql, Does.Contain("@p4")); // LIMIT
    }

    #endregion

    #region Scalar before collection — cross-dialect (Issue 8)

    [Test]
    public async Task Where_ScalarBeforeCollection_CrossDialect_IndicesCorrect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Scalar param at GlobalIndex 0, collection at GlobalIndex 1
        var ids = new List<int> { 1, 2, 3 };
        var maxId = 10;

        var ltDiag = Lite.Users()
            .Where(u => u.UserId <= maxId && ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ToDiagnostics();

        var pgDiag = Pg.Users()
            .Where(u => u.UserId <= maxId && ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ToDiagnostics();

        var myDiag = My.Users()
            .Where(u => u.UserId <= maxId && ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ToDiagnostics();

        var ssDiag = Ss.Users()
            .Where(u => u.UserId <= maxId && ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ToDiagnostics();

        // Scalar @p0 stays at @p0 (no preceding collections), collection expands to @p1,@p2,@p3
        Assert.Multiple(() =>
        {
            Assert.That(ltDiag.Sql, Does.Contain("@p0"), "SQLite: scalar at original index");
            Assert.That(ltDiag.Sql, Does.Contain("IN (@p1, @p2, @p3)"), "SQLite: collection shifted");
            Assert.That(ltDiag.Parameters, Has.Count.EqualTo(4));

            Assert.That(pgDiag.Sql, Does.Contain("$1"), "PG: scalar at $1");
            Assert.That(pgDiag.Sql, Does.Contain("IN ($2, $3, $4)"), "PG: collection shifted");
            Assert.That(pgDiag.Parameters, Has.Count.EqualTo(4));

            Assert.That(myDiag.Sql, Does.Contain("IN (?, ?, ?)"), "MySQL: collection expansion");
            Assert.That(myDiag.Parameters, Has.Count.EqualTo(4));

            Assert.That(ssDiag.Sql, Does.Contain("@p0"), "SS: scalar at original index");
            Assert.That(ssDiag.Sql, Does.Contain("IN (@p1, @p2, @p3)"), "SS: collection shifted");
            Assert.That(ssDiag.Parameters, Has.Count.EqualTo(4));
        });
    }

    #endregion

    #region Multiple collections (Issue 7)

    [Test]
    public async Task Where_TwoCollections_IndicesAccumulateCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var orderIds = new List<int> { 1, 2, 3 };
        var statuses = new List<string> { "Shipped", "Pending" };

        var ltDiag = Lite.Orders()
            .Where(o => orderIds.Contains(o.OrderId) && statuses.Contains(o.Status))
            .Select(o => o.Total)
            .ToDiagnostics();

        var pgDiag = Pg.Orders()
            .Where(o => orderIds.Contains(o.OrderId) && statuses.Contains(o.Status))
            .Select(o => o.Total)
            .ToDiagnostics();

        // Both collections should be parameterized with shifted indices.
        // Collection 1 (3 ints): @p0,@p1,@p2. Shift = 2.
        // Collection 2 (2 strings): starts at @p(1+2)=@p3, occupies @p3,@p4.
        Assert.Multiple(() =>
        {
            Assert.That(ltDiag.Sql, Does.Contain("IN (@p0, @p1, @p2)"), "SQLite: first collection");
            Assert.That(ltDiag.Sql, Does.Contain("IN (@p3, @p4)"), "SQLite: second collection shifted");

            Assert.That(pgDiag.Sql, Does.Contain("IN ($1, $2, $3)"), "PG: first collection");
            Assert.That(pgDiag.Sql, Does.Contain("IN ($4, $5)"), "PG: second collection shifted");
        });
    }

    [Test]
    public async Task Where_TwoCollections_ExecutionCorrect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var orderIds = new List<int> { 1, 2, 3 };
        var statuses = new List<string> { "Shipped" };

        var results = await Lite.Orders()
            .Where(o => orderIds.Contains(o.OrderId) && statuses.Contains(o.Status))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();

        // Orders 1 and 3 are Shipped, Order 2 is Pending
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain(1));
        Assert.That(results, Does.Contain(3));
    }

    #endregion

    #region Execution correctness — exact reproduction from #140

    [Test]
    public async Task Where_CollectionPlusScalar_ExecutionProducesCorrectResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var userIds = new List<int> { 1, 2, 3 };
        var minId = 0;
        var results = await Lite.Users()
            .Where(u => userIds.Contains(u.UserId) && u.UserId > minId)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results, Does.Contain((1, "Alice")));
        Assert.That(results, Does.Contain((2, "Bob")));
        Assert.That(results, Does.Contain((3, "Charlie")));
    }

    #endregion

    #region Issue #140 exact scenario — collection + nullable DateTime + boolean

    [Test]
    public async Task Where_CollectionPlusNullableDateTime_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Exact pattern from issue #140: collection + nullable scalar comparison
        var userIds = new List<int> { 1, 2, 3 };
        DateTime? startDate = new DateTime(2024, 1, 1);

        var ltDiag = Lite.Users()
            .Where(u => userIds.Contains(u.UserId) && (startDate == null || u.CreatedAt >= startDate))
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics();

        var pgDiag = Pg.Users()
            .Where(u => userIds.Contains(u.UserId) && (startDate == null || u.CreatedAt >= startDate))
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics();

        var myDiag = My.Users()
            .Where(u => userIds.Contains(u.UserId) && (startDate == null || u.CreatedAt >= startDate))
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics();

        var ssDiag = Ss.Users()
            .Where(u => userIds.Contains(u.UserId) && (startDate == null || u.CreatedAt >= startDate))
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics();

        // Collection expands to 3 elements. Nullable DateTime? generates 2 scalar params
        // (one for IS NULL check, one for >= comparison). All scalars must be shifted past
        // the collection expansion — no index overlap allowed.
        Assert.Multiple(() =>
        {
            Assert.That(ltDiag.Sql, Does.Contain("IN (@p0, @p1, @p2)"), "SQLite: collection");
            Assert.That(ltDiag.Sql, Does.Not.Contain("IN (@p0, @p1, @p2) AND (@p1"), "SQLite: no collision with @p1");

            Assert.That(pgDiag.Sql, Does.Contain("IN ($1, $2, $3)"), "PG: collection");
            Assert.That(pgDiag.Sql, Does.Not.Contain("IN ($1, $2, $3) AND ($2"), "PG: no collision with $2");

            Assert.That(myDiag.Sql, Does.Contain("IN (?, ?, ?)"), "MySQL: collection");

            Assert.That(ssDiag.Sql, Does.Contain("IN (@p0, @p1, @p2)"), "SS: collection");
            Assert.That(ssDiag.Sql, Does.Not.Contain("IN (@p0, @p1, @p2) AND (@p1"), "SS: no collision with @p1");
        });
    }

    [Test]
    public async Task Where_CollectionPlusNullableDateTime_ExecutionCorrect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Exact #140 reproduction — must not crash on SQLite
        var userIds = new List<int> { 1, 2, 3 };
        DateTime? startDate = new DateTime(2024, 1, 1);

        var results = await Lite.Users()
            .Where(u => userIds.Contains(u.UserId) && (startDate == null || u.CreatedAt >= startDate))
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        // All 3 users have CreatedAt seeded (via DEFAULT), all >= 2024-01-01
        Assert.That(results, Has.Count.GreaterThanOrEqualTo(1));
    }

    #endregion
}
