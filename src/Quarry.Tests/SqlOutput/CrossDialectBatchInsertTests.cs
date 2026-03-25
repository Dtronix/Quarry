using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectBatchInsertTests
{
    #region Batch Insert ToDiagnostics

    [Test]
    public async Task BatchInsert_MultiColumn_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).Prepare();
        var pg   = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).Prepare();
        var my   = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).Prepare();
        var ss   = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");
    }

    [Test]
    public async Task BatchInsert_SingleColumn_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().InsertBatch(u => u.UserName).Values(new[] { new User { UserName = "a" } }).Prepare();
        var pg   = Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" } }).Prepare();
        var my   = My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" } }).Prepare();
        var ss   = Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" } }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0) OUTPUT INSERTED.[UserId]");
    }

    #endregion

    #region Batch Insert ExecuteNonQueryAsync

    [Test]
    public async Task BatchInsert_MultiColumn_ExecuteNonQueryAsync()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };

        var liteSql = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).Prepare();
        var pg      = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new Pg.User { UserName = u.UserName, IsActive = u.IsActive })).Prepare();
        var my      = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new My.User { UserName = u.UserName, IsActive = u.IsActive })).Prepare();
        var ss      = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new Ss.User { UserName = u.UserName, IsActive = u.IsActive })).Prepare();

        QueryTestHarness.AssertDialects(
            liteSql.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");

        // Verify real execution against SQLite — include CreatedAt to satisfy NOT NULL constraint
        var now = DateTime.UtcNow;
        var liteUsers = new[] { new User { UserName = "a", IsActive = true, CreatedAt = now }, new User { UserName = "b", IsActive = false, CreatedAt = now } };
        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(liteUsers).Prepare();
        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));
    }

    [Test]
    public async Task BatchInsert_SingleColumn_ExecuteNonQueryAsync()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var liteSql = Lite.Users().InsertBatch(u => u.UserName).Values(new[] { new User { UserName = "a" }, new User { UserName = "b" }, new User { UserName = "c" } }).Prepare();
        var pg      = Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" }, new Pg.User { UserName = "b" }, new Pg.User { UserName = "c" } }).Prepare();
        var my      = My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" }, new My.User { UserName = "b" }, new My.User { UserName = "c" } }).Prepare();
        var ss      = Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" }, new Ss.User { UserName = "b" }, new Ss.User { UserName = "c" } }).Prepare();

        QueryTestHarness.AssertDialects(
            liteSql.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0) OUTPUT INSERTED.[UserId]");

        // Verify real execution against SQLite — include required columns
        var now = DateTime.UtcNow;
        var liteUsers = new[] { new User { UserName = "a", CreatedAt = now }, new User { UserName = "b", CreatedAt = now }, new User { UserName = "c", CreatedAt = now } };
        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.CreatedAt)).Values(liteUsers).Prepare();
        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(3));
    }

    #endregion

    #region Batch Insert ExecuteScalarAsync

    [Test]
    public async Task BatchInsert_ExecuteScalarAsync_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var liteSql = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).Prepare();
        var pg      = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).Prepare();
        var my      = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).Prepare();
        var ss      = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).Prepare();

        QueryTestHarness.AssertDialects(
            liteSql.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");

        // Verify real execution against SQLite — include CreatedAt to satisfy NOT NULL constraint
        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(new[] { new User { UserName = "a", IsActive = true, CreatedAt = DateTime.UtcNow } }).Prepare();
        var newId = await lite.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));
    }

    #endregion

    #region Batch Insert ToSql

    [Test]
    public async Task BatchInsert_ToSql_SingleEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).Prepare();
        var pg   = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).Prepare();
        var my   = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).Prepare();
        var ss   = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");
    }

    [Test]
    public async Task BatchInsert_ToDiagnostics_MultipleEntities_ShowsSingleRowTemplate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var Lite = t.Lite;

        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var lite = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).Prepare();

        // ToDiagnostics returns the single-row template SQL, not the expanded multi-row SQL
        Assert.That(lite.ToDiagnostics().Sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    #endregion

    #region Batch Insert Empty Collection

    [Test]
    public async Task BatchInsert_EmptyCollection_ThrowsOnExecution()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var Lite = t.Lite;

        var builder = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(Array.Empty<User>());

        Assert.ThrowsAsync<ArgumentException>(() => builder.ExecuteNonQueryAsync());
    }

    #endregion
}
