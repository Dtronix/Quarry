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
}
