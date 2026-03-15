using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectSelectTests : CrossDialectTestBase
{
    [Test]
    public void Select_Tuple_TwoColumns()
    {
        AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName)).ToTestCase(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).ToTestCase(),
            My.Users().Select(u => (u.UserId, u.UserName)).ToTestCase(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");
    }

    [Test]
    public void Select_Tuple_ThreeColumns()
    {
        AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToTestCase(),
            Pg.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToTestCase(),
            My.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToTestCase(),
            Ss.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");
    }

    [Test]
    public void Select_Dto_UserSummary()
    {
        AssertDialects(
            Lite.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Pg.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            My.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Ss.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");
    }

    [Test]
    public void Select_Dto_UserWithEmail()
    {
        AssertDialects(
            Lite.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToTestCase(),
            Pg.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToTestCase(),
            My.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToTestCase(),
            Ss.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email] FROM [users]");
    }

    [Test]
    public void Select_OrdersTable_Tuple()
    {
        AssertDialects(
            Lite.Orders().Select(o => (o.OrderId, o.Total)).ToTestCase(),
            Pg.Orders().Select(o => (o.OrderId, o.Total)).ToTestCase(),
            My.Orders().Select(o => (o.OrderId, o.Total)).ToTestCase(),
            Ss.Orders().Select(o => (o.OrderId, o.Total)).ToTestCase(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders`",
            ss:     "SELECT [OrderId], [Total] FROM [orders]");
    }

    [Test]
    public void Select_Distinct()
    {
        AssertDialects(
            Lite.Users().Distinct().ToTestCase(),
            Pg.Users().Distinct().ToTestCase(),
            My.Users().Distinct().ToTestCase(),
            Ss.Users().Distinct().ToTestCase(),
            sqlite: "SELECT DISTINCT * FROM \"users\"",
            pg:     "SELECT DISTINCT * FROM \"users\"",
            mysql:  "SELECT DISTINCT * FROM `users`",
            ss:     "SELECT DISTINCT * FROM [users]");
    }

    [Test]
    public void Select_Entity_User_AllColumns()
    {
        AssertDialects(
            Lite.Users().Select(u => u).ToTestCase(),
            Pg.Users().Select(u => u).ToTestCase(),
            My.Users().Select(u => u).ToTestCase(),
            Ss.Users().Select(u => u).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");
    }

    [Test]
    public void Select_Entity_Order_WithForeignKey()
    {
        AssertDialects(
            Lite.Orders().Select(o => o).ToTestCase(),
            Pg.Orders().Select(o => o).ToTestCase(),
            My.Orders().Select(o => o).ToTestCase(),
            Ss.Orders().Select(o => o).ToTestCase(),
            sqlite: "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders`",
            ss:     "SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders]");
    }

    [Test]
    public void Pagination_LimitOffset()
    {
        AssertDialects(
            Lite.Users().Limit(10).Offset(20).ToTestCase(),
            Pg.Users().Limit(10).Offset(20).ToTestCase(),
            My.Users().Limit(10).Offset(20).ToTestCase(),
            Ss.Users().Limit(10).Offset(20).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" LIMIT 10 OFFSET 20",
            pg:     "SELECT * FROM \"users\" LIMIT 10 OFFSET 20",
            mysql:  "SELECT * FROM `users` LIMIT 10 OFFSET 20",
            ss:     "SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");
    }

    [Test]
    public void Pagination_LimitOnly()
    {
        AssertDialects(
            Lite.Users().Limit(5).ToTestCase(),
            Pg.Users().Limit(5).ToTestCase(),
            My.Users().Limit(5).ToTestCase(),
            Ss.Users().Limit(5).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" LIMIT 5",
            pg:     "SELECT * FROM \"users\" LIMIT 5",
            mysql:  "SELECT * FROM `users` LIMIT 5",
            ss:     "SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
    }
}
