using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectBatchInsertTests : CrossDialectTestBase
{
    #region Batch Insert ToDiagnostics

    [Test]
    public void BatchInsert_MultiColumn_ToDiagnostics()
    {
        var liteSql = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;
        var pgSql = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;
        var mySql = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;
        var ssSql = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");
    }

    [Test]
    public void BatchInsert_SingleColumn_ToDiagnostics()
    {
        var liteSql = Lite.Users().InsertBatch(u => u.UserName).Values(new[] { new User { UserName = "a" } }).ToDiagnostics().Sql;
        var pgSql = Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" } }).ToDiagnostics().Sql;
        var mySql = My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" } }).ToDiagnostics().Sql;
        var ssSql = Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" } }).ToDiagnostics().Sql;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
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
        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        await Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        await Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new Pg.User { UserName = u.UserName, IsActive = u.IsActive })).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        await My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new My.User { UserName = u.UserName, IsActive = u.IsActive })).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        await Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users.Select(u => new Ss.User { UserName = u.UserName, IsActive = u.IsActive })).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1), (@p2, @p3)",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2), ($3, $4)",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?), (?, ?)",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1), (@p2, @p3)");
    }

    [Test]
    public async Task BatchInsert_SingleColumn_ExecuteNonQueryAsync()
    {
        await Lite.Users().InsertBatch(u => u.UserName).Values(new[] { new User { UserName = "a" }, new User { UserName = "b" }, new User { UserName = "c" } }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Users().InsertBatch(u => u.UserName).Values(new[] { new Pg.User { UserName = "a" }, new Pg.User { UserName = "b" }, new Pg.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Users().InsertBatch(u => u.UserName).Values(new[] { new My.User { UserName = "a" }, new My.User { UserName = "b" }, new My.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Users().InsertBatch(u => u.UserName).Values(new[] { new Ss.User { UserName = "a" }, new Ss.User { UserName = "b" }, new Ss.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0), (@p1), (@p2)",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1), ($2), ($3)",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?), (?), (?)",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0), (@p1), (@p2)");
    }

    #endregion

    #region Batch Insert ExecuteScalarAsync

    [Test]
    public async Task BatchInsert_ExecuteScalarAsync_ReturnsIdentity()
    {
        Connection.ScalarResult = 42;

        await Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).ExecuteScalarAsync<int>();
        var ssSql = Connection.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(liteSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""), "SQLite");
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]"), "SqlServer");
        });
    }

    #endregion

    #region Batch Insert ToSql

    [Test]
    public void BatchInsert_ToSql_SingleEntity()
    {
        var liteSql = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;
        var pgSql = Pg.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Pg.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;
        var mySql = My.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new My.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;
        var ssSql = Ss.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(new[] { new Ss.User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");
    }

    [Test]
    public void BatchInsert_ToDiagnostics_MultipleEntities_ShowsSingleRowTemplate()
    {
        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var liteSql = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ToDiagnostics().Sql;

        // ToDiagnostics returns the single-row template SQL, not the expanded multi-row SQL
        Assert.That(liteSql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    #endregion

    #region Batch Insert Empty Collection

    [Test]
    public void BatchInsert_EmptyCollection_ThrowsOnExecution()
    {
        var builder = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(Array.Empty<User>());

        Assert.ThrowsAsync<ArgumentException>(() => builder.ExecuteNonQueryAsync());
    }

    #endregion

    #region Batch Insert Runtime Fallback

    [Test]
    public void InsertBatch_WithoutGenerator_ThrowsInvalidOperationException()
    {
        // EntityAccessor.InsertBatch throws because batch insert requires source generation
        var accessor = new Quarry.EntityAccessor<User>(SqlDialect.SQLite, "users", null, null!);

        Assert.Throws<InvalidOperationException>(() => accessor.InsertBatch(u => u.UserName));
    }

    #endregion
}
