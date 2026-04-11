using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class VariableStoredChainTests
{
    #region Batch Insert — Variable Stored

    [Test]
    public async Task BatchInsert_VariableStored_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Chain split: InsertBatch in one statement, Values+terminal in another
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var sql = batch.Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_VariableStored_ToSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var sql = batch.Values(new[] { new User { UserName = "a", IsActive = true } }).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_TwoHopVariable_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Two-hop pattern: InsertBatch -> var batch -> batch.Values -> var exec -> exec.ToDiagnostics
        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(new[] { new User { UserName = "a", IsActive = true } });
        var sql = exec.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_TwoHopVariable_ToSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var batch = Lite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(new[] { new User { UserName = "a", IsActive = true } });
        var sql = exec.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_SingleColumn_VariableStored_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var batch = Lite.Users().InsertBatch(u => u.UserName);
        var sql = batch.Values(new[] { new User { UserName = "a" } }).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\") VALUES (@p0) RETURNING \"UserId\""));
    }

    [Test]
    public async Task BatchInsert_TwoHopVariable_MultiRow_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

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
        await using var t = await QueryTestHarness.CreateAsync();
        // Use a SQLite context on MockConnection for execution tests that inspect LastCommand
        using var mockLite = new TestDbContext(t.MockConnection);

        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var batch = mockLite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        await batch.Values(users).ExecuteNonQueryAsync();

        Assert.That(t.MockConnection.LastCommand!.CommandText, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1), (@p2, @p3)"));
    }

    [Test]
    public async Task BatchInsert_TwoHopVariable_ExecuteNonQueryAsync()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        // Use a SQLite context on MockConnection for execution tests that inspect LastCommand
        using var mockLite = new TestDbContext(t.MockConnection);

        var users = new[] { new User { UserName = "a", IsActive = true }, new User { UserName = "b", IsActive = false } };
        var batch = mockLite.Users().InsertBatch(u => (u.UserName, u.IsActive));
        var exec = batch.Values(users);
        await exec.ExecuteNonQueryAsync();

        Assert.That(t.MockConnection.LastCommand!.CommandText, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1), (@p2, @p3)"));
    }

    #endregion

    #region Query — Variable Stored

    [Test]
    public async Task Query_VariableStored_SingleColumnSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => u.UserName).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public async Task Query_VariableStored_EntitySelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => u).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public async Task Query_VariableStored_TupleSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var query = Lite.Users().Where(u => u.IsActive);
        var sql = query.Select(u => (u.UserId, u.UserName)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public async Task Query_TwoHopVariable_ToDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Two-hop: query variable → filtered query variable → select + terminal
        var query = Lite.Users().Where(u => u.IsActive);
        var filtered = query.Where(u => u.UserId > 0);
        var sql = filtered.Select(u => u.UserName).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
    }

    [Test]
    public async Task Query_VariableStored_ConditionalWhere_Active()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

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
    public async Task Query_VariableStored_ConditionalWhere_Inactive()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var query = Lite.Users().Where(u => true);
#pragma warning disable CS0162 // Unreachable code — intentional: tests inactive conditional branch
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
#pragma warning restore CS0162
        var diag = query.Select(u => u.UserName).ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("\"IsActive\""));
        Assert.That(diag.Sql, Does.Contain("\"UserName\""));
    }

    #endregion
}
