using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Complex multi-clause cross-dialect tests. Each test exercises multiple
/// builder methods chained together, verifying the full generated SQL.
/// </summary>
[TestFixture]
internal class CrossDialectComplexTests : CrossDialectTestBase
{
    #region Where + Select

    [Test]
    public void Where_Comparison_ThenSelect_Tuple()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Pg.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            My.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Ss.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" > 10",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" > 10",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` > 10",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] > 10");
    }

    #endregion

    #region Multiple Where + Select

    [Test]
    public void Where_NullCheck_And_Boolean_ThenSelect()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            Pg.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            My.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            Ss.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = 1)",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE (`Email` IS NOT NULL) AND (`IsActive` = 1)",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE ([Email] IS NOT NULL) AND ([IsActive] = 1)");
    }

    [Test]
    public void Where_Boolean_And_Comparison_ThenSelect()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE (\"IsActive\" = 1) AND (\"UserId\" > 5)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE (\"IsActive\" = TRUE) AND (\"UserId\" > 5)",
            mysql:  "SELECT `UserId`, `UserName`, `Email` FROM `users` WHERE (`IsActive` = 1) AND (`UserId` > 5)",
            ss:     "SELECT [UserId], [UserName], [Email] FROM [users] WHERE ([IsActive] = 1) AND ([UserId] > 5)");
    }

    #endregion

    #region Distinct + Where + Select

    [Test]
    public void Distinct_Where_Select()
    {
        AssertDialects(
            Lite.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            Pg.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            My.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            Ss.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT DISTINCT `UserName`, `Email` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT DISTINCT [UserName], [Email] FROM [users] WHERE [IsActive] = 1");
    }

    #endregion

    #region Where + Select + Pagination

    [Test]
    public void Where_Select_LimitOffset()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1 LIMIT 10 OFFSET 20",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = TRUE LIMIT 10 OFFSET 20",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE `IsActive` = 1 LIMIT 10 OFFSET 20",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE [IsActive] = 1 ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");
    }

    [Test]
    public void Where_Select_Limit()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 LIMIT 5",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE LIMIT 5",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1 LIMIT 5",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1 ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
    }

    #endregion

    #region Orders Table — Complex

    [Test]
    public void Orders_Where_Comparison_Select()
    {
        AssertDialects(
            Lite.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).ToDiagnostics(),
            Pg.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).ToDiagnostics(),
            My.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).ToDiagnostics(),
            Ss.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100",
            pg:     "SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100",
            mysql:  "SELECT `OrderId`, `Total`, `Status` FROM `orders` WHERE `Total` > 100",
            ss:     "SELECT [OrderId], [Total], [Status] FROM [orders] WHERE [Total] > 100");
    }

    #endregion

    #region Join + Where + Select

    [Test]
    public void Join_Where_Boolean_Select()
    {
        AssertDialects(
            Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToDiagnostics(),
            Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToDiagnostics(),
            My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToDiagnostics(),
            Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 1");
    }

    [Test]
    public void Join_Where_RightTable_Select()
    {
        AssertDialects(
            Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).ToDiagnostics(),
            Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).ToDiagnostics(),
            My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).ToDiagnostics(),
            Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 50",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 50",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t1`.`Status` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 50",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t1].[Status] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 50");
    }

    #endregion
}
