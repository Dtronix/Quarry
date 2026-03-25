using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.Integration;

/// <summary>
/// Cross-dialect tests for .Prepare() — verifies that single-terminal collapse
/// and multi-terminal paths produce correct SQL across all dialects and correct
/// results against a real SQLite database.
/// </summary>
[TestFixture]
internal class PrepareIntegrationTests
{
    #region Single-Terminal Collapse — Select Execution

    [Test]
    public async Task Prepare_SingleTerminal_ExecuteFetchAll_ReturnsCorrectData()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg   = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my   = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss   = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Prepare_SingleTerminal_ExecuteFetchFirst_ReturnsCorrectData()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var pg   = Pg.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var my   = My.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var ss   = Ss.Users().Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var result = await lite.ExecuteFetchFirstAsync();
        Assert.That(result, Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Multi-Terminal — Select

    [Test]
    public async Task Prepare_MultiTerminal_DiagnosticsAndFetchAll()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg   = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my   = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss   = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Prepare_MultiTerminal_ToSqlAndFetchAll()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var pg   = Pg.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var my   = My.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var ss   = Ss.Users().Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Prepare_MultiTerminal_DiagnosticsAndToSql_SameSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg   = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my   = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss   = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1");
    }

    #endregion

    #region Multi-Terminal — Delete

    [Test]
    public async Task Prepare_Delete_MultiTerminal_DiagnosticsAndExecute()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Delete().Where(u => u.UserId == 999).Prepare();
        var pg   = Pg.Users().Delete().Where(u => u.UserId == 999).Prepare();
        var my   = My.Users().Delete().Where(u => u.UserId == 999).Prepare();
        var ss   = Ss.Users().Delete().Where(u => u.UserId == 999).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" = 999",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" = 999",
            mysql:  "DELETE FROM `users` WHERE `UserId` = 999",
            ss:     "DELETE FROM [users] WHERE [UserId] = 999");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Multi-Terminal — Update

    [Test]
    public async Task Prepare_Update_MultiTerminal_DiagnosticsAndExecute()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 999).Prepare();
        var pg   = Pg.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 999).Prepare();
        var my   = My.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 999).Prepare();
        var ss   = Ss.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 999).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'Updated' WHERE \"UserId\" = 999",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'Updated' WHERE \"UserId\" = 999",
            mysql:  "UPDATE `users` SET `UserName` = 'Updated' WHERE `UserId` = 999",
            ss:     "UPDATE [users] SET [UserName] = 'Updated' WHERE [UserId] = 999");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Multi-Terminal — Batch Insert

    [Test]
    public async Task Prepare_BatchInsert_MultiTerminal_DiagnosticsAndToSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var users = new List<User>
        {
            new() { UserName = "PrepBatch1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new() { UserName = "PrepBatch2", IsActive = false, CreatedAt = DateTime.UtcNow }
        };

        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users).Prepare();
        var pg   = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users.Select(u => new Pg.User { UserName = u.UserName, IsActive = u.IsActive, CreatedAt = u.CreatedAt })).Prepare();
        var my   = My.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users.Select(u => new My.User { UserName = u.UserName, IsActive = u.IsActive, CreatedAt = u.CreatedAt })).Prepare();
        var ss   = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users.Select(u => new Ss.User { UserName = u.UserName, IsActive = u.IsActive, CreatedAt = u.CreatedAt })).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]");
    }

    #endregion
}
