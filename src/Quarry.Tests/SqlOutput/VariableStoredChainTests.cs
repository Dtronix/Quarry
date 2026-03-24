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
        var sql = batch.Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;

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
        var sql = exec.ToDiagnostics().Sql;

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
    public void BatchInsert_TwoHopVariable_MultiRow_ToDiagnostics()
    {
        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(users);
        var sql = exec.ToDiagnostics().Sql;

        // ToDiagnostics returns single-row template SQL
        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    #endregion

    #region Batch Insert — Variable Stored with ExecuteNonQueryAsync

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
    public void Query_VariableStored_SingleColumnSelect()
    {
        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => u.UserName).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public void Query_VariableStored_EntitySelect()
    {
        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => u).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public void Query_VariableStored_TupleSelect()
    {
        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => (u.UserId, u.UserName)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public void Query_TwoHopVariable_ToDiagnostics()
    {
        // Two-hop: query variable → filtered query variable → select + terminal
        var query = Lite.Users().Where(u => u.IsActive);
        var filtered = query.Where(u => u.UserId > 0);
        var sql = filtered.Select(u => u.UserName).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public void Query_VariableStored_ConditionalWhere_Active()
    {
        var query = Lite.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.Select(u => u.UserName).ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("\"IsActive\""));
        Assert.That(diag.Sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public void Query_VariableStored_ConditionalWhere_Inactive()
    {
        var query = Lite.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.Select(u => u.UserName).ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("\"IsActive\""));
        Assert.That(diag.Sql, Does.Contain("\"UserName\""));
    }

    #endregion
}
