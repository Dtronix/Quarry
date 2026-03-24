namespace Quarry.Tests.Integration;

/// <summary>
/// Integration tests for .Prepare() — verifies that single-terminal collapse
/// and multi-terminal paths produce correct results against a real SQLite database.
/// </summary>
[TestFixture]
internal class PrepareIntegrationTests : SqliteIntegrationTestBase
{
    #region Single-Terminal Collapse — Select Execution

    [Test]
    public async Task Prepare_SingleTerminal_ExecuteFetchAll_ReturnsCorrectData()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var results = await prepared.ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Prepare_SingleTerminal_ExecuteFetchFirst_ReturnsCorrectData()
    {
        var prepared = Db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var result = await prepared.ExecuteFetchFirstAsync();

        Assert.That(result, Is.EqualTo((1, "Alice")));
    }

    [Test]
    public void Prepare_SingleTerminal_ToDiagnostics_ProducesValidSql()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var diag = prepared.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("SELECT"));
        Assert.That(diag.Sql, Does.Contain("WHERE"));
        Assert.That(diag.Sql, Does.Contain("\"IsActive\""));
    }

    #endregion

    #region Multi-Terminal — Select

    [Test]
    public async Task Prepare_MultiTerminal_DiagnosticsAndFetchAll()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var results = await prepared.ExecuteFetchAllAsync();

        Assert.That(diag.Sql, Does.Contain("SELECT"));
        Assert.That(diag.Sql, Does.Contain("WHERE"));
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Prepare_MultiTerminal_ToSqlAndFetchAll()
    {
        var prepared = Db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var sql = prepared.ToSql();
        var results = await prepared.ExecuteFetchAllAsync();

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Prepare_MultiTerminal_DiagnosticsAndToSql_SameSql()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var sql = prepared.ToSql();

        Assert.That(sql, Is.EqualTo(diag.Sql));
    }

    #endregion

    #region Multi-Terminal — Delete

    [Test]
    public async Task Prepare_Delete_MultiTerminal_DiagnosticsAndExecute()
    {
        // Use a condition that matches no rows to avoid modifying test data
        var prepared = Db.Users()
            .Delete().Where(u => u.UserId == 999)
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var affected = await prepared.ExecuteNonQueryAsync();

        Assert.That(diag.Sql, Does.Contain("DELETE"));
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Multi-Terminal — Update

    [Test]
    public async Task Prepare_Update_MultiTerminal_DiagnosticsAndExecute()
    {
        // Use a condition that matches no rows to avoid modifying test data
        var prepared = Db.Users()
            .Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 999)
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var affected = await prepared.ExecuteNonQueryAsync();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion
}
