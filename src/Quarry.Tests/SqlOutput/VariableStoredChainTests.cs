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
    public void BatchInsert_TwoHopVariable_ToDiagnostics()
    {
        // Two-hop pattern: InsertBatch -> var batch -> batch.Values -> var exec -> exec.ToDiagnostics
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
    public void BatchInsert_TwoHopVariable_ExecuteNonQueryAsync_ToDiagnostics()
    {
        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(users);
        var sql = exec.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1), (@p2, @p3) RETURNING \"UserId\""));
    }

    #endregion
}
