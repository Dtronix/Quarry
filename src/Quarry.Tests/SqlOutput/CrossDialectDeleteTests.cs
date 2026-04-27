using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectDeleteTests
{
    #region Basic Where

    [Test]
    public async Task Delete_Where_Equality()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Delete().Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Delete().Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Delete().Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Delete().Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "DELETE FROM `users` WHERE `UserId` = 1",
            ss:     "DELETE FROM [users] WHERE [UserId] = 1");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));
    }

    [Test]
    public async Task Delete_Where_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Delete().Where(u => u.UserId > 100).Prepare();
        var pg = Pg.Users().Delete().Where(u => u.UserId > 100).Prepare();
        var my = My.Users().Delete().Where(u => u.UserId > 100).Prepare();
        var ss = Ss.Users().Delete().Where(u => u.UserId > 100).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" > 100",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" > 100",
            mysql:  "DELETE FROM `users` WHERE `UserId` > 100",
            ss:     "DELETE FROM [users] WHERE [UserId] > 100");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(0));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(0));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(0));
    }

    #endregion

    #region Boolean

    [Test]
    public async Task Delete_Where_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Delete().Where(u => u.IsActive).Prepare();
        var pg = Pg.Users().Delete().Where(u => u.IsActive).Prepare();
        var my = My.Users().Delete().Where(u => u.IsActive).Prepare();
        var ss = Ss.Users().Delete().Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "DELETE FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "DELETE FROM `users` WHERE `IsActive` = 1",
            ss:     "DELETE FROM [users] WHERE [IsActive] = 1");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2)); // Alice and Bob are active

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(2));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(2));
    }

    [Test]
    public async Task Delete_Where_NegatedBoolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Delete().Where(u => !u.IsActive).Prepare();
        var pg = Pg.Users().Delete().Where(u => !u.IsActive).Prepare();
        var my = My.Users().Delete().Where(u => !u.IsActive).Prepare();
        var ss = Ss.Users().Delete().Where(u => !u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"IsActive\" = 0",
            pg:     "DELETE FROM \"users\" WHERE \"IsActive\" = FALSE",
            mysql:  "DELETE FROM `users` WHERE `IsActive` = 0",
            ss:     "DELETE FROM [users] WHERE [IsActive] = 0");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1)); // Only Charlie is inactive

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));
    }

    #endregion

    #region Multiple Where (AND)

    [Test]
    public async Task Delete_MultipleWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).Prepare();
        var pg = Pg.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).Prepare();
        var my = My.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).Prepare();
        var ss = Ss.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE (\"UserId\" = 1) AND (\"IsActive\" = 1)",
            pg:     "DELETE FROM \"users\" WHERE (\"UserId\" = 1) AND (\"IsActive\" = TRUE)",
            mysql:  "DELETE FROM `users` WHERE (`UserId` = 1) AND (`IsActive` = 1)",
            ss:     "DELETE FROM [users] WHERE ([UserId] = 1) AND ([IsActive] = 1)");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1)); // Alice: UserId=1, IsActive=true

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));
    }

    #endregion

    #region Contains (IN clause)

    private static readonly int[] _deleteIds = new[] { 1, 2 };

    [Test]
    public async Task Delete_Where_Contains()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Delete().Where(u => _deleteIds.Contains(u.UserId)).Prepare();
        var pg = Pg.Users().Delete().Where(u => _deleteIds.Contains(u.UserId)).Prepare();
        var my = My.Users().Delete().Where(u => _deleteIds.Contains(u.UserId)).Prepare();
        var ss = Ss.Users().Delete().Where(u => _deleteIds.Contains(u.UserId)).Prepare();

        // Static readonly with constant initializer → inlined literals
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" IN (1, 2)",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" IN (1, 2)",
            mysql:  "DELETE FROM `users` WHERE `UserId` IN (1, 2)",
            ss:     "DELETE FROM [users] WHERE [UserId] IN (1, 2)");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2)); // Alice (1) and Bob (2)

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(2));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(2));
    }

    [Test]
    public async Task Delete_Where_ListContains_RuntimeExpansion()
    {
        // Local List<int> is NOT constant-inlined — generator emits the runtime-expansion
        // path (one DbParameter per element). Mirrors the original GH-258 regression shape.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ids = new List<int> { 2, 3 };

        var ltAffected = await Lite.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(ltAffected, Is.EqualTo(2));
        var ltRemaining = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ltRemaining, Has.Count.EqualTo(1));
        Assert.That(ltRemaining[0], Is.EqualTo("Alice"));

        var pgAffected = await Pg.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));
        var pgRemaining = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(pgRemaining, Has.Count.EqualTo(1));
        Assert.That(pgRemaining[0], Is.EqualTo("Alice"));

        var myAffected = await My.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(2));
        var myRemaining = await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(myRemaining, Has.Count.EqualTo(1));
        Assert.That(myRemaining[0], Is.EqualTo("Alice"));

        var ssAffected = await Ss.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(2));
        var ssRemaining = await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ssRemaining, Has.Count.EqualTo(1));
        Assert.That(ssRemaining[0], Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Delete_Where_EnumerableContains_RuntimeExpansion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var sources = new[] { new { Id = 1 }, new { Id = 2 } };
        IEnumerable<int> ids = sources.Select(s => s.Id);

        var ltAffected = await Lite.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(ltAffected, Is.EqualTo(2));
        var ltRemaining = await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ltRemaining, Has.Count.EqualTo(1));
        Assert.That(ltRemaining[0], Is.EqualTo("Charlie"));

        var pgAffected = await Pg.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));
        var pgRemaining = await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(pgRemaining, Has.Count.EqualTo(1));
        Assert.That(pgRemaining[0], Is.EqualTo("Charlie"));

        var myAffected = await My.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(2));
        var myRemaining = await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(myRemaining, Has.Count.EqualTo(1));
        Assert.That(myRemaining[0], Is.EqualTo("Charlie"));

        var ssAffected = await Ss.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(2));
        var ssRemaining = await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        Assert.That(ssRemaining, Has.Count.EqualTo(1));
        Assert.That(ssRemaining[0], Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task Delete_Where_EmptyListContains_DeletesNothing()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ids = new List<int>();

        Assert.That(await Lite.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync(), Is.EqualTo(0));
        Assert.That(await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync(), Has.Count.EqualTo(3));

        Assert.That(await Pg.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync(), Is.EqualTo(0));
        Assert.That(await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync(), Has.Count.EqualTo(3));

        Assert.That(await My.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync(), Is.EqualTo(0));
        Assert.That(await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync(), Has.Count.EqualTo(3));

        Assert.That(await Ss.Users().Delete().Where(u => ids.Contains(u.UserId)).ExecuteNonQueryAsync(), Is.EqualTo(0));
        Assert.That(await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync(), Has.Count.EqualTo(3));
    }

    #endregion

    #region Other Entities

    [Test]
    public async Task Delete_Order_Where()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Delete().Where(o => o.OrderId == 42).Prepare();
        var pg = Pg.Orders().Delete().Where(o => o.OrderId == 42).Prepare();
        var my = My.Orders().Delete().Where(o => o.OrderId == 42).Prepare();
        var ss = Ss.Orders().Delete().Where(o => o.OrderId == 42).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"orders\" WHERE \"OrderId\" = 42",
            pg:     "DELETE FROM \"orders\" WHERE \"OrderId\" = 42",
            mysql:  "DELETE FROM `orders` WHERE `OrderId` = 42",
            ss:     "DELETE FROM [orders] WHERE [OrderId] = 42");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0)); // No order with ID 42

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(0));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(0));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(0));
    }

    #endregion
}
