using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectSelectTests : CrossDialectTestBase
{
    [Test]
    public void Select_Tuple_TwoColumns()
    {
        AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");
    }

    [Test]
    public void Select_Tuple_ThreeColumns()
    {
        AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");
    }

    [Test]
    public void Select_Dto_UserSummary()
    {
        AssertDialects(
            Lite.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Pg.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");
    }

    [Test]
    public void Select_Dto_UserWithEmail()
    {
        AssertDialects(
            Lite.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            Pg.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            My.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            Ss.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email] FROM [users]");
    }

    [Test]
    public void Select_OrdersTable_Tuple()
    {
        AssertDialects(
            Lite.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            Pg.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            My.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            Ss.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders`",
            ss:     "SELECT [OrderId], [Total] FROM [orders]");
    }

    [Test]
    public void Select_Distinct()
    {
        AssertDialects(
            Lite.Users().Distinct().ToDiagnostics(),
            Pg.Users().Distinct().ToDiagnostics(),
            My.Users().Distinct().ToDiagnostics(),
            Ss.Users().Distinct().ToDiagnostics(),
            sqlite: "SELECT DISTINCT * FROM \"users\"",
            pg:     "SELECT DISTINCT * FROM \"users\"",
            mysql:  "SELECT DISTINCT * FROM `users`",
            ss:     "SELECT DISTINCT * FROM [users]");
    }

    [Test]
    public void Select_Entity_User_AllColumns()
    {
        AssertDialects(
            Lite.Users().Select(u => u).ToDiagnostics(),
            Pg.Users().Select(u => u).ToDiagnostics(),
            My.Users().Select(u => u).ToDiagnostics(),
            Ss.Users().Select(u => u).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");
    }

    [Test]
    public void Select_Entity_Order_WithForeignKey()
    {
        AssertDialects(
            Lite.Orders().Select(o => o).ToDiagnostics(),
            Pg.Orders().Select(o => o).ToDiagnostics(),
            My.Orders().Select(o => o).ToDiagnostics(),
            Ss.Orders().Select(o => o).ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders`",
            ss:     "SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders]");
    }

    [Test]
    public void Pagination_LimitOffset()
    {
        AssertDialects(
            Lite.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            Pg.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            My.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            Ss.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" LIMIT 10 OFFSET 20",
            pg:     "SELECT * FROM \"users\" LIMIT 10 OFFSET 20",
            mysql:  "SELECT * FROM `users` LIMIT 10 OFFSET 20",
            ss:     "SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");
    }

    [Test]
    public void Pagination_LimitOnly()
    {
        AssertDialects(
            Lite.Users().Where(u => true).Limit(5).ToDiagnostics(),
            Pg.Users().Where(u => true).Limit(5).ToDiagnostics(),
            My.Users().Where(u => true).Limit(5).ToDiagnostics(),
            Ss.Users().Where(u => true).Limit(5).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" LIMIT 5",
            pg:     "SELECT * FROM \"users\" LIMIT 5",
            mysql:  "SELECT * FROM `users` LIMIT 5",
            ss:     "SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
    }
}
