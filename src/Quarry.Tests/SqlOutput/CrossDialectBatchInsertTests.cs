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

        QueryTestHarness.AssertDialects(
            Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
            Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
            My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
            Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
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

        QueryTestHarness.AssertDialects(
            Lite.Users().InsertBatch(u => u.UserName).Values(new[] { new User { UserName = "a" } }).ToDiagnostics().Sql,
            Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" } }).ToDiagnostics().Sql,
            My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" } }).ToDiagnostics().Sql,
            Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" } }).ToDiagnostics().Sql,
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
        var conn = t.MockConnection;

        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };

        await Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new Pg.User { UserName = u.UserName, IsActive = u.IsActive })).ExecuteNonQueryAsync();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new My.User { UserName = u.UserName, IsActive = u.IsActive })).ExecuteNonQueryAsync();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new Ss.User { UserName = u.UserName, IsActive = u.IsActive })).ExecuteNonQueryAsync();
        var ssSql = conn.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2), ($3, $4)"), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?), (?, ?)"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1), (@p2, @p3)"), "SqlServer");
        });

        // Verify real execution against SQLite — include CreatedAt to satisfy NOT NULL constraint
        var now = DateTime.UtcNow;
        var liteUsers = new[] { new User { UserName = "a", IsActive = true, CreatedAt = now }, new User { UserName = "b", IsActive = false, CreatedAt = now } };
        var affected = await Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(liteUsers).ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));
    }

    [Test]
    public async Task BatchInsert_SingleColumn_ExecuteNonQueryAsync()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;
        var conn = t.MockConnection;

        await Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" }, new Pg.User { UserName = "b" }, new Pg.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" }, new My.User { UserName = "b" }, new My.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" }, new Ss.User { UserName = "b" }, new Ss.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var ssSql = conn.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\") VALUES ($1), ($2), ($3)"), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`) VALUES (?), (?), (?)"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName]) VALUES (@p0), (@p1), (@p2)"), "SqlServer");
        });

        // Verify real execution against SQLite — include required columns
        var now = DateTime.UtcNow;
        var liteUsers = new[] { new User { UserName = "a", CreatedAt = now }, new User { UserName = "b", CreatedAt = now }, new User { UserName = "c", CreatedAt = now } };
        var affected = await Lite.Users().InsertBatch(u => (u.UserName, u.CreatedAt)).Values(liteUsers).ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(3));
    }

    #endregion

    #region Batch Insert ExecuteScalarAsync

    [Test]
    public async Task BatchInsert_ExecuteScalarAsync_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;
        var conn = t.MockConnection;
        conn.ScalarResult = 42;

        await Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var ssSql = conn.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]"), "SqlServer");
        });

        // Verify real execution against SQLite — include CreatedAt to satisfy NOT NULL constraint
        var newId = await Lite.Users().InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt)).Values(new[] { new User { UserName = "a", IsActive = true, CreatedAt = DateTime.UtcNow } }).ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));
    }

    #endregion

    #region Batch Insert ToSql

    [Test]
    public async Task BatchInsert_ToSql_SingleEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
            Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
            My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
            Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql,
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
        var liteSql = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ToDiagnostics().Sql;

        // ToDiagnostics returns the single-row template SQL, not the expanded multi-row SQL
        Assert.That(liteSql, Is.EqualTo(
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
