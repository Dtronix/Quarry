using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class VariableStoredChainTests : CrossDialectTestBase
{
    #region Batch Insert — Variable Stored

    [Test]
    public void BatchInsert_VariableStored_ToDiagnostics()
    {
        // Chain split: InsertBatch in one statement, Values+terminal in another
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var sql = batch.Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public void BatchInsert_VariableStored_ToSql()
    {
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var sql = batch.Values(new[] { new User { UserName = "a", IsActive = true } }).ToSql();

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_VariableStored_ExecuteNonQueryAsync()
    {
        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        await batch.Values(users).ExecuteNonQueryAsync();

        Assert.That(Connection.LastCommand!.CommandText, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1), (@p2, @p3)"));
    }

    [Test]
    public void BatchInsert_TwoHopVariable_ToDiagnostics()
    {
        // Two-hop pattern: InsertBatch -> var batch -> batch.Values -> var exec -> exec.ToSql
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(new[] { new User { UserName = "a", IsActive = true } });
        var sql = exec.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public void BatchInsert_TwoHopVariable_ToSql()
    {
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(new[] { new User { UserName = "a", IsActive = true } });
        var sql = exec.ToSql();

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public void BatchInsert_SingleColumn_VariableStored_ToDiagnostics()
    {
        var batch = Lite.Users().InsertBatch(u => u.UserName);
        var sql = batch.Values(new[] { new User { UserName = "a" } }).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\") VALUES (@p0) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_TwoHopVariable_ExecuteNonQueryAsync()
    {
        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(users);
        await exec.ExecuteNonQueryAsync();

        Assert.That(Connection.LastCommand!.CommandText, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1), (@p2, @p3)"));
    }

    #endregion

    #region Query — Variable Stored

    [Test]
    public void Query_VariableStored_ToDiagnostics()
    {
        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => u.UserName).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public void Query_VariableStored_SelectAndExecute()
    {
        var query = Lite.Users().Where(u => u.IsActive);
        var diag = query.Select(u => new { u.UserName, u.Email }).ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("\"IsActive\""));
        Assert.That(diag.Sql, Does.Contain("\"UserName\""));
        Assert.That(diag.Sql, Does.Contain("\"Email\""));
    }

    #endregion
}
