using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectSelectTests
{
    [Test]
    public async Task Select_Tuple_TwoColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Select_Tuple_ThreeColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice", true)));
        Assert.That(results[1], Is.EqualTo((2, "Bob", true)));
        Assert.That(results[2], Is.EqualTo((3, "Charlie", false)));
    }

    [Test]
    public async Task Select_Dto_UserSummary()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Pg.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");

        var results = await Lite.Users()
            .Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive })
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));

        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].IsActive, Is.True);

        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].IsActive, Is.True);

        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[2].IsActive, Is.False);
    }

    [Test]
    public async Task Select_Dto_UserWithEmail()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            Pg.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            My.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            Ss.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email] FROM [users]");

        var results = await Lite.Users()
            .Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email })
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));

        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].Email, Is.EqualTo("alice@test.com"));

        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].Email, Is.Null);

        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].Email, Is.EqualTo("charlie@test.com"));
    }

    [Test]
    public async Task Select_OrdersTable_Tuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Select(o => (o.OrderId, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            My.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            Ss.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders`",
            ss:     "SELECT [OrderId], [Total] FROM [orders]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((2, 75.50m)));
        Assert.That(results[2], Is.EqualTo((3, 150.00m)));
    }

    [Test]
    public async Task Select_Distinct()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Distinct().ToDiagnostics(),
            Pg.Users().Distinct().ToDiagnostics(),
            My.Users().Distinct().ToDiagnostics(),
            Ss.Users().Distinct().ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT DISTINCT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT DISTINCT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");
    }

    [Test]
    public async Task Select_Entity_User_AllColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
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
    public async Task Select_Entity_Order_WithForeignKey()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
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
    public async Task Pagination_LimitOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            Pg.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            My.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            Ss.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" LIMIT 10 OFFSET 20",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" LIMIT 10 OFFSET 20",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` LIMIT 10 OFFSET 20",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");

        // Execution: skip 1, take 2 using Select tuple for verifiable results
        var results = await Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(2).Offset(1)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Pagination_LimitOnly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => true).Limit(5).ToDiagnostics(),
            Pg.Users().Where(u => true).Limit(5).ToDiagnostics(),
            My.Users().Where(u => true).Limit(5).ToDiagnostics(),
            Ss.Users().Where(u => true).Limit(5).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" LIMIT 5",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" LIMIT 5",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` LIMIT 5",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
    }

    [Test]
    public async Task Pagination_LiteralLimit_ParameterizedOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Use a variable (not const) so the generator treats offset as parameterized
        int offset = 1;

        QueryTestHarness.AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 2 OFFSET @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 2 OFFSET $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT 2 OFFSET ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET @p0 ROWS FETCH NEXT 2 ROWS ONLY");

        // Execution: skip 1 user, take 2 → should get Bob and Charlie
        var results = await Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(2).Offset(offset)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Pagination_ParameterizedLimit_LiteralOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Inverse mixed case: parameterized limit, literal offset
        int limit = 2;

        QueryTestHarness.AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT @p0 OFFSET 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT $1 OFFSET 1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT ? OFFSET 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET 1 ROWS FETCH NEXT @p0 ROWS ONLY");

        // Execution: skip 1, take 2 → should get Bob and Charlie
        var results = await Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(limit).Offset(1)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Select_NamedTuple_TwoColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (Id: u.UserId, Name: u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Select(u => (Id: u.UserId, Name: u.UserName)).ToDiagnostics(),
            My.Users().Select(u => (Id: u.UserId, Name: u.UserName)).ToDiagnostics(),
            Ss.Users().Select(u => (Id: u.UserId, Name: u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        // Verify named element access works
        Assert.That(results[0].Id, Is.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("Alice"));
        Assert.That(results[1].Id, Is.EqualTo(2));
        Assert.That(results[1].Name, Is.EqualTo("Bob"));
        Assert.That(results[2].Id, Is.EqualTo(3));
        Assert.That(results[2].Name, Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task Select_NamedTuple_ThreeColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).ToDiagnostics(),
            My.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).ToDiagnostics(),
            Ss.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].Id, Is.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("Alice"));
        Assert.That(results[0].Active, Is.True);
        Assert.That(results[2].Id, Is.EqualTo(3));
        Assert.That(results[2].Active, Is.False);
    }

    [Test]
    public async Task Pagination_BothParameterized()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Both parameterized
        int limit = 2;
        int offset = 1;

        QueryTestHarness.AssertDialects(
            Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT @p0 OFFSET @p1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT $1 OFFSET $2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT ? OFFSET ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET @p1 ROWS FETCH NEXT @p0 ROWS ONLY");

        // Execution: skip 1, take 2 → should get Bob and Charlie
        var results = await Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(limit).Offset(offset)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }
}
