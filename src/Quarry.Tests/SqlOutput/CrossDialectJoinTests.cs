using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectJoinTests : CrossDialectTestBase
{
    #region Inner Join

    [Test]
    public void Join_InnerJoin_OnClause()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region Join + Where

    [Test]
    public void Join_WithWhere_OnLeftTable()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive]");
    }

    [Test]
    public void Join_WithWhere_OnRightTable()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 100",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 100");
    }

    #endregion

    #region Join + Tuple Projection Columns

    [Test]
    public void Join_Select_Tuple_ColumnQuoting()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region Left Join

    [Test]
    public void LeftJoin_Select()
    {
        AssertDialects(
            Lite.Users.LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Pg.Users.LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            My.Users.LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Ss.Users.LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region Right Join

    [Test]
    public void RightJoin_Select()
    {
        AssertDialects(
            Lite.Users.RightJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Pg.Users.RightJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            My.Users.RightJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            Ss.Users.RightJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\" FROM `users` AS `t0` RIGHT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\" FROM [users] AS [t0] RIGHT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region 3-Table Join

    [Test]
    public void Join_ThreeTable_Select()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId]");
    }

    #endregion

    #region 4-Table Join

    [Test]
    public void Join_FourTable_Select()
    {
        AssertDialects(
            Lite.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).ToTestCase(),
            Pg.Users.Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Pg.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).ToTestCase(),
            My.Users.Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<My.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).ToTestCase(),
            Ss.Users.Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Ss.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).ToTestCase(),
            sqlite: "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\", t3.\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"accounts\" AS \"t3\" ON \"t0\".\"UserId\" = \"t3\".\"UserId\"",
            pg:     "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\", t3.\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"accounts\" AS \"t3\" ON \"t0\".\"UserId\" = \"t3\".\"UserId\"",
            mysql:  "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\", t3.\"AccountName\" FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` INNER JOIN `accounts` AS `t3` ON `t0`.`UserId` = `t3`.`UserId`",
            ss:     "SELECT t0.\"UserName\", t1.\"Total\", t2.\"ProductName\", t3.\"AccountName\" FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] INNER JOIN [accounts] AS [t3] ON [t0].[UserId] = [t3].[UserId]");
    }

    #endregion
}
