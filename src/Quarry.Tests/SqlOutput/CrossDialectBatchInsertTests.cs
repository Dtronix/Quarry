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

        var lt = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).Prepare();
        var pg = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).Prepare();
        var my = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).Prepare();
        var ss = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
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

        var lt = Lite.Users().InsertBatch(u => u.UserName).Values(new[] { new User { UserName = "a" } }).Prepare();
        var pg = Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" } }).Prepare();
        var my = My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" } }).Prepare();
        var ss = Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" } }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
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

        var now = DateTime.UtcNow;
        var users = new[] { new User { UserName = "a", IsActive = true, CreatedAt = now }, new User { UserName = "b", IsActive = false, CreatedAt = now } };

        var lt = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users).Prepare();
        var pg = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users.Select(u => new Pg.User { UserName = u.UserName, IsActive = u.IsActive, CreatedAt = u.CreatedAt })).Prepare();
        var my = My.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users.Select(u => new My.User { UserName = u.UserName, IsActive = u.IsActive, CreatedAt = u.CreatedAt })).Prepare();
        var ss = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(users.Select(u => new Ss.User { UserName = u.UserName, IsActive = u.IsActive, CreatedAt = u.CreatedAt })).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));
    }

    [Test]
    public async Task BatchInsert_SingleColumn_ExecuteNonQueryAsync()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var now = DateTime.UtcNow;
        var users = new[] { new User { UserName = "a", CreatedAt = now }, new User { UserName = "b", CreatedAt = now }, new User { UserName = "c", CreatedAt = now } };

        var lt = Lite.Users().InsertBatch(u => (u.UserName, u.CreatedAt)).Values(users).Prepare();
        var pg = Pg.Users().InsertBatch(u => (u.UserName, u.CreatedAt)).Values(users.Select(u => new Pg.User { UserName = u.UserName, CreatedAt = u.CreatedAt })).Prepare();
        var my = My.Users().InsertBatch(u => (u.UserName, u.CreatedAt)).Values(users.Select(u => new My.User { UserName = u.UserName, CreatedAt = u.CreatedAt })).Prepare();
        var ss = Ss.Users().InsertBatch(u => (u.UserName, u.CreatedAt)).Values(users.Select(u => new Ss.User { UserName = u.UserName, CreatedAt = u.CreatedAt })).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"CreatedAt\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"CreatedAt\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `CreatedAt`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [CreatedAt]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(3));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(3));
    }

    #endregion

    #region Batch Insert ExecuteScalarAsync

    [Test]
    public async Task BatchInsert_ExecuteScalarAsync_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(new[] { new User { UserName = "a", IsActive = true, CreatedAt = DateTime.UtcNow } }).Prepare();
        var pg = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(new[] { new Pg.User { UserName = "a", IsActive = true, CreatedAt = DateTime.UtcNow } }).Prepare();
        var my = My.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(new[] { new My.User { UserName = "a", IsActive = true, CreatedAt = DateTime.UtcNow } }).Prepare();
        var ss = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(new[] { new Ss.User { UserName = "a", IsActive = true, CreatedAt = DateTime.UtcNow } }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));
    }

    #endregion

    #region Batch Insert ToSql

    [Test]
    public async Task BatchInsert_ToSql_SingleEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).Prepare();
        var pg = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).Prepare();
        var my = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).Prepare();
        var ss = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
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
        var (Lite, Pg, My, Ss) = t;

        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var lt = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).Prepare();

        // ToDiagnostics returns the single-row template SQL, not the expanded multi-row SQL
        Assert.That(lt.ToDiagnostics().Sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    #endregion

    #region Batch Insert Empty Collection

    [Test]
    public async Task BatchInsert_EmptyCollection_ThrowsOnExecution()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var builder = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(Array.Empty<User>());

        Assert.ThrowsAsync<ArgumentException>(() => builder.ExecuteNonQueryAsync());
    }

    #endregion
}
