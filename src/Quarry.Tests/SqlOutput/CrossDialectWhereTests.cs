using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectWhereTests
{
    #region Boolean

    [Test]
    public async Task Where_BooleanProperty()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1");

        // Execution: 2 active users
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_NegatedBoolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => !u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => !u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => !u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => !u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = FALSE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 0",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 0");

        // Execution: 1 inactive user
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Comparison Operators

    [Test]
    public async Task Where_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserId > 1).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserId > 1).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserId > 1).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserId > 1).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" > 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" > 1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` > 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] > 1");

        // Execution: 2 users with UserId > 1
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
    public async Task Where_LessThanOrEqual()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserId <= 2).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserId <= 2).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserId <= 2).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserId <= 2).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= 2",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= 2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` <= 2",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] <= 2");

        // Execution: 2 users with UserId <= 2
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region Null Checks

    [Test]
    public async Task Where_IsNull()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"Email\" IS NULL",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"Email\" IS NULL",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `Email` IS NULL",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [Email] IS NULL");

        // Execution: Bob has null email
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_IsNotNull()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Email != null).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Email != null).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Email != null).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Email != null).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"Email\" IS NOT NULL",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"Email\" IS NOT NULL",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `Email` IS NOT NULL",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [Email] IS NOT NULL");

        // Execution: Alice and Charlie have non-null emails
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Chained Where

    [Test]
    public async Task Where_MultipleChained()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"IsActive\" = 1) AND (\"UserId\" > 0)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"IsActive\" = TRUE) AND (\"UserId\" > 0)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (`IsActive` = 1) AND (`UserId` > 0)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE ([IsActive] = 1) AND ([UserId] > 0)");

        // Execution: 2 active users with UserId > 0
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_NullCheck_And_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = 1)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (`Email` IS NOT NULL) AND (`IsActive` = 1)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE ([Email] IS NOT NULL) AND ([IsActive] = 1)");

        // Execution: only Alice has non-null email AND is active
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Where + Select Combined

    [Test]
    public async Task Where_ThenSelect_Tuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1");

        // Execution: 2 active users
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_ThenSelect_Dto()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [IsActive] = 1");

        // Execution: 2 active users as DTOs
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].IsActive, Is.True);
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].IsActive, Is.True);

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].IsActive, Is.True);
        Assert.That(pgResults[1].UserId, Is.EqualTo(2));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(pgResults[1].IsActive, Is.True);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[0].IsActive, Is.True);
        Assert.That(myResults[1].UserId, Is.EqualTo(2));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(myResults[1].IsActive, Is.True);
    }

    #endregion
}
