using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectOrderByTests
{
    #region Single Entity OrderBy

    [Test]
    public async Task OrderBy_SingleColumn_Asc()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` ORDER BY `UserName` ASC",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY [UserName] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task OrderBy_SingleColumn_Desc()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"CreatedAt\" DESC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"CreatedAt\" DESC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` ORDER BY `CreatedAt` DESC",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY [CreatedAt] DESC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        // Charlie created 2024-03-10, Bob 2024-02-20, Alice 2024-01-15
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region ThenBy

    [Test]
    public async Task OrderBy_ThenBy_MultiColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC, \"CreatedAt\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC, \"CreatedAt\" ASC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` ORDER BY `UserName` ASC, `CreatedAt` ASC",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY [UserName] ASC, [CreatedAt] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Joined OrderBy

    [Test]
    public async Task OrderBy_Joined_RightTableColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"Total\" ASC",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"Total\" ASC",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`Total` ASC",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[Total] ASC");

        // Join uses "Order" view which maps to "orders" table
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        // Ordered by Total ASC: 75.50, 150.00, 250.00
        Assert.That(results[0], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));
        Assert.That(results[2], Is.EqualTo(("Alice", 250.00m)));
    }

    #endregion
}
