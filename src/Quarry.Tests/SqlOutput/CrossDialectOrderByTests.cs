using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectOrderByTests : CrossDialectTestBase
{
    #region Single Entity OrderBy

    [Test]
    public void OrderBy_SingleColumn_Asc()
    {
        AssertDialects(
            Lite.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ToTestCase(),
            Pg.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ToTestCase(),
            My.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ToTestCase(),
            Ss.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` ORDER BY `UserName` ASC",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY [UserName] ASC");
    }

    [Test]
    public void OrderBy_SingleColumn_Desc()
    {
        AssertDialects(
            Lite.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).ToTestCase(),
            Pg.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).ToTestCase(),
            My.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).ToTestCase(),
            Ss.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.CreatedAt, Direction.Descending).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"CreatedAt\" DESC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"CreatedAt\" DESC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` ORDER BY `CreatedAt` DESC",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY [CreatedAt] DESC");
    }

    #endregion

    #region ThenBy

    [Test]
    public void OrderBy_ThenBy_MultiColumn()
    {
        AssertDialects(
            Lite.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).ToTestCase(),
            Pg.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).ToTestCase(),
            My.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).ToTestCase(),
            Ss.Users.Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserName).ThenBy(u => u.CreatedAt).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC, \"CreatedAt\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserName\" ASC, \"CreatedAt\" ASC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` ORDER BY `UserName` ASC, `CreatedAt` ASC",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY [UserName] ASC, [CreatedAt] ASC");
    }

    #endregion

    #region Joined OrderBy

    [Test]
    public void OrderBy_Joined_RightTableColumn()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => o.Total).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"Total\" ASC",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"Total\" ASC",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`Total` ASC",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[Total] ASC");
    }

    #endregion
}
