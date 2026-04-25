using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectSelectTests
{
    #region Tuple Projections

    [Test]
    public async Task Select_Tuple_TwoColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));
        Assert.That(pgResults[2], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
        Assert.That(myResults[2], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Select_Tuple_ThreeColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice", true)));
        Assert.That(results[1], Is.EqualTo((2, "Bob", true)));
        Assert.That(results[2], Is.EqualTo((3, "Charlie", false)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice", true)));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob", true)));
        Assert.That(pgResults[2], Is.EqualTo((3, "Charlie", false)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice", true)));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob", true)));
        Assert.That(myResults[2], Is.EqualTo((3, "Charlie", false)));
    }

    #endregion

    #region Named Tuple Projections

    [Test]
    public async Task Select_NamedTuple_TwoColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (Id: u.UserId, Name: u.UserName)).Prepare();
        var pg = Pg.Users().Select(u => (Id: u.UserId, Name: u.UserName)).Prepare();
        var my = My.Users().Select(u => (Id: u.UserId, Name: u.UserName)).Prepare();
        var ss = Ss.Users().Select(u => (Id: u.UserId, Name: u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        // Verify named element access works
        Assert.That(results[0].Id, Is.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("Alice"));
        Assert.That(results[1].Id, Is.EqualTo(2));
        Assert.That(results[1].Name, Is.EqualTo("Bob"));
        Assert.That(results[2].Id, Is.EqualTo(3));
        Assert.That(results[2].Name, Is.EqualTo("Charlie"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].Id, Is.EqualTo(1));
        Assert.That(pgResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(pgResults[1].Id, Is.EqualTo(2));
        Assert.That(pgResults[1].Name, Is.EqualTo("Bob"));
        Assert.That(pgResults[2].Id, Is.EqualTo(3));
        Assert.That(pgResults[2].Name, Is.EqualTo("Charlie"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].Id, Is.EqualTo(1));
        Assert.That(myResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(myResults[1].Id, Is.EqualTo(2));
        Assert.That(myResults[1].Name, Is.EqualTo("Bob"));
        Assert.That(myResults[2].Id, Is.EqualTo(3));
        Assert.That(myResults[2].Name, Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task Select_NamedTuple_ThreeColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).Prepare();
        var pg = Pg.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).Prepare();
        var my = My.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).Prepare();
        var ss = Ss.Users().Select(u => (Id: u.UserId, Name: u.UserName, Active: u.IsActive)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].Id, Is.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("Alice"));
        Assert.That(results[0].Active, Is.True);
        Assert.That(results[2].Id, Is.EqualTo(3));
        Assert.That(results[2].Active, Is.False);

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].Id, Is.EqualTo(1));
        Assert.That(pgResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].Active, Is.True);
        Assert.That(pgResults[2].Id, Is.EqualTo(3));
        Assert.That(pgResults[2].Active, Is.False);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].Id, Is.EqualTo(1));
        Assert.That(myResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(myResults[0].Active, Is.True);
        Assert.That(myResults[2].Id, Is.EqualTo(3));
        Assert.That(myResults[2].Active, Is.False);
    }

    #endregion

    #region DTO Projections

    [Test]
    public async Task Select_Dto_UserSummary()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].IsActive, Is.True);

        Assert.That(pgResults[1].UserId, Is.EqualTo(2));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(pgResults[1].IsActive, Is.True);

        Assert.That(pgResults[2].UserId, Is.EqualTo(3));
        Assert.That(pgResults[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(pgResults[2].IsActive, Is.False);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[0].IsActive, Is.True);

        Assert.That(myResults[1].UserId, Is.EqualTo(2));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(myResults[1].IsActive, Is.True);

        Assert.That(myResults[2].UserId, Is.EqualTo(3));
        Assert.That(myResults[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(myResults[2].IsActive, Is.False);
    }

    [Test]
    public async Task Select_Dto_UserWithEmail()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).Prepare();
        var pg = Pg.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).Prepare();
        var my = My.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).Prepare();
        var ss = Ss.Users().Select(u => new UserWithEmailDto { UserId = u.UserId, UserName = u.UserName, Email = u.Email }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].Email, Is.EqualTo("alice@test.com"));

        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].Email, Is.Null);

        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].Email, Is.EqualTo("charlie@test.com"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].Email, Is.EqualTo("alice@test.com"));

        Assert.That(pgResults[1].UserId, Is.EqualTo(2));
        Assert.That(pgResults[1].Email, Is.Null);

        Assert.That(pgResults[2].UserId, Is.EqualTo(3));
        Assert.That(pgResults[2].Email, Is.EqualTo("charlie@test.com"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].Email, Is.EqualTo("alice@test.com"));

        Assert.That(myResults[1].UserId, Is.EqualTo(2));
        Assert.That(myResults[1].Email, Is.Null);

        Assert.That(myResults[2].UserId, Is.EqualTo(3));
        Assert.That(myResults[2].Email, Is.EqualTo("charlie@test.com"));
    }

    #endregion

    #region Entity Projections

    [Test]
    public async Task Select_Entity_User_AllColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u).Prepare();
        var pg = Pg.Users().Select(u => u).Prepare();
        var my = My.Users().Select(u => u).Prepare();
        var ss = Ss.Users().Select(u => u).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].Email, Is.EqualTo("alice@test.com"));
        Assert.That(results[0].IsActive, Is.True);
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].Email, Is.Null);
        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[2].IsActive, Is.False);

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].Email, Is.EqualTo("alice@test.com"));
        Assert.That(pgResults[0].IsActive, Is.True);
        Assert.That(pgResults[1].UserId, Is.EqualTo(2));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(pgResults[1].Email, Is.Null);
        Assert.That(pgResults[2].UserId, Is.EqualTo(3));
        Assert.That(pgResults[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(pgResults[2].IsActive, Is.False);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[0].Email, Is.EqualTo("alice@test.com"));
        Assert.That(myResults[0].IsActive, Is.True);
        Assert.That(myResults[1].UserId, Is.EqualTo(2));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(myResults[1].Email, Is.Null);
        Assert.That(myResults[2].UserId, Is.EqualTo(3));
        Assert.That(myResults[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(myResults[2].IsActive, Is.False);
    }

    [Test]
    public async Task Select_Entity_Order_WithForeignKey()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Select(o => o).Prepare();
        var pg = Pg.Orders().Select(o => o).Prepare();
        var my = My.Orders().Select(o => o).Prepare();
        var ss = Ss.Orders().Select(o => o).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders`",
            ss:     "SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].Total, Is.EqualTo(250.00m));
        Assert.That(results[0].Status, Is.EqualTo("Shipped"));
        Assert.That(results[1].OrderId, Is.EqualTo(2));
        Assert.That(results[1].Total, Is.EqualTo(75.50m));
        Assert.That(results[2].OrderId, Is.EqualTo(3));
        Assert.That(results[2].Total, Is.EqualTo(150.00m));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].OrderId, Is.EqualTo(1));
        Assert.That(pgResults[0].Total, Is.EqualTo(250.00m));
        Assert.That(pgResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(pgResults[1].OrderId, Is.EqualTo(2));
        Assert.That(pgResults[1].Total, Is.EqualTo(75.50m));
        Assert.That(pgResults[2].OrderId, Is.EqualTo(3));
        Assert.That(pgResults[2].Total, Is.EqualTo(150.00m));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].OrderId, Is.EqualTo(1));
        Assert.That(myResults[0].Total, Is.EqualTo(250.00m));
        Assert.That(myResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(myResults[1].OrderId, Is.EqualTo(2));
        Assert.That(myResults[1].Total, Is.EqualTo(75.50m));
        Assert.That(myResults[2].OrderId, Is.EqualTo(3));
        Assert.That(myResults[2].Total, Is.EqualTo(150.00m));
    }

    [Test]
    public async Task Select_Distinct()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Distinct().Select(u => u).Prepare();
        var pg = Pg.Users().Distinct().Select(u => u).Prepare();
        var my = My.Users().Distinct().Select(u => u).Prepare();
        var ss = Ss.Users().Distinct().Select(u => u).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT DISTINCT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT DISTINCT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].IsActive, Is.True);
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[2].IsActive, Is.False);

        // PG does not guarantee row order without an explicit ORDER BY (SQLite happens to
        // preserve insertion order). Sort by UserId to match the Lite assertion shape.
        var pgResults = await pg.ExecuteFetchAllAsync().SortedByAsync(r => r.UserId);
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].IsActive, Is.True);
        Assert.That(pgResults[1].UserId, Is.EqualTo(2));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(pgResults[2].UserId, Is.EqualTo(3));
        Assert.That(pgResults[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(pgResults[2].IsActive, Is.False);

        var myResults = await my.ExecuteFetchAllAsync().SortedByAsync(r => r.UserId);
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[0].IsActive, Is.True);
        Assert.That(myResults[1].UserId, Is.EqualTo(2));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(myResults[2].UserId, Is.EqualTo(3));
        Assert.That(myResults[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(myResults[2].IsActive, Is.False);
    }

    #endregion

    #region Table Select

    [Test]
    public async Task Select_OrdersTable_Tuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Select(o => (o.OrderId, o.Total)).Prepare();
        var pg = Pg.Orders().Select(o => (o.OrderId, o.Total)).Prepare();
        var my = My.Orders().Select(o => (o.OrderId, o.Total)).Prepare();
        var ss = Ss.Orders().Select(o => (o.OrderId, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders`",
            ss:     "SELECT [OrderId], [Total] FROM [orders]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((2, 75.50m)));
        Assert.That(results[2], Is.EqualTo((3, 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo((1, 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo((2, 75.50m)));
        Assert.That(pgResults[2], Is.EqualTo((3, 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo((1, 250.00m)));
        Assert.That(myResults[1], Is.EqualTo((2, 75.50m)));
        Assert.That(myResults[2], Is.EqualTo((3, 150.00m)));
    }

    #endregion

    #region Pagination

    [Test]
    public async Task Pagination_LimitOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(1).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(1).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(1).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 2 OFFSET 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 2 OFFSET 1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT 2 OFFSET 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Pagination_LimitOnly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 5",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 5",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT 5",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));
        Assert.That(pgResults[2], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
        Assert.That(myResults[2], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Pagination_LiteralLimit_ParameterizedOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Use a variable (not const) so the generator treats offset as parameterized
        int offset = 1;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(2).Offset(offset).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 2 OFFSET @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT 2 OFFSET $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT 2 OFFSET ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET @p0 ROWS FETCH NEXT 2 ROWS ONLY");

        // Execution: skip 1 user, take 2 → should get Bob and Charlie
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Pagination_ParameterizedLimit_LiteralOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Inverse mixed case: parameterized limit, literal offset
        int limit = 2;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT @p0 OFFSET 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT $1 OFFSET 1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT ? OFFSET 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET 1 ROWS FETCH NEXT @p0 ROWS ONLY");

        // Execution: skip 1, take 2 → should get Bob and Charlie
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Pagination_BothParameterized()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Both parameterized
        int limit = 2;
        int offset = 1;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName)).Limit(limit).Offset(offset).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT @p0 OFFSET @p1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" LIMIT $1 OFFSET $2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` LIMIT ? OFFSET ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] ORDER BY (SELECT NULL) OFFSET @p1 ROWS FETCH NEXT @p0 ROWS ONLY");

        // Execution: skip 1, take 2 → should get Bob and Charlie
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region No-Select execution terminals (IQueryBuilder<T> terminals — result type is entity T)

    [Test]
    public async Task NoSelect_ExecuteFetchAllAsync_ReturnsAllEntities()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Prepare();
        var my = My.Users().Where(u => u.IsActive).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[1].UserId, Is.EqualTo(2));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[1].UserId, Is.EqualTo(2));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task NoSelect_ExecuteFetchFirstAsync_ReturnsFirstEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Prepare();
        var my = My.Users().Where(u => u.IsActive).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1");

        var result = await lt.ExecuteFetchFirstAsync();
        Assert.That(result.UserId, Is.EqualTo(1));
        Assert.That(result.UserName, Is.EqualTo("Alice"));
        Assert.That(result.Email, Is.EqualTo("alice@test.com"));
        Assert.That(result.IsActive, Is.True);

        var pgResult = await pg.ExecuteFetchFirstAsync();
        Assert.That(pgResult.UserId, Is.EqualTo(1));
        Assert.That(pgResult.UserName, Is.EqualTo("Alice"));
        Assert.That(pgResult.Email, Is.EqualTo("alice@test.com"));
        Assert.That(pgResult.IsActive, Is.True);

        var myResult = await my.ExecuteFetchFirstAsync();
        Assert.That(myResult.UserId, Is.EqualTo(1));
        Assert.That(myResult.UserName, Is.EqualTo("Alice"));
        Assert.That(myResult.Email, Is.EqualTo("alice@test.com"));
        Assert.That(myResult.IsActive, Is.True);
    }

    [Test]
    public async Task NoSelect_ExecuteFetchFirstOrDefaultAsync_ReturnsEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var result = await lt.ExecuteFetchFirstOrDefaultAsync();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserId, Is.EqualTo(1));
        Assert.That(result.UserName, Is.EqualTo("Alice"));

        var pgResult = await pg.ExecuteFetchFirstOrDefaultAsync();
        Assert.That(pgResult, Is.Not.Null);
        Assert.That(pgResult!.UserId, Is.EqualTo(1));
        Assert.That(pgResult.UserName, Is.EqualTo("Alice"));

        var myResult = await my.ExecuteFetchFirstOrDefaultAsync();
        Assert.That(myResult, Is.Not.Null);
        Assert.That(myResult!.UserId, Is.EqualTo(1));
        Assert.That(myResult.UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task NoSelect_ExecuteFetchFirstOrDefaultAsync_ReturnsNullForNoMatch()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, _) = t;

        var lt = Lite.Users().Where(u => u.UserId == 999).Prepare();
        var result = await lt.ExecuteFetchFirstOrDefaultAsync();
        Assert.That(result, Is.Null);

        var pg = Pg.Users().Where(u => u.UserId == 999).Prepare();
        var pgResult = await pg.ExecuteFetchFirstOrDefaultAsync();
        Assert.That(pgResult, Is.Null);

        var my = My.Users().Where(u => u.UserId == 999).Prepare();
        var myResult = await my.ExecuteFetchFirstOrDefaultAsync();
        Assert.That(myResult, Is.Null);
    }

    [Test]
    public async Task NoSelect_ExecuteFetchSingleAsync_ReturnsSingleEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserId == 2).Prepare();
        var pg = Pg.Users().Where(u => u.UserId == 2).Prepare();
        var my = My.Users().Where(u => u.UserId == 2).Prepare();
        var ss = Ss.Users().Where(u => u.UserId == 2).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 2",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 2",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 2",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 2");

        var result = await lt.ExecuteFetchSingleAsync();
        Assert.That(result.UserId, Is.EqualTo(2));
        Assert.That(result.UserName, Is.EqualTo("Bob"));
        Assert.That(result.Email, Is.Null);

        var pgResult = await pg.ExecuteFetchSingleAsync();
        Assert.That(pgResult.UserId, Is.EqualTo(2));
        Assert.That(pgResult.UserName, Is.EqualTo("Bob"));
        Assert.That(pgResult.Email, Is.Null);

        var myResult = await my.ExecuteFetchSingleAsync();
        Assert.That(myResult.UserId, Is.EqualTo(2));
        Assert.That(myResult.UserName, Is.EqualTo("Bob"));
        Assert.That(myResult.Email, Is.Null);
    }

    [Test]
    public async Task NoSelect_ExecuteFetchSingleOrDefaultAsync_ReturnsEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserId == 2).Prepare();
        var pg = Pg.Users().Where(u => u.UserId == 2).Prepare();
        var my = My.Users().Where(u => u.UserId == 2).Prepare();
        var ss = Ss.Users().Where(u => u.UserId == 2).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 2",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 2",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 2",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 2");

        var result = await lt.ExecuteFetchSingleOrDefaultAsync();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserId, Is.EqualTo(2));
        Assert.That(result.UserName, Is.EqualTo("Bob"));

        var pgResult = await pg.ExecuteFetchSingleOrDefaultAsync();
        Assert.That(pgResult, Is.Not.Null);
        Assert.That(pgResult!.UserId, Is.EqualTo(2));
        Assert.That(pgResult.UserName, Is.EqualTo("Bob"));

        var myResult = await my.ExecuteFetchSingleOrDefaultAsync();
        Assert.That(myResult, Is.Not.Null);
        Assert.That(myResult!.UserId, Is.EqualTo(2));
        Assert.That(myResult.UserName, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task NoSelect_ExecuteFetchSingleOrDefaultAsync_ReturnsNullForNoMatch()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, _) = t;

        var lt = Lite.Users().Where(u => u.UserId == 999).Prepare();
        var result = await lt.ExecuteFetchSingleOrDefaultAsync();
        Assert.That(result, Is.Null);

        var pg = Pg.Users().Where(u => u.UserId == 999).Prepare();
        var pgResult = await pg.ExecuteFetchSingleOrDefaultAsync();
        Assert.That(pgResult, Is.Null);

        var my = My.Users().Where(u => u.UserId == 999).Prepare();
        var myResult = await my.ExecuteFetchSingleOrDefaultAsync();
        Assert.That(myResult, Is.Null);
    }

    [Test]
    public async Task NoSelect_ExecuteFetchSingleOrDefaultAsync_ThrowsOnMultipleRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, _) = t;

        // IsActive matches 2 rows (Alice + Bob) — SingleOrDefault must throw
        var lt = Lite.Users().Where(u => u.IsActive).Prepare();
        try
        {
            await lt.ExecuteFetchSingleOrDefaultAsync();
            Assert.Fail("Expected InvalidOperationException for multiple rows");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message, Does.Contain("more than one element"));
        }

        var pg = Pg.Users().Where(u => u.IsActive).Prepare();
        try
        {
            await pg.ExecuteFetchSingleOrDefaultAsync();
            Assert.Fail("Expected InvalidOperationException for multiple rows");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message, Does.Contain("more than one element"));
        }

        var my = My.Users().Where(u => u.IsActive).Prepare();
        try
        {
            await my.ExecuteFetchSingleOrDefaultAsync();
            Assert.Fail("Expected InvalidOperationException for multiple rows");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message, Does.Contain("more than one element"));
        }
    }

    [Test]
    public async Task NoSelect_ExecuteFetchAllAsync_ReturnsAllRowsWithDistinct()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Distinct().Prepare();
        var pg = Pg.Users().Distinct().Prepare();
        var my = My.Users().Distinct().Prepare();
        var ss = Ss.Users().Distinct().Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT DISTINCT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT DISTINCT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
    }

    #endregion
}
